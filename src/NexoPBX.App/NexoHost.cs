// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NexoPBX.Abstractions.Events;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Time;
using NexoPBX.Api;
using NexoPBX.Auth;
using NexoPBX.Store.Postgres;
using NexoPBX.Store.Sqlite;
using Serilog;
using Serilog.Formatting.Compact;

namespace NexoPBX.App;

/// <summary>The composition root: the only place that knows every implementation (ARCHITECTURE §4.1).</summary>
public static class NexoHost
{
    /// <summary>Prefix of environment variables, e.g. <c>NEXOPBX__DATABASE__PROVIDER=postgres</c>.</summary>
    public const string EnvPrefix = "NEXOPBX__";

    /// <summary>Builds the application (without starting it) for the given paths and run mode.</summary>
    /// <param name="paths">Resolved configuration and data locations.</param>
    /// <param name="runMode">"headless" or "desktop"; reported by the API.</param>
    /// <param name="serviceMode">True when launched as a Windows Service or systemd unit.</param>
    /// <param name="configure">Hook to add configuration sources with the highest precedence (tests, CLI).</param>
    public static WebApplication Build(NexoPaths paths, string runMode = "headless", bool serviceMode = false, Action<IConfigurationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.DataDirectory);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Configuration precedence, lowest to highest: built-in defaults < YAML file < environment < caller override.
        // Keys are written in snake_case (http_listen) but bound to PascalCase properties, so underscores are removed
        // once, here: the binder matches names case-insensitively.
        var raw = new ConfigurationBuilder();
        raw.AddYamlFile(paths.ConfigFile, optional: true, reloadOnChange: false);
        raw.AddEnvironmentVariables(EnvPrefix);
        configure?.Invoke(raw);

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(raw.Build().AsEnumerable()
            .Where(kv => kv.Value is not null)
            .Select(kv => new KeyValuePair<string, string?>(kv.Key.Replace("_", string.Empty, StringComparison.Ordinal), kv.Value)));

        ConfigureLogging(builder);
        ConfigureServices(builder, paths, runMode);
        ConfigureKestrel(builder, paths);

        if (serviceMode)
        {
            builder.Host.UseWindowsService(o => o.ServiceName = "NexoPBX");
            builder.Host.UseSystemd();
        }

