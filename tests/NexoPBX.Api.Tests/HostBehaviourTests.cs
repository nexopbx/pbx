// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexoPBX.App;

namespace NexoPBX.Api.Tests;

public sealed class HostBehaviourTests
{
    private static async Task<T> WithHostAsync<T>(HostFixture host, Func<HostFixture, Task<T>> test)
    {
        await host.InitializeAsync();
        try
        {
            return await test(host);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Login_attempts_are_rate_limited_per_client_address()
    {
        var statuses = await WithHostAsync(new HostFixture().With("api:login_rate_limit_per_minute", "3"), async host =>
        {
            using var http = host.NewHttpClient();
            var list = new List<HttpStatusCode>();
            for (var i = 0; i < 6; i++)
            {
                list.Add((await http.PostAsJsonAsync("/v1/auth:login", new { username = "owner", password = "wrong" })).StatusCode);
            }

            return list;
        });

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Shutdown_is_prompt_even_with_a_live_event_stream_open()
    {
        var elapsed = await WithHostAsync(new HostFixture().With("server:shutdown_timeout_seconds", "20"), async host =>
        {
            var token = await host.LoginAsOwnerAsync();
            using var http = host.NewHttpClient(token);
            using var stream = await http.GetAsync("/v1/events/stream", HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, stream.StatusCode);

            var watch = Stopwatch.StartNew();
            await host.StopAsync();
            return watch.Elapsed;
        });

        Assert.True(elapsed < TimeSpan.FromSeconds(8), $"Stopping took {elapsed}, so the open stream held up the drain.");
    }

    [Fact]
    public async Task Without_migrations_the_host_is_alive_but_not_ready()
    {
        var host = new HostFixture { BootstrapOwner = false }.With("database:auto_migrate", "false");

        var (live, ready) = await WithHostAsync(host, async h =>
        {
            using var http = h.NewHttpClient();
            return ((await http.GetAsync("/health/live")).StatusCode, (await http.GetAsync("/health/ready")).StatusCode);
        });

        Assert.Equal(HttpStatusCode.OK, live);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready);
    }

    [Fact]
    public async Task A_fresh_database_is_migrated_automatically_on_first_start()
    {
        var status = await WithHostAsync(new HostFixture(), async h =>
        {
            using var http = h.NewHttpClient();
            return (await http.GetAsync("/health/ready")).StatusCode;
        });

        Assert.Equal(HttpStatusCode.OK, status);
    }
}

public sealed class InstanceLockTests
{
    [Fact]
    public void Only_one_instance_can_hold_a_data_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"nexopbx-lock-{Guid.NewGuid():N}");
        try
        {
            using var first = InstanceLock.TryAcquire(dir);
            Assert.NotNull(first);
            Assert.Null(InstanceLock.TryAcquire(dir));

            first.Dispose();
            using var again = InstanceLock.TryAcquire(dir);
            Assert.NotNull(again);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>Configuration tests touch process-wide environment variables, so they must not run in parallel with each other.</summary>
[Collection("environment")]
public sealed class ConfigurationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"nexopbx-cfg-{Guid.NewGuid():N}");

    public ConfigurationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NEXOPBX__SERVER__HTTP_LISTEN", null);
        Environment.SetEnvironmentVariable("NEXOPBX__DATABASE__AUTO_MIGRATE", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private NexoPaths Paths() => new(Path.Combine(_dir, "nexopbx.yaml"), Path.Combine(_dir, "data"));

    private IServiceProvider Build(Action<IConfigurationBuilder>? configure = null) => NexoHost.Build(Paths(), configure: configure).Services;

    [Fact]
    public void Defaults_are_safe_loopback_only()
    {
        var server = Build().GetRequiredService<IOptions<ServerOptions>>().Value;

        Assert.StartsWith("127.0.0.1:", server.HttpListen);
        Assert.StartsWith("127.0.0.1:", server.GrpcListen);
        Assert.Empty(server.HttpsListen);
        Assert.False(server.IpcEnabled);
    }

    [Fact]
    public void Yaml_keys_in_snake_case_bind_to_options()
    {
        File.WriteAllText(Paths().ConfigFile, """
            server:
              http_listen: 127.0.0.1:18080
              shutdown_timeout_seconds: 7
            database:
              auto_migrate: false
            logging:
              level: Debug
              format: text
            """);

        var sp = Build();

        Assert.Equal("127.0.0.1:18080", sp.GetRequiredService<IOptions<ServerOptions>>().Value.HttpListen);
        Assert.Equal(7, sp.GetRequiredService<IOptions<ServerOptions>>().Value.ShutdownTimeoutSeconds);
        Assert.False(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.AutoMigrate);
        Assert.Equal("Debug", sp.GetRequiredService<IOptions<LoggingOptions>>().Value.Level);
    }

    [Fact]
    public void Environment_overrides_yaml_and_the_caller_overrides_environment()
    {
        File.WriteAllText(Paths().ConfigFile, "server:\n  http_listen: 127.0.0.1:18080\n");
        Environment.SetEnvironmentVariable("NEXOPBX__SERVER__HTTP_LISTEN", "127.0.0.1:18081");
        Environment.SetEnvironmentVariable("NEXOPBX__DATABASE__AUTO_MIGRATE", "false");

        var fromEnv = Build().GetRequiredService<IOptions<ServerOptions>>().Value;
        Assert.Equal("127.0.0.1:18081", fromEnv.HttpListen);

        var overridden = Build(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["server:http_listen"] = "127.0.0.1:18082" }));
        Assert.Equal("127.0.0.1:18082", overridden.GetRequiredService<IOptions<ServerOptions>>().Value.HttpListen);
        Assert.False(overridden.GetRequiredService<IOptions<DatabaseOptions>>().Value.AutoMigrate);
    }

