// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Claims;
using Grpc.Core;
using NexoPBX.Abstractions.Security;
using NexoPBX.Auth.Http;

namespace NexoPBX.Api.Infrastructure;

/// <summary>The authenticated caller of a gRPC call or HTTP request, read from its claims.</summary>
public sealed class Caller
{
    private readonly ClaimsPrincipal _principal;

    private Caller(ClaimsPrincipal principal) => _principal = principal;

    /// <summary>The tenant the caller belongs to.</summary>
    public Guid TenantId => Guid.Parse(_principal.FindFirstValue(NexoClaims.Tenant)!);

    /// <summary>The token used for this call.</summary>
    public Guid TokenId => Guid.Parse(_principal.FindFirstValue(NexoClaims.TokenId)!);

    /// <summary>The user the token acts as, if any.</summary>
    public Guid? UserId => Guid.TryParse(_principal.FindFirstValue(NexoClaims.UserId), out var id) ? id : null;

    /// <summary>The user's role, if any.</summary>
    public Role? Role => Enum.TryParse<Role>(_principal.FindFirstValue(NexoClaims.Role), out var r) ? r : null;

    /// <summary>The user id, or throws a precondition error for tokens not tied to a user.</summary>
    public Guid RequireUserId() => UserId ?? throw ApiErrors.Create(StatusCode.FailedPrecondition, "TOKEN_HAS_NO_USER", "This token is not tied to a user.");

    /// <summary>True if the caller currently holds the scope.</summary>
    public bool Has(string scope) => _principal.HasClaim(NexoClaims.Scope, scope);

    /// <summary>All scopes the caller currently holds.</summary>
    public IReadOnlySet<string> Scopes => _principal.FindAll(NexoClaims.Scope).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>Remote address as text, for audit. Null for local pipe/socket clients.</summary>
    public string? RemoteIp { get; private init; }

    /// <summary>Reads the caller from a gRPC call context.</summary>
    public static Caller From(ServerCallContext context)
    {
        var http = context.GetHttpContext();
        return new Caller(http.User) { RemoteIp = http.Connection.RemoteIpAddress?.ToString() };
    }

    /// <summary>Reads the caller from an HTTP request.</summary>
    public static Caller From(Microsoft.AspNetCore.Http.HttpContext http) =>
        new(http.User) { RemoteIp = http.Connection.RemoteIpAddress?.ToString() };
}
