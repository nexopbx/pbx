// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.Abstractions.Identity;

/// <summary>Well-known tenant identifiers. Every row carries a tenant id from day one (DECISIONS D-003).</summary>
public static class TenantIds
{
    /// <summary>The single tenant exposed by the UI until multi-tenant management is needed.</summary>
    public static readonly Guid Default = new("00000000-0000-0000-0000-000000000001");
}

/// <summary>The tenant the current operation runs for. Scoped per request / per operation.</summary>
public interface ITenantContext
{
    /// <summary>Tenant used to filter every query and stamp every new row.</summary>
    Guid TenantId { get; }
}

/// <summary>Mutable tenant context, set once by the authentication layer (or by a CLI command).</summary>
public sealed class TenantContext : ITenantContext
{
    /// <inheritdoc />
    public Guid TenantId { get; set; } = TenantIds.Default;
}