    [Theory]
    [InlineData("database:provider", "oracle")]
    [InlineData("database:provider", "postgres")] // without a connection string
    [InlineData("server:http_listen", "not-an-address")]
    [InlineData("server:https_listen", "127.0.0.1:18443")] // without a certificate
    public void Bad_configuration_fails_fast_with_a_clear_message(string key, string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Build(c => c.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void The_sample_configuration_file_only_uses_keys_that_exist_and_loads_cleanly()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexoPBX.slnx")))
        {
            dir = dir.Parent;
        }

        var sample = Path.Combine(dir!.FullName, "deploy", "nexopbx.sample.yaml");
        var sections = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["server"] = typeof(ServerOptions),
            ["database"] = typeof(DatabaseOptions),
            ["logging"] = typeof(LoggingOptions),
            ["auth"] = typeof(NexoPBX.Auth.AuthOptions),
            ["api"] = typeof(NexoPBX.Api.ApiOptions),
        };

        var raw = new ConfigurationBuilder().AddYamlFile(sample, optional: false).Build();
        var unknown = new List<string>();
        foreach (var section in raw.GetChildren())
        {
            if (!sections.TryGetValue(section.Key, out var type))
            {
                unknown.Add(section.Key);
                continue;
            }

            var names = type.GetProperties().Select(property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            unknown.AddRange(section.GetChildren()
                .Where(key => !names.Contains(key.Key.Replace("_", string.Empty, StringComparison.Ordinal)))
                .Select(key => $"{section.Key}:{key.Key}"));
        }

        Assert.True(unknown.Count == 0, $"Unknown keys in deploy/nexopbx.sample.yaml: {string.Join(", ", unknown)}");

        // And the whole file is accepted by the real startup path, with the documented values.
        var options = NexoHost.Build(new NexoPaths(sample, Path.Combine(_dir, "data"))).Services;
        Assert.Equal("127.0.0.1:8080", options.GetRequiredService<IOptions<ServerOptions>>().Value.HttpListen);
        Assert.Equal(TimeSpan.FromMinutes(15), options.GetRequiredService<IOptions<NexoPBX.Auth.AuthOptions>>().Value.LockoutDuration);
    }

    [Fact]
    public void Paths_resolve_with_explicit_over_environment_over_default()
    {
        Environment.SetEnvironmentVariable(NexoPaths.DataDirEnvVar, Path.Combine(_dir, "from-env"));
        try
        {
            Assert.Equal(Path.Combine(_dir, "from-env"), NexoPaths.Resolve(false).DataDirectory);
            Assert.Equal(Path.GetFullPath(Path.Combine(_dir, "explicit")), NexoPaths.Resolve(false, dataOverride: Path.Combine(_dir, "explicit")).DataDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(NexoPaths.DataDirEnvVar, null);
        }

        var service = NexoPaths.Resolve(true);
        Assert.Contains(OperatingSystem.IsWindows() ? "NexoPBX" : "nexopbx", service.ConfigFile, StringComparison.Ordinal);
    }
}

/// <summary>The command-line verbs, run in-process against a brand-new data directory (the first-install path).</summary>
[Collection("environment")]
public sealed class CliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"nexopbx-cli-{Guid.NewGuid():N}");

