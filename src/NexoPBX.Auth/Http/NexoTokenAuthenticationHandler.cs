// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using NexoPBX.Auth.Tokens;

namespace NexoPBX.Auth.Http;

/// <summary>Claim types used inside NexoPBX principals.</summary>
public static class NexoClaims
{
    /// <summary>One claim per granted scope.</summary>
    public const string Scope = "nexopbx:scope";

    /// <summary>Tenant id.</summary>
    public const string Tenant = "nexopbx:tenant";

    /// <summary>Token id.</summary>
    public const string TokenId = "nexopbx:token";

    /// <summary>User id (absent for tokens not tied to a user).</summary>
    public const string UserId = "nexopbx:user";

    /// <summary>Role name (absent for tokens not tied to a user).</summary>
    public const string Role = "nexopbx:role";
}

/// <summary>Authentication scheme for <c>Authorization: Bearer nxp_...</c> (HTTP and gRPC metadata alike).</summary>
public static class NexoAuthenticationDefaults
{
    /// <summary>Scheme name.</summary>
    public const string Scheme = "NexoToken";
}

/// <summary>Reads the bearer token, validates it and builds the claims principal.</summary>
public sealed class NexoTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly TokenAuthenticator _authenticator;

    /// <summary>Creates the handler.</summary>
    public NexoTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TokenAuthenticator authenticator)
        : base(options, logger, encoder)
    {
        _authenticator = authenticator;
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers[HeaderNames.Authorization].ToString();
        if (string.IsNullOrEmpty(header))
        {
            return AuthenticateResult.NoResult();
        }

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var principal = await _authenticator.AuthenticateAsync(header[prefix.Length..].Trim(), Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail("Invalid or expired token.");
        }

        var claims = new List<Claim>
        {
            new(NexoClaims.Tenant, principal.TenantId.ToString()),
            new(NexoClaims.TokenId, principal.TokenId.ToString()),
        };
        if (principal.UserId is { } userId)
        {
            claims.Add(new Claim(NexoClaims.UserId, userId.ToString()));
            claims.Add(new Claim(ClaimTypes.Name, principal.Username ?? userId.ToString()));
        }

        if (principal.Role is { } role)
        {
            claims.Add(new Claim(NexoClaims.Role, role.ToString()));
        }

        claims.AddRange(principal.Scopes.Select(s => new Claim(NexoClaims.Scope, s)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
