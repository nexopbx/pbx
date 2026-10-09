// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.App;

/// <summary>Network listeners (section <c>server</c>). Loopback by default: nothing is exposed until you say so.</summary>
public sealed class ServerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "server";

    /// <summary>HTTP/1.1 listener: REST, gRPC-Web, SSE, health, web UI. Empty disables it.</summary>
    public string HttpListen { get; set; } = "127.0.0.1:8080";

    /// <summary>HTTP/2 cleartext listener for native gRPC clients. Empty disables it.</summary>
    public string GrpcListen { get; set; } = "127.0.0.1:8081";

    /// <summary>HTTPS listener (HTTP/1.1 + HTTP/2). Needs <see cref="CertificatePath"/>. Empty disables it.</summary>
    public string HttpsListen { get; set; } = string.Empty;

    /// <summary>PEM or PFX certificate for <see cref="HttpsListen"/>.</summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>Password of the PFX, or of the encrypted PEM key.</summary>
    public string CertificatePassword { get; set; } = string.Empty;

    /// <summary>Optional separate PEM private key file.</summary>
    public string CertificateKeyPath { get; set; } = string.Empty;

    /// <summary>Local IPC endpoint (named pipe on Windows, Unix socket on Linux) for the desktop app. Off until M8.</summary>
    public bool IpcEnabled { get; set; }

    /// <summary>Seconds to let in-flight work finish on shutdown before connections are cut.</summary>
    public int ShutdownTimeoutSeconds { get; set; } = 30;
}

/// <summary>Database selection (section <c>database</c>).</summary>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "database";

    /// <summary>"sqlite" (default, zero configuration) or "postgres".</summary>
    public string Provider { get; set; } = "sqlite";

    /// <summary>Connection string. For SQLite, empty means <c>nexopbx.db</c> in the data directory. Required for PostgreSQL.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Apply pending migrations at startup. Convenient for SQLite; for PostgreSQL consider running <c>nexopbxd db migrate</c> yourself.</summary>
    public bool AutoMigrate { get; set; } = true;
}

/// <summary>Logging (section <c>logging</c>).</summary>
public sealed class LoggingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "logging";

    /// <summary>Verbose, Debug, Information, Warning, Error or Fatal.</summary>
    public string Level { get; set; } = "Information";

    /// <summary>"json" (structured, default) or "text" (human friendly).</summary>
    public string Format { get; set; } = "json";

    /// <summary>Optional rolling log file (daily, 14 files kept). Empty logs to the console only.</summary>
    public string File { get; set; } = string.Empty;
}
