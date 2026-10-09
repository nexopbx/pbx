// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.Abstractions.Security;

/// <summary>Roles of a user or token. Stored by name, so values may be reordered but never renamed.</summary>
public enum Role
{
    /// <summary>Full control, including users and system settings.</summary>
    Owner = 0,

    /// <summary>Administers the PBX configuration.</summary>
    Admin = 1,

    /// <summary>Monitors queues and agents, can act on live calls.</summary>
    Supervisor = 2,

    /// <summary>Takes calls.</summary>
    Agent = 3,

    /// <summary>Can look but not touch.</summary>
    ReadOnly = 4,

    /// <summary>Machine client: permissions come only from the token scopes.</summary>
    ApiClient = 5,
}
