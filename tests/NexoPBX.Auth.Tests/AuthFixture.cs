// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NexoPBX.Abstractions.Events;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Time;
using NexoPBX.Store;
using NexoPBX.Store.Repositories;
using NexoPBX.Store.Sqlite;
using NexoPBX.Testing;

namespace NexoPBX.Auth.Tests;

/// <summary>A real SQLite database, the real auth services and a clock the test controls.</summary>
public sealed class AuthFixture : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"nexopbx-auth-{Guid.NewGuid():N}.db");

    public VirtualClock Clock { get; } = new();

    public ServiceProvider Services { get; private set; } = null!;

    public IEventBus Bus => Services.GetRequiredService<IEventBus>();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(Clock);
        services.AddNexoSqliteStore($"Data Source={_path}");
        services.AddNexoAuth();
        services.Configure<AuthOptions>(o =>
        {
            // Cheap hashing keeps the suite fast; production defaults are covered by a dedicated test.
            o.Argon2MemoryKiB = 64;
            o.Argon2Iterations = 1;
        });
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        Services = services.BuildServiceProvider();

        using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<IStoreMigrator>().MigrateAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }

    /// <summary>A new scope, like one HTTP request: its own DbContext and tenant context.</summary>
    public IServiceScope NewScope() => Services.CreateScope();

    /// <summary>Runs an action in a fresh scope.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = NewScope();
        return await action(scope.ServiceProvider);
    }

    public Task<NexoPBX.Store.Entities.User> CreateUserAsync(string name, NexoPBX.Abstractions.Security.Role role, string password = "correct horse battery") =>
        InScopeAsync(sp => sp.GetRequiredService<AuthService>().CreateUserAsync(name, name, password, role, null, default));
}
