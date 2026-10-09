// SPDX-License-Identifier: AGPL-3.0-only
using NexoPBX.Store.Entities;

namespace NexoPBX.Store.Repositories;

/// <summary>A page of results and the token to ask for the next one (null on the last page).</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextPageToken);

/// <summary>Users of the current tenant.</summary>
public interface IUserRepository
{
    /// <summary>Finds a user by id.</summary>
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Finds a user by login name (case-insensitive).</summary>
    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>Lists users ordered by id, page by page.</summary>
    Task<Page<User>> ListAsync(int pageSize, string? pageToken, CancellationToken cancellationToken);

    /// <summary>Number of users in the tenant.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Adds a user. Throws <see cref="DuplicateException"/> if the username is taken.</summary>
    Task AddAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Saves changes to a user previously loaded. If <paramref name="expectedVersion"/> is given it must match
    /// the stored version, otherwise <see cref="ConcurrencyConflictException"/> is thrown.
    /// </summary>
    Task UpdateAsync(User user, long? expectedVersion, CancellationToken cancellationToken);
}

/// <summary>Tokens of the current tenant.</summary>
public interface ITokenRepository
{
    /// <summary>Finds a token by the SHA-256 hash of its value.</summary>
    Task<ApiToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Finds a token by id.</summary>
    Task<ApiToken?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Lists the tokens of a user, newest first.</summary>
    Task<IReadOnlyList<ApiToken>> ListByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Adds a token.</summary>
    Task AddAsync(ApiToken token, CancellationToken cancellationToken);

    /// <summary>Saves changes to a token previously loaded (revocation, last-used).</summary>
    Task UpdateAsync(ApiToken token, CancellationToken cancellationToken);

    /// <summary>Revokes every live token of a user (for example after a password change or deactivation).</summary>
    Task<int> RevokeAllForUserAsync(Guid userId, DateTime now, CancellationToken cancellationToken);
}

/// <summary>Append-only audit trail of the current tenant.</summary>
public interface IAuditRepository
{
    /// <summary>Appends a record.</summary>
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken);

    /// <summary>Lists records newest first.</summary>
    Task<Page<AuditEvent>> ListAsync(int pageSize, string? pageToken, CancellationToken cancellationToken);
}

/// <summary>Applies and inspects schema migrations.</summary>
public interface IStoreMigrator
{
    /// <summary>Name of the database provider ("sqlite" or "postgres").</summary>
    string Provider { get; }

    /// <summary>Applies every pending migration.</summary>
    Task MigrateAsync(CancellationToken cancellationToken);

    /// <summary>Names of migrations not yet applied.</summary>
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken);

    /// <summary>True if the database can be reached right now.</summary>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
