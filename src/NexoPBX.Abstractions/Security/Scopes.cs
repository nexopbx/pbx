// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Frozen;

namespace NexoPBX.Abstractions.Security;

/// <summary>Fine-grained permissions. API tokens carry an explicit subset; users get the set of their role.</summary>
public static class Scopes
{
    /// <summary>Read system information and health details.</summary>
    public const string SystemRead = "system.read";

    /// <summary>Subscribe to the live event stream.</summary>
    public const string EventsSubscribe = "events.subscribe";

    /// <summary>List and read users.</summary>
    public const string UsersRead = "users.read";

    /// <summary>Create and modify users.</summary>
    public const string UsersWrite = "users.write";

    /// <summary>Create, list and revoke API tokens (own tokens for any user, others for admins).</summary>
    public const string TokensManage = "tokens.manage";

    /// <summary>Read the audit log.</summary>
    public const string AuditRead = "audit.read";

    /// <summary>Every scope known to this version.</summary>
    public static readonly FrozenSet<string> All = new[]
    {
        SystemRead, EventsSubscribe, UsersRead, UsersWrite, TokensManage, AuditRead,
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<Role, FrozenSet<string>> ByRole = new Dictionary<Role, FrozenSet<string>>
    {
        [Role.Owner] = All,
        [Role.Admin] = All,
        [Role.Supervisor] = new[] { SystemRead, EventsSubscribe, UsersRead, AuditRead }.ToFrozenSet(StringComparer.Ordinal),
        [Role.Agent] = new[] { SystemRead, EventsSubscribe, TokensManage }.ToFrozenSet(StringComparer.Ordinal),
        [Role.ReadOnly] = new[] { SystemRead, UsersRead, AuditRead }.ToFrozenSet(StringComparer.Ordinal),
        [Role.ApiClient] = FrozenSet<string>.Empty,
    }.ToFrozenDictionary();

    /// <summary>Scopes granted by a role.</summary>
    public static IReadOnlySet<string> ForRole(Role role) => ByRole[role];

    /// <summary>True if every requested scope is known.</summary>
    public static bool AreValid(IEnumerable<string> scopes) => scopes.All(All.Contains);
}
