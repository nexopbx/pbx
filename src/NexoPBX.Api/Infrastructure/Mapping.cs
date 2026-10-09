// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using NexoPBX.Abstractions.Events;
using NexoPBX.Auth.Tokens;
using NexoPBX.Contracts.V1;
using DomainRole = NexoPBX.Abstractions.Security.Role;

namespace NexoPBX.Api.Infrastructure;

internal static class Mapping
{
    public static Timestamp? ToTimestamp(DateTime? value) =>
        value is { } v ? Timestamp.FromDateTime(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;

    public static string ToEtag(long version) => version.ToString(CultureInfo.InvariantCulture);

    public static long? ParseEtag(string etag)
    {
        if (string.IsNullOrEmpty(etag))
        {
            return null;
        }

        return long.TryParse(etag, NumberStyles.None, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw ApiErrors.Invalid("etag", "etag is not valid.");
    }

    public static User ToProto(NexoPBX.Store.Entities.User user) => new()
    {
        Id = user.Id.ToString(),
        Username = user.Username,
        DisplayName = user.DisplayName,
        Role = ToProto(user.Role),
        IsActive = user.IsActive,
        TotpEnabled = user.TotpEnabled,
        CreateTime = ToTimestamp(user.CreatedAt),
        LastLoginTime = ToTimestamp(user.LastLoginAt),
        Etag = ToEtag(user.Version),
    };

    public static Role ToProto(DomainRole role) => role switch
    {
        DomainRole.Owner => Role.Owner,
        DomainRole.Admin => Role.Admin,
        DomainRole.Supervisor => Role.Supervisor,
        DomainRole.Agent => Role.Agent,
        DomainRole.ReadOnly => Role.ReadOnly,
        DomainRole.ApiClient => Role.ApiClient,
        _ => Role.Unspecified,
    };

    public static DomainRole ToDomain(Role role, string field) => role switch
    {
        Role.Owner => DomainRole.Owner,
        Role.Admin => DomainRole.Admin,
        Role.Supervisor => DomainRole.Supervisor,
        Role.Agent => DomainRole.Agent,
        Role.ReadOnly => DomainRole.ReadOnly,
        Role.ApiClient => DomainRole.ApiClient,
        _ => throw ApiErrors.Invalid(field, "A valid role is required."),
    };

    public static ApiToken ToProto(NexoPBX.Store.Entities.ApiToken token)
    {
        var proto = new ApiToken
        {
            Id = token.Id.ToString(),
            Name = token.Name,
            Prefix = token.Prefix,
            CreateTime = ToTimestamp(token.CreatedAt),
            ExpireTime = ToTimestamp(token.ExpiresAt),
            RevokeTime = ToTimestamp(token.RevokedAt),
            LastUsedTime = ToTimestamp(token.LastUsedAt),
            UserId = token.UserId?.ToString() ?? string.Empty,
        };
        proto.Scopes.AddRange(TokenService.SplitScopes(token.Scopes));
        return proto;
    }

    public static Event ToProto(EventEnvelope envelope)
    {
        var proto = new Event
        {
            Id = envelope.Id.ToString(),
            Type = envelope.Type,
            TenantId = envelope.TenantId.ToString(),
            OccurredAt = ToTimestamp(envelope.OccurredAtUtc),
            Subject = envelope.Subject ?? string.Empty,
        };
        foreach (var (key, value) in envelope.Data)
        {
            proto.Data[key] = value;
        }

        return proto;
    }
}
