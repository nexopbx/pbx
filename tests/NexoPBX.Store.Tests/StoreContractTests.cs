// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.DependencyInjection;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Security;
using NexoPBX.Store.Entities;
using NexoPBX.Store.Repositories;
using Xunit;

namespace NexoPBX.Store.Tests;

/// <summary>The behaviours every provider must offer identically (ADR-007). Subclasses only choose the provider.</summary>
public abstract class StoreContractTests : IAsyncLifetime
{
    private static readonly Guid OtherTenant = new("00000000-0000-0000-0000-0000000000b2");
    private readonly GuidV7IdGenerator _ids = new();
    private IStoreProvider? _provider;

    /// <summary>When set, every test is reported as skipped with this reason (provider not available here).</summary>
    protected virtual string? SkipReason => null;

    protected abstract IStoreProvider CreateProvider();

    protected IServiceProvider Services =>
        SkipReason is { } reason ? throw new SkipException(reason) : _provider!.Services;

    public async Task InitializeAsync()
    {
        if (SkipReason is not null)
        {
            return;
        }

        _provider = CreateProvider();
        await _provider.InitializeAsync();
        using var scope = Services.ScopeFor(TenantIds.Default);
        await scope.ServiceProvider.GetRequiredService<IStoreMigrator>().MigrateAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }

    private User NewUser(string name, Role role = Role.Agent) => new()
    {
        Id = _ids.NewId(),
        Username = name,
        DisplayName = name,
        PasswordHash = "hash",
        Role = role,
        CreatedAt = DateTime.UtcNow,
    };

    [SkippableFact]
    public async Task Migrations_are_applied_and_the_default_tenant_exists()
    {
        using var scope = Services.ScopeFor(TenantIds.Default);
        var migrator = scope.ServiceProvider.GetRequiredService<IStoreMigrator>();

        Assert.True(await migrator.CanConnectAsync(CancellationToken.None));
        Assert.Empty(await migrator.GetPendingMigrationsAsync(CancellationToken.None));

        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tenant = Assert.Single(db.Tenants.ToList());
        Assert.Equal(TenantIds.Default, tenant.Id);
    }

    [SkippableFact]
    public async Task User_roundtrip_is_case_insensitive_and_keeps_utc()
    {
        var created = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        using (var scope = Services.ScopeFor(TenantIds.Default))
        {
            var user = NewUser("Alice", Role.Admin);
            user.CreatedAt = created;
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(user, default);
        }

        using var read = Services.ScopeFor(TenantIds.Default);
        var found = await read.ServiceProvider.GetRequiredService<IUserRepository>().FindByUsernameAsync("  aLiCe ", default);

        Assert.NotNull(found);
        Assert.Equal(Role.Admin, found.Role);
        Assert.Equal(created, found.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, found.CreatedAt.Kind);
        Assert.Equal(TenantIds.Default, found.TenantId);
    }

    [SkippableFact]
    public async Task Duplicate_username_is_rejected_regardless_of_case()
    {
        using var scope = Services.ScopeFor(TenantIds.Default);
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        await users.AddAsync(NewUser("bob"), default);

        await Assert.ThrowsAsync<DuplicateException>(() => users.AddAsync(NewUser("BOB"), default));
    }

    [SkippableFact]
    public async Task Tenants_cannot_see_each_other()
    {
        using (var scope = Services.ScopeFor(TenantIds.Default))
        {
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(NewUser("carol"), default);
        }

        using var other = Services.ScopeFor(OtherTenant);
        var users = other.ServiceProvider.GetRequiredService<IUserRepository>();

        Assert.Null(await users.FindByUsernameAsync("carol", default));
        Assert.Equal(0, await users.CountAsync(default));

        // The same username is free in another tenant.
        await users.AddAsync(NewUser("carol"), default);
        Assert.Equal(1, await users.CountAsync(default));
    }

