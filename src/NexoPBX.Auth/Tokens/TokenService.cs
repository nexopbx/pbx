// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Security;
using NexoPBX.Abstractions.Time;
using NexoPBX.Store.Entities;
using NexoPBX.Store.Repositories;

namespace NexoPBX.Auth.Tokens;

/// <summary>A freshly issued token. <see cref="Plaintext"/> is shown once and never stored.</summary>
public sealed record IssuedToken(string Plaintext, ApiToken Entity);

/// <summary>Issues, validates and revokes opaque bearer tokens (256 random bits, only the SHA-256 is stored).</summary>
public sealed class TokenService
{
    /// <summary>Every token starts with this, so secret scanners and humans can recognise it.</summary>
    public const string Prefix = "nxp_";

    private const int SecretBytes = 32;
    private readonly ITokenRepository _tokens;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    /// <summary>Creates the service.</summary>
    public TokenService(ITokenRepository tokens, IClock clock, IIdGenerator ids)
    {
        _tokens = tokens;
        _clock = clock;
        _ids = ids;
    }

    /// <summary>Lower-case hex SHA-256 of a token string.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>True if the string has the right shape; saves a database lookup for obvious garbage.</summary>
    public static bool LooksLikeToken(string? token) =>
        token is not null && token.Length == Prefix.Length + 43 && token.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Issues a token. Scopes must be known; the caller is responsible for checking the user may hold them.</summary>
    public async Task<IssuedToken> IssueAsync(
        Guid? userId,
        string name,
        IEnumerable<string> scopes,
        TokenKind kind,
        TimeSpan? lifetime,
        CancellationToken cancellationToken)
    {
        var scopeList = scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (!Scopes.AreValid(scopeList))
        {
            throw new NexoValidationException("scopes", "One or more scopes are unknown.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            throw new NexoValidationException("name", "Token name is required (up to 200 characters).");
        }

        var plaintext = Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));
        var now = _clock.UtcNow;
        var entity = new ApiToken
        {
            Id = _ids.NewId(),
            UserId = userId,
            Name = name.Trim(),
            TokenHash = Hash(plaintext),
            Prefix = plaintext[..8],
            Scopes = string.Join(' ', scopeList),
            Kind = kind,
            CreatedAt = now,
            ExpiresAt = lifetime is { } l ? now + l : null,
        };
        await _tokens.AddAsync(entity, cancellationToken);
        return new IssuedToken(plaintext, entity);
    }

    /// <summary>Marks a token revoked. Idempotent.</summary>
    public async Task<bool> RevokeAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        var token = await _tokens.FindByIdAsync(tokenId, cancellationToken);
        if (token is null)
        {
            return false;
        }

        if (token.RevokedAt is null)
        {
            token.RevokedAt = _clock.UtcNow;
            await _tokens.UpdateAsync(token, cancellationToken);
        }

        return true;
    }

    /// <summary>True if the token can still be used right now.</summary>
    public bool IsLive(ApiToken token)
    {
        var now = _clock.UtcNow;
        return token.RevokedAt is null && (token.ExpiresAt is null || token.ExpiresAt > now);
    }

    /// <summary>Splits the stored scope string.</summary>
    public static IReadOnlyList<string> SplitScopes(string scopes) =>
        scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
