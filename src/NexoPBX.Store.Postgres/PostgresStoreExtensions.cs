// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NexoPBX.Store.Postgres;

/// <summary>Registers the PostgreSQL store for larger installations (ADR-007).</summary>
public static class PostgresStoreExtensions
{
    /// <summary>Name reported by <see cref="Repositories.IStoreMigrator.Provider"/>.</summary>
    public const string ProviderName = "postgres";

    /// <summary>Adds the store backed by the PostgreSQL database in <paramref name="connectionString"/>.</summary>
    public static IServiceCollection AddNexoPostgresStore(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddNexoStoreCore(ProviderName);
        services.AddDbContext<NexoDbContext>(options => options
            .UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(PostgresStoreExtensions).Assembly.GetName().Name)));
        return services;
    }
}
