// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Sockets;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexoPBX.Abstractions.Security;
using NexoPBX.App;
using NexoPBX.Auth;
using NexoPBX.Contracts.V1;

namespace NexoPBX.Api.Tests;

/// <summary>
/// A complete NexoPBX host (real Kestrel, real SQLite file, real HTTP/1.1 and HTTP/2 listeners) on free loopback
/// ports. Tests talk to it exactly like a client would, so Kestrel configuration, middleware order, authentication,
/// error mapping and serialisation are all exercised together.
/// </summary>
public sealed class HostFixture : IAsyncLifetime
{
    public const string OwnerPassword = "correct horse battery staple";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"nexopbx-api-{Guid.NewGuid():N}");
    private readonly Dictionary<string, string?> _overrides = [];
    private WebApplication _app = null!;

    /// <summary>Set false before InitializeAsync for hosts whose schema is deliberately missing.</summary>
    public bool BootstrapOwner { get; set; } = true;

    public int HttpPort { get; private set; }

    public int GrpcPort { get; private set; }

    public Uri HttpUrl => new($"http://127.0.0.1:{HttpPort}");

    public Uri GrpcUrl => new($"http://127.0.0.1:{GrpcPort}");

    public IServiceProvider Services => _app.Services;

    /// <summary>Adds configuration before the host starts (call from a subclass or before InitializeAsync).</summary>
    public HostFixture With(string key, string value)
    {
        _overrides[key] = value;
        return this;
    }

    public async Task InitializeAsync()
    {
        HttpPort = FreePort();
        GrpcPort = FreePort();
        Directory.CreateDirectory(_dir);
        var config = new Dictionary<string, string?>
        {
            ["server:http_listen"] = $"127.0.0.1:{HttpPort}",
            ["server:grpc_listen"] = $"127.0.0.1:{GrpcPort}",
            ["database:provider"] = "sqlite",
            ["logging:level"] = "Warning",
            ["logging:format"] = "text",
            ["auth:argon2_memory_kib"] = "64",
            ["auth:argon2_iterations"] = "1",
        };
        foreach (var (key, value) in _overrides)
        {
            config[key] = value;
        }

        _app = NexoHost.Build(
            new NexoPaths(Path.Combine(_dir, "nexopbx.yaml"), Path.Combine(_dir, "data")),
            configure: c => c.AddInMemoryCollection(config));
        await _app.StartAsync();

        if (BootstrapOwner)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AuthService>().BootstrapOwnerAsync("owner", "Owner", OwnerPassword, default);
        }
    }

    /// <summary>Stops the host now (tests of shutdown behaviour); disposing later is then a no-op.</summary>
    public Task StopAsync() => _app.StopAsync();

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best effort: the OS temp directory is cleaned eventually
        }
    }

    public HttpClient NewHttpClient(string? token = null)
    {
        var client = new HttpClient { BaseAddress = HttpUrl, Timeout = TimeSpan.FromSeconds(30) };
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>Native gRPC over HTTP/2 cleartext.</summary>
    public GrpcChannel NewGrpcChannel() => GrpcChannel.ForAddress(GrpcUrl);

    /// <summary>gRPC-Web over HTTP/1.1, as a browser would.</summary>
    public GrpcChannel NewGrpcWebChannel() => GrpcChannel.ForAddress(HttpUrl, new GrpcChannelOptions
    {
        HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler()),
        HttpVersion = HttpVersion.Version11,
        HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
    });

    public static Metadata Bearer(string token) => new() { { "authorization", $"Bearer {token}" } };

    /// <summary>Logs in over gRPC and returns the session token.</summary>
    public async Task<string> LoginAsync(string username, string password, string? totp = null)
    {
        using var channel = NewGrpcChannel();
        var response = await new UserService.UserServiceClient(channel).LoginAsync(new LoginRequest { Username = username, Password = password, TotpCode = totp ?? string.Empty });
        return response.Token;
    }

    public Task<string> LoginAsOwnerAsync() => LoginAsync("owner", OwnerPassword);

    /// <summary>Creates a user directly through the service layer and returns a session token for it.</summary>
    public async Task<string> CreateUserAndLoginAsync(string username, Abstractions.Security.Role role)
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthService>().CreateUserAsync(username, username, OwnerPassword, role, null, default);
        }

        return await LoginAsync(username, OwnerPassword);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