    public CliTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("NEXOPBX_BOOTSTRAP_PASSWORD", "correct horse battery staple");
        Environment.SetEnvironmentVariable("NEXOPBX__AUTH__ARGON2_MEMORY_KIB", "64");
        Environment.SetEnvironmentVariable("NEXOPBX__AUTH__ARGON2_ITERATIONS", "1");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NEXOPBX_BOOTSTRAP_PASSWORD", null);
        Environment.SetEnvironmentVariable("NEXOPBX__AUTH__ARGON2_MEMORY_KIB", null);
        Environment.SetEnvironmentVariable("NEXOPBX__AUTH__ARGON2_ITERATIONS", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private NexoPaths Paths() => new(Path.Combine(_dir, "nexopbx.yaml"), Path.Combine(_dir, "data"));

    [Fact]
    public async Task First_install_flow_works_without_running_db_migrate_by_hand()
    {
        var paths = Paths();

        Assert.Equal(0, await Cli.UserBootstrapAsync(paths, "admin", null, passwordFromStdin: false, default));
        Assert.Equal(2, await Cli.UserBootstrapAsync(paths, "second", null, passwordFromStdin: false, default)); // only once
        Assert.Equal(0, await Cli.TokenCreateAsync(paths, "admin", "cli-token", ["system.read"], 0, default));
        Assert.Equal(2, await Cli.TokenCreateAsync(paths, "nobody", "x", ["system.read"], 0, default));
        Assert.Equal(2, await Cli.TokenCreateAsync(paths, "admin", "x", ["bogus.scope"], 0, default));
        Assert.Equal(0, await Cli.DbStatusAsync(paths, default));
        Assert.Equal(0, await Cli.DbMigrateAsync(paths, default)); // idempotent
    }

    [Fact]
    public async Task With_auto_migrate_off_the_cli_explains_instead_of_crashing()
    {
        Environment.SetEnvironmentVariable("NEXOPBX__DATABASE__AUTO_MIGRATE", "false");
        try
        {
            Assert.Equal(2, await Cli.UserBootstrapAsync(Paths(), "admin", null, passwordFromStdin: false, default));
            Assert.Equal(2, await Cli.DbStatusAsync(Paths(), default)); // pending migrations => non-zero
            Assert.Equal(0, await Cli.DbMigrateAsync(Paths(), default));
            Assert.Equal(0, await Cli.UserBootstrapAsync(Paths(), "admin", null, passwordFromStdin: false, default));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXOPBX__DATABASE__AUTO_MIGRATE", null);
        }
    }

    [Fact]
    public async Task A_missing_password_is_a_usage_error()
    {
        Environment.SetEnvironmentVariable("NEXOPBX_BOOTSTRAP_PASSWORD", null);

        Assert.Equal(1, await Cli.UserBootstrapAsync(Paths(), "admin", null, passwordFromStdin: false, default));
    }
}
