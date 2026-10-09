// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NexoPBX.Store.Sqlite;

/// <summary>Registers the SQLite (WAL) store, the zero-configuration default (ADR-007).</summary>
public static class SqliteStoreExtensions
{
    /// <summary>Name reported by <see cref="Repositories.IStoreMigrator.Provider"/>.</summary>
    public const string ProviderName = "sqlite";

    /// <summary>Adds the store backed by the SQLite database in <paramref name="connectionString"/>.</summary>
    public static IServiceCollection AddNexoSqliteStore(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddNexoStoreCore(ProviderName);
        services.AddSingleton(new SqliteWriteGate());
        services.AddDbContext<NexoDbContext>((sp, options) => options
            .UseSqlite(connectionString, b => b.MigrationsAssembly(typeof(SqliteStoreExtensions).Assembly.GetName().Name))
            .AddInterceptors(new SqlitePragmaInterceptor(), sp.GetRequiredService<SqliteWriteGate>()));
        return services;
    }
}
