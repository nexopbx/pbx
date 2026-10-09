// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.Auth;

/// <summary>Tunable authentication settings (section <c>auth</c> of the configuration file).</summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "auth";

    /// <summary>Argon2id memory cost in KiB. Default follows the OWASP minimum (19 MiB); tune with a benchmark.</summary>
    public int Argon2MemoryKiB { get; set; } = 19 * 1024;

    /// <summary>Argon2id iterations.</summary>
    public int Argon2Iterations { get; set; } = 2;

    /// <summary>Argon2id parallelism.</summary>
    public int Argon2Parallelism { get; set; } = 1;

    /// <summary>Shortest accepted password.</summary>
    public int MinPasswordLength { get; set; } = 12;

    /// <summary>Longest accepted password (bounds hashing cost).</summary>
    public int MaxPasswordLength { get; set; } = 256;

    /// <summary>Failed logins in a row before the account is locked.</summary>
    public int MaxFailedLogins { get; set; } = 5;

    /// <summary>How long a locked account stays locked.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Lifetime of a session token issued by a login.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Name shown by authenticator apps for TOTP.</summary>
    public string TotpIssuer { get; set; } = "NexoPBX";
}