    [SkippableFact]
    public async Task Writing_a_row_for_a_foreign_tenant_is_refused()
    {
        using var scope = Services.ScopeFor(TenantIds.Default);
        var user = NewUser("mallory");
        user.TenantId = OtherTenant;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(user, default));
    }

    [SkippableFact]
    public async Task Version_increases_and_stale_updates_conflict()
    {
        Guid id;
        using (var scope = Services.ScopeFor(TenantIds.Default))
        {
            var user = NewUser("dave");
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(user, default);
            id = user.Id;
        }

        using var s1 = Services.ScopeFor(TenantIds.Default);
        using var s2 = Services.ScopeFor(TenantIds.Default);
        var r1 = s1.ServiceProvider.GetRequiredService<IUserRepository>();
        var r2 = s2.ServiceProvider.GetRequiredService<IUserRepository>();
        var first = (await r1.FindByIdAsync(id, default))!;
        var second = (await r2.FindByIdAsync(id, default))!;
        var startVersion = first.Version;

        first.DisplayName = "Dave One";
        await r1.UpdateAsync(first, startVersion, default);
        Assert.Equal(startVersion + 1, first.Version);

        second.DisplayName = "Dave Two";
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => r2.UpdateAsync(second, startVersion, default));

        using var check = Services.ScopeFor(TenantIds.Default);
        Assert.Equal("Dave One", (await check.ServiceProvider.GetRequiredService<IUserRepository>().FindByIdAsync(id, default))!.DisplayName);
    }

    [SkippableFact]
    public async Task Listing_pages_through_every_user_exactly_once()
    {
        using var scope = Services.ScopeFor(TenantIds.Default);
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        for (var i = 0; i < 5; i++)
        {
            await users.AddAsync(NewUser($"user{i}"), default);
        }

        var seen = new List<Guid>();
        string? token = null;
        var pages = 0;
        do
        {
            var page = await users.ListAsync(2, token, default);
            seen.AddRange(page.Items.Select(u => u.Id));
            token = page.NextPageToken;
            pages++;
        }
        while (token is not null);

        Assert.Equal(3, pages);
        Assert.Equal(5, seen.Distinct().Count());
        Assert.Equal(seen.Select(g => g.ToString("D")).Order(StringComparer.Ordinal), seen.Select(g => g.ToString("D")));
    }

    [SkippableFact]
    public async Task A_garbage_page_token_is_a_clear_error()
    {
        using var scope = Services.ScopeFor(TenantIds.Default);
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        await Assert.ThrowsAsync<ArgumentException>(() => users.ListAsync(10, "not-a-token", default));
    }

    [SkippableFact]
    public async Task Tokens_can_be_found_by_hash_and_revoked_in_bulk()
    {
        var userId = _ids.NewId();
        var now = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        using var scope = Services.ScopeFor(TenantIds.Default);
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenRepository>();
        foreach (var n in new[] { "a", "b" })
        {
            await tokens.AddAsync(new ApiToken
            {
                Id = _ids.NewId(),
                UserId = userId,
                Name = n,
                TokenHash = new string(n[0], 64),
                Prefix = "nxp_" + n,
                Scopes = Scopes.SystemRead,
                Kind = TokenKind.Api,
                CreatedAt = now,
            }, default);
        }

        var found = await tokens.FindByHashAsync(new string('a', 64), default);
        Assert.Equal("a", found!.Name);

        Assert.Equal(2, await tokens.RevokeAllForUserAsync(userId, now.AddHours(1), default));
        Assert.Equal(0, await tokens.RevokeAllForUserAsync(userId, now.AddHours(2), default));
        Assert.All(await tokens.ListByUserAsync(userId, default), t => Assert.NotNull(t.RevokedAt));
    }

    [SkippableFact]
    public async Task Audit_records_list_newest_first()
    {
        using var scope = Services.ScopeFor(TenantIds.Default);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditRepository>();
        for (var i = 0; i < 3; i++)
        {
            await audit.AppendAsync(new AuditEvent
            {
                Id = _ids.NewId(),
                At = new DateTime(2026, 1, 1, 0, 0, i, DateTimeKind.Utc),
                Action = $"test.{i}",
                Outcome = AuditOutcome.Success,
            }, default);
        }

        var page = await audit.ListAsync(10, null, default);
        Assert.Equal(["test.2", "test.1", "test.0"], page.Items.Select(a => a.Action));
        Assert.Null(page.NextPageToken);
    }

    [SkippableFact]
    public async Task A_date_of_unspecified_kind_is_refused()
    {
        using var scope = Services.ScopeFor(TenantIds.Default);
        var user = NewUser("erin");
        user.CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(user, default));
    }
}

public sealed class SqliteStoreTests : StoreContractTests
{
    protected override IStoreProvider CreateProvider() => new SqliteStoreProvider();

    [SkippableFact]
    public async Task Parallel_writers_are_serialised_instead_of_failing_with_database_locked()
    {
        var tasks = Enumerable.Range(0, 40).Select(async i =>
        {
            using var scope = Services.ScopeFor(TenantIds.Default);
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(
                new User { Id = Guid.CreateVersion7(), Username = $"p{i}", DisplayName = "p", PasswordHash = "h", CreatedAt = DateTime.UtcNow }, default);
        });

        await Task.WhenAll(tasks);

        using var read = Services.ScopeFor(TenantIds.Default);
        Assert.Equal(40, await read.ServiceProvider.GetRequiredService<IUserRepository>().CountAsync(default));
    }
}

public sealed class PostgresStoreTests : StoreContractTests
{
    protected override string? SkipReason =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable(PostgresStoreProvider.EnvVar))
            ? $"Set {PostgresStoreProvider.EnvVar} to a PostgreSQL admin connection string to run these tests."
            : null;

    protected override IStoreProvider CreateProvider() => new PostgresStoreProvider();
}
