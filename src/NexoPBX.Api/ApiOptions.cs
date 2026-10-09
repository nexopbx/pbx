// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.Api;

/// <summary>Public API settings (section <c>api</c>).</summary>
public sealed class ApiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "api";

    /// <summary>
    /// Browser origins allowed to call the API cross-origin. Empty (default) means none: the web UI is served by the
    /// same server, so it is same-origin. Never use "*".
    /// </summary>
    public string[] CorsAllowedOrigins { get; set; } = [];

    /// <summary>Requests per minute and client address for every endpoint (named-pipe/UDS clients are exempt).</summary>
    public int RateLimitPerMinute { get; set; } = 1200;

    /// <summary>Login attempts per minute and client address.</summary>
    public int LoginRateLimitPerMinute { get; set; } = 10;

    /// <summary>Publishes the OpenAPI document at <c>/openapi/v1.json</c>.</summary>
    public bool EnableOpenApi { get; set; } = true;
}

/// <summary>Facts about the running host that the API reports. Registered by the application.</summary>
/// <param name="RunMode">"headless" or "desktop".</param>
/// <param name="StartedAtUtc">When the host started.</param>
public sealed record HostInfo(string RunMode, DateTime StartedAtUtc);
