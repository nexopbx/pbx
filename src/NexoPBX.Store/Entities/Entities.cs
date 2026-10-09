// SPDX-License-Identifier: AGPL-3.0-only
using NexoPBX.Abstractions.Security;

namespace NexoPBX.Store.Entities;

/// <summary>Entities with an optimistic-concurrency version, exposed as <c>etag</c> by the public API.</summary>
public interface IVersioned
{
    /// <summary>Incremented by the store on every update.</summary>
    long Version { get; set; }
}

/// <summary>Base class of every row that belongs to a tenant (DECISIONS D-003).</summary>
public abstract class TenantEntity
{
    /// <summary>Primary key (UUID v7).</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant. Stamped and enforced by <see cref="NexoDbContext"/>.</summary>
    public Guid TenantId { get; set; }
}

/// <summary>A customer of the installation. A fresh install has exactly one: <c>Default</c>.</summary>
public sealed class Tenant
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>A human user of the PBX (admin, supervisor, agent...).</summary>
public sealed class User : TenantEntity, IVersioned
{
    /// <summary>Login name as typed by the user when created.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Lower-invariant form used for uniqueness and lookups.</summary>
    public string NormalizedUsername { get; set; } = string.Empty;

    /// <summary>Name shown in the UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Argon2id hash in PHC-like format. Never the password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Role that decides the user's scopes.</summary>
    public Role Role { get; set; }

    /// <summary>Inactive users cannot log in; their tokens stop working.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>TOTP secret, encrypted at rest by the data-protection layer. Null if never enrolled.</summary>
    public string? TotpSecretProtected { get; set; }

    /// <summary>True once the user confirmed enrolment with a valid code.</summary>
    public bool TotpEnabled { get; set; }

    /// <summary>Last accepted TOTP time step, to refuse code replay.</summary>
    public long TotpLastStep { get; set; }

    /// <summary>Consecutive failed logins since the last success.</summary>
    public int FailedLogins { get; set; }

    /// <summary>Login refused until this instant (UTC), set after too many failures.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Last successful login (UTC).</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <inheritdoc />
    public long Version { get; set; }
}

/// <summary>What a token is for.</summary>
public enum TokenKind
{
    /// <summary>Long-lived, created on purpose for a script or integration.</summary>
    Api = 0,

    /// <summary>Short-lived, issued by a successful interactive login.</summary>
    Session = 1,
}

/// <summary>An opaque bearer token. Only its SHA-256 hash is stored.</summary>
public sealed class ApiToken : TenantEntity
{
    /// <summary>User the token acts as. Null for tokens not tied to a person.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Human label ("grafana", "sample client"...).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the token. Unique.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>First characters of the token, safe to show so people can tell tokens apart.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Space-separated scopes this token may use.</summary>
    public string Scopes { get; set; } = string.Empty;

    /// <summary>Api or Session.</summary>
    public TokenKind Kind { get; set; }

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Expiry (UTC). Null means it never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Set when revoked (UTC).</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Last time the token authenticated a request (UTC, coarse).</summary>
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>Outcome of an audited action.</summary>
public enum AuditOutcome
{
    /// <summary>It happened.</summary>
    Success = 0,

    /// <summary>It was attempted and failed (bad password, conflict...).</summary>
    Failure = 1,

    /// <summary>It was refused for lack of permission.</summary>
    Denied = 2,
}

/// <summary>Append-only record of who did what. Never updated or deleted by the application.</summary>
public sealed class AuditEvent : TenantEntity
{
    /// <summary>When it happened (UTC).</summary>
    public DateTime At { get; set; }

    /// <summary>User that acted, if any.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Token that was used, if any.</summary>
    public Guid? ActorTokenId { get; set; }

    /// <summary>Dotted action name, e.g. <c>user.created</c>, <c>auth.login</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Kind of entity affected.</summary>
    public string? TargetType { get; set; }

    /// <summary>Id of the entity affected.</summary>
    public string? TargetId { get; set; }

    /// <summary>Outcome of the action.</summary>
    public AuditOutcome Outcome { get; set; }

    /// <summary>Caller address, when known.</summary>
    public string? RemoteIp { get; set; }

    /// <summary>Extra details as compact JSON. Never contains secrets.</summary>
    public string? Details { get; set; }
}
