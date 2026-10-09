// SPDX-License-Identifier: AGPL-3.0-only
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Security;
using NexoPBX.Abstractions.Time;
using NexoPBX.Store.Entities;
using NexoPBX.Store.Repositories;

namespace NexoPBX.Auth.Tokens;

/// <summary>Who is calling, as established from a valid token.</summary>
/// <param name="TenantId">Tenant the token belongs to.</param>
/// <param name="TokenId">The token used.</param>
/// <param name="UserId">The user the token acts as, if any.</param>
/// <param name="Username">The user's login name, if any.</param>
/// <param name="Role">The user's role, if any.</param>
/// <param name="Scopes">What the caller may do right now.</param>
/// <param name="Kind">Api or Session token.</param>
public sealed record AuthenticatedPrincipal(
    Guid TenantId,
    Guid TokenId,
    Guid? UserId,
    string? Username,
    Role? Role,
    IReadOnlySet<string> Scopes,
    TokenKind Kind);

/// <summary>Turns a bearer token string into an <see cref="AuthenticatedPrincipal"/>, or nothing.</summary>
public sealed class TokenAuthenticator
{
    private static readonly TimeSpan LastUsedGranularity = TimeSpan.FromMinutes(5);
    private readonly TokenService _tokens;
    private readonly ITokenRepository _tokenRepository;
    private readonly IUserRepository _users;
    private readonly TenantContext _tenant;
    private readonly IClock _clock;

    /// <summary>Creates the authenticator.</summary>
    public TokenAuthenticator(TokenService tokens, ITokenRepository tokenRepository, IUserRepository users, TenantContext tenant, IClock clock)
    {
        _tokens = tokens;
        _tokenRepository = tokenRepository;
        _users = users;
        _tenant = tenant;
        _clock = clock;
    }

    /// <summary>Validates a token. Also switches the request's tenant context to the token's tenant.</summary>
    public async Task<AuthenticatedPrincipal?> AuthenticateAsync(string? bearer, CancellationToken cancellationToken)
    {
        if (!TokenService.LooksLikeToken(bearer))
        {
            return null;
        }

        var token = await _tokenRepository.FindByHashAsync(TokenService.Hash(bearer!), cancellationToken);
        if (token is null || !_tokens.IsLive(token))
        {
            return null;
        }

        // Everything after this point runs inside the token's own tenant.
        _tenant.TenantId = token.TenantId;
        var tokenScopes = TokenService.SplitScopes(token.Scopes);

        User? user = null;
        IReadOnlySet<string> effective = tokenScopes.ToHashSet(StringComparer.Ordinal);
        if (token.UserId is { } userId)
        {
            user = await _users.FindByIdAsync(userId, cancellationToken);
            if (user is not { IsActive: true })
            {
                return null;
            }

            // A role change or demotion takes effect on existing tokens immediately.
            // ApiClient users have no scopes of their own: their token's scopes are the whole grant.
            if (user.Role != Role.ApiClient)
            {
                var roleScopes = Scopes.ForRole(user.Role);
                effective = tokenScopes.Where(roleScopes.Contains).ToHashSet(StringComparer.Ordinal);
            }
        }

        await TouchAsync(token, cancellationToken);
        return new AuthenticatedPrincipal(token.TenantId, token.Id, user?.Id, user?.Username, user?.Role, effective, token.Kind);
    }

    private async Task TouchAsync(ApiToken token, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        if (token.LastUsedAt is { } last && now - last < LastUsedGranularity)
        {
            return;
        }

        token.LastUsedAt = now;
        await _tokenRepository.UpdateAsync(token, cancellationToken);
    }
}
