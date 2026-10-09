// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NexoPBX.Store.Repositories;

namespace NexoPBX.Api.Infrastructure;

/// <summary>Ready only when the database answers and its schema is fully migrated.</summary>
public sealed class StoreHealthCheck : IHealthCheck
{
    private readonly IStoreMigrator _store;

    /// <summary>Creates the check.</summary>
    public StoreHealthCheck(IStoreMigrator store) => _store = store;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _store.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("The database cannot be reached.");
            }

            var pending = await _store.GetPendingMigrationsAsync(cancellationToken);
            return pending.Count > 0
                ? HealthCheckResult.Unhealthy($"{pending.Count} database migration(s) pending. Run 'nexopbxd db migrate'.")
                : HealthCheckResult.Healthy($"{_store.Provider} database is up to date.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database check failed.", ex);
        }
    }
}
