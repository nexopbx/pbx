// SPDX-License-Identifier: AGPL-3.0-only
using System.Reflection;
using System.Runtime.InteropServices;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NexoPBX.Abstractions.Security;
using NexoPBX.Abstractions.Time;
using NexoPBX.Contracts.V1;
using NexoPBX.Store.Repositories;

namespace NexoPBX.Api.Services;

/// <summary>Implements <c>nexopbx.v1.SystemService</c>.</summary>
[Authorize(Policy = Scopes.SystemRead)]
public sealed class SystemGrpcService : SystemService.SystemServiceBase
{
    private readonly HostInfo _host;
    private readonly IStoreMigrator _store;
    private readonly HealthCheckService _health;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    public SystemGrpcService(HostInfo host, IStoreMigrator store, HealthCheckService health, IClock clock)
    {
        _host = host;
        _store = store;
        _health = health;
        _clock = clock;
    }

    /// <inheritdoc />
    public override Task<SystemInfo> GetInfo(GetInfoRequest request, ServerCallContext context)
    {
        var version = typeof(SystemGrpcService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(SystemGrpcService).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        var now = _clock.UtcNow;
        return Task.FromResult(new SystemInfo
        {
            Product = "NexoPBX",
            Version = version.Split('+')[0],
            RunMode = _host.RunMode,
            DatabaseProvider = _store.Provider,
            Os = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            StartedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(_host.StartedAtUtc, DateTimeKind.Utc)),
            Uptime = Duration.FromTimeSpan(now - _host.StartedAtUtc),
        });
    }

    /// <inheritdoc />
    public override async Task<NexoPBX.Contracts.V1.HealthReport> GetHealth(GetHealthRequest request, ServerCallContext context)
    {
        var report = await _health.CheckHealthAsync(context.CancellationToken);
        var result = new NexoPBX.Contracts.V1.HealthReport { Status = Map(report.Status) };
        foreach (var (name, entry) in report.Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            result.Checks.Add(new NexoPBX.Contracts.V1.HealthCheckResult { Name = name, Status = Map(entry.Status), Description = entry.Description ?? string.Empty });
        }

        return result;
    }

    private static NexoPBX.Contracts.V1.HealthStatus Map(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus status) => status switch
    {
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy => NexoPBX.Contracts.V1.HealthStatus.Healthy,
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded => NexoPBX.Contracts.V1.HealthStatus.Degraded,
        _ => NexoPBX.Contracts.V1.HealthStatus.Unhealthy,
    };
}
