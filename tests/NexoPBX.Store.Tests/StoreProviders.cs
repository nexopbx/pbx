// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.DependencyInjection;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Store.Postgres;
using NexoPBX.Store.Sqlite;
using Npgsql;

namespace NexoPBX.Store.Tests;

/// <summary>A throw-away database on one of the supported providers.</summary>
public interface IStoreProvider : IAsyncDisposable
{
    /// <summary>Service provider with the store registered.</summary>
    ServiceProvider Services { get; }

    /// <summary>Starts the database (creating it if needed).</summary>
    Task InitializeAsync();
}

public sealed class SqliteStoreProvider : IStoreProvider
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"nexopbx-test-{Guid.NewGuid():N}.db");

    public ServiceProvider Services { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Services = new ServiceCollection().AddNexoSqliteStore($"Data Source={_path}").BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }
}

public sealed class PostgresStoreProvider : IStoreProvider
{
    public const string EnvVar = "NEXOPBX_TEST_POSTGRES";
    private readonly string _adminConnection = Environment.GetEnvironmentVariable(EnvVar)!;
    private readonly string _database = $"nexopbx_test_{Guid.NewGuid():N}";

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _database };
        Services = new ServiceCollection().AddNexoPostgresStore(builder.ConnectionString).BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

internal static class ScopeExtensions
{
    public static IServiceScope ScopeFor(this IServiceProvider services, Guid tenantId)
    {
        var scope = services.CreateScope();
        ((TenantContext)scope.ServiceProvider.GetRequiredService<ITenantContext>()).TenantId = tenantId;
        return scope;
    }
}