        var shutdownSeconds = (builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions()).ShutdownTimeoutSeconds;
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(Math.Clamp(shutdownSeconds, 1, 600)));

        var app = builder.Build();
        app.UseNexoApi();
        app.MapNexoApi();
        return app;
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(LoggingOptions.SectionName).Get<LoggingOptions>() ?? new LoggingOptions();
        var level = Enum.TryParse<Serilog.Events.LogEventLevel>(options.Level, ignoreCase: true, out var parsed)
            ? parsed
            : Serilog.Events.LogEventLevel.Information;

        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext();

        logger = string.Equals(options.Format, "text", StringComparison.OrdinalIgnoreCase)
            ? logger.WriteTo.Console()
            : logger.WriteTo.Console(new CompactJsonFormatter());

        if (!string.IsNullOrWhiteSpace(options.File))
        {
            logger = logger.WriteTo.File(new CompactJsonFormatter(), options.File, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
        }

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(logger.CreateLogger(), dispose: true);
    }

    private static void ConfigureServices(WebApplicationBuilder builder, NexoPaths paths, string runMode)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services.Configure<ServerOptions>(config.GetSection(ServerOptions.SectionName));
        services.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.SectionName));
        services.Configure<LoggingOptions>(config.GetSection(LoggingOptions.SectionName));

        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddSingleton<IEventBus>(sp => new InMemoryEventBus(sp.GetRequiredService<IClock>(), sp.GetRequiredService<IIdGenerator>()));
        services.AddSingleton(new HostInfo(runMode, DateTime.UtcNow));

        var database = config.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        switch (database.Provider.Trim().ToLowerInvariant())
        {
            case "sqlite":
                var connection = string.IsNullOrWhiteSpace(database.ConnectionString)
                    ? $"Data Source={Path.Combine(paths.DataDirectory, "nexopbx.db")}"
                    : database.ConnectionString;
                services.AddNexoSqliteStore(connection);
                break;
            case "postgres" or "postgresql":
                if (string.IsNullOrWhiteSpace(database.ConnectionString))
                {
                    throw new InvalidOperationException("database.connection_string is required when database.provider is 'postgres'.");
                }

                services.AddNexoPostgresStore(database.ConnectionString);
                break;
            default:
                throw new InvalidOperationException($"Unknown database.provider '{database.Provider}'. Use 'sqlite' or 'postgres'.");
        }

        ConfigureDataProtection(services, paths);
        services.AddNexoAuth(config);
        services.AddNexoApi(config);
        services.AddHostedService<HostLifecycleService>();
    }

    private static void ConfigureDataProtection(IServiceCollection services, NexoPaths paths)
    {
        // Keys that encrypt secrets at rest (TOTP seeds, later integration credentials). They live in the data
        // directory, readable only by the service account, and on Windows are additionally sealed with DPAPI.
        var keyDirectory = Directory.CreateDirectory(Path.Combine(paths.DataDirectory, "keys"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyDirectory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var dataProtection = services.AddDataProtection()
            .SetApplicationName("NexoPBX")
            .PersistKeysToFileSystem(keyDirectory);
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }
    }

    private static void ConfigureKestrel(WebApplicationBuilder builder, NexoPaths paths)
    {
        var server = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
            kestrel.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);

            if (TryParseEndpoint(server.HttpListen, out var http))
            {
                kestrel.Listen(http, o => o.Protocols = HttpProtocols.Http1);
            }

            if (TryParseEndpoint(server.GrpcListen, out var grpc))
            {
                kestrel.Listen(grpc, o => o.Protocols = HttpProtocols.Http2);
            }

            if (TryParseEndpoint(server.HttpsListen, out var https))
            {
                var certificate = LoadCertificate(server);
                kestrel.Listen(https, o =>
                {
                    o.Protocols = HttpProtocols.Http1AndHttp2;
                    o.UseHttps(certificate);
                });
            }

            if (server.IpcEnabled)
            {
                if (OperatingSystem.IsWindows())
                {
                    kestrel.ListenNamedPipe("nexopbx", o => o.Protocols = HttpProtocols.Http2);
                }
                else
                {
                    var socket = Path.Combine(paths.DataDirectory, "nexopbx.sock");
                    File.Delete(socket);
                    kestrel.ListenUnixSocket(socket, o => o.Protocols = HttpProtocols.Http2);
                }
            }
        });
    }

    private static X509Certificate2 LoadCertificate(ServerOptions server)
    {
        if (string.IsNullOrWhiteSpace(server.CertificatePath))
        {
            throw new InvalidOperationException("server.certificate_path is required when server.https_listen is set.");
        }

        var password = string.IsNullOrEmpty(server.CertificatePassword) ? null : server.CertificatePassword;
        var extension = Path.GetExtension(server.CertificatePath);
        if (extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(server.CertificatePath, password);
        }

        var keyPath = string.IsNullOrWhiteSpace(server.CertificateKeyPath) ? null : server.CertificateKeyPath;
        var pem = X509Certificate2.CreateFromPemFile(server.CertificatePath, keyPath);

        // Windows needs the key to be persisted to be usable by Schannel.
        return OperatingSystem.IsWindows()
            ? X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pfx), null)
            : pem;
    }

    private static bool TryParseEndpoint(string value, out IPEndPoint endpoint)
    {
        endpoint = new IPEndPoint(IPAddress.Loopback, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (IPEndPoint.TryParse(value.Trim(), out var parsed))
        {
            endpoint = parsed;
            return true;
        }

        throw new InvalidOperationException($"'{value}' is not a valid listen address. Use host:port, for example 127.0.0.1:8080 or [::1]:8080.");
    }
}
