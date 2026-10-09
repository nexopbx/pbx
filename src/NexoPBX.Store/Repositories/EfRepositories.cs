// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Text;
using Microsoft.EntityFrameworkCore;
using NexoPBX.Store.Entities;

namespace NexoPBX.Store.Repositories;

internal static class PageTokens
{
    public static Guid? Decode(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            var bytes = Base64Url.DecodeFromChars(token);
            return bytes.Length == 16 ? new Guid(bytes) : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid page token.", nameof(token));
        }
    }

    public static string Encode(Guid id) => Base64Url.EncodeToString(id.ToByteArray());
}

internal sealed class EfUserRepository : IUserRepository
{
    private readonly NexoDbContext _db;

    public EfUserRepository(NexoDbContext db) => _db = db;

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var normalized = Normalize(username);
        return _db.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, cancellationToken);
    }

    public async Task<Page<User>> ListAsync(int pageSize, string? pageToken, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var after = PageTokens.Decode(pageToken);
        var query = _db.Users.AsNoTracking().OrderBy(u => u.Id).AsQueryable();
        if (after is { } afterId)
        {
            query = query.Where(u => u.Id.CompareTo(afterId) > 0);
        }

        var items = await query.Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new Page<User>(items, hasMore ? PageTokens.Encode(items[^1].Id) : null);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) => _db.Users.CountAsync(cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        user.NormalizedUsername = Normalize(user.Username);
        if (await _db.Users.AnyAsync(u => u.NormalizedUsername == user.NormalizedUsername, cancellationToken))
        {
            throw new DuplicateException($"Username '{user.Username}' is already taken.");
        }

        await _db.Users.AddAsync(user, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _db.Entry(user).State = EntityState.Detached;
            throw new DuplicateException($"Username '{user.Username}' is already taken.", ex);
        }
    }

    public async Task UpdateAsync(User user, long? expectedVersion, CancellationToken cancellationToken)
    {
        var entry = _db.Entry(user);
        if (expectedVersion is { } expected)
        {
            if (user.Version != expected)
            {
                throw new ConcurrencyConflictException($"User {user.Id} was modified by someone else.");
            }
        }

        entry.Property(u => u.NormalizedUsername).CurrentValue = Normalize(user.Username);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException($"User {user.Id} was modified by someone else.", ex);
        }
    }

    public static string Normalize(string username) => username.Trim().ToLowerInvariant();
}

internal sealed class EfTokenRepository : ITokenRepository
{
    private readonly NexoDbContext _db;

    public EfTokenRepository(NexoDbContext db) => _db = db;

    public Task<ApiToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        _db.ApiTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public Task<ApiToken?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ApiToken>> ListByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.ApiTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ApiToken token, CancellationToken cancellationToken)
    {
        await _db.ApiTokens.AddAsync(token, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ApiToken token, CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RevokeAllForUserAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var live = await _db.ApiTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in live)
        {
            token.RevokedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return live.Count;
    }
}

internal sealed class EfAuditRepository : IAuditRepository
{
    private readonly NexoDbContext _db;

    public EfAuditRepository(NexoDbContext db) => _db = db;

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await _db.AuditEvents.AddAsync(auditEvent, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Page<AuditEvent>> ListAsync(int pageSize, string? pageToken, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var before = PageTokens.Decode(pageToken);
        var query = _db.AuditEvents.AsNoTracking().OrderByDescending(a => a.Id).AsQueryable();
        if (before is { } beforeId)
        {
            query = query.Where(a => a.Id.CompareTo(beforeId) < 0);
        }

        var items = await query.Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new Page<AuditEvent>(items, hasMore ? PageTokens.Encode(items[^1].Id) : null);
    }
}

internal sealed class EfStoreMigrator : IStoreMigrator
{
    private readonly NexoDbContext _db;

    public EfStoreMigrator(NexoDbContext db, string provider)
    {
        _db = db;
        Provider = provider;
    }

    public string Provider { get; }

    public Task MigrateAsync(CancellationToken cancellationToken) => _db.Database.MigrateAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken) =>
        (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => _db.Database.CanConnectAsync(cancellationToken);
}
