// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Frozen;

namespace NexoPBX.Abstractions.Events;

/// <summary>Well-known event types. Additive only within <c>v1</c>.</summary>
public static class EventTypes
{
    /// <summary>The host finished starting.</summary>
    public const string SystemStarted = "system.started";

    /// <summary>The host is draining and about to stop.</summary>
    public const string SystemStopping = "system.stopping";

    /// <summary>An audit record was written.</summary>
    public const string AuditRecorded = "audit.recorded";

    /// <summary>
    /// Synthetic event: this subscriber was too slow and events were dropped.
    /// Clients must re-read current state instead of trusting the stream.
    /// </summary>
    public const string StreamResync = "stream.resync";
}

/// <summary>An event travelling on the internal bus and on the public event stream.</summary>
/// <param name="Id">Unique, time-ordered identifier.</param>
/// <param name="Type">Event type, dotted lower-case (see <see cref="EventTypes"/>).</param>
/// <param name="TenantId">Tenant the event belongs to.</param>
/// <param name="OccurredAtUtc">When it happened.</param>
/// <param name="Subject">Optional id of the entity the event is about.</param>
/// <param name="Data">Flat key/value details. Typed payloads can be added later without breaking clients.</param>
public sealed record EventEnvelope(
    Guid Id,
    string Type,
    Guid TenantId,
    DateTime OccurredAtUtc,
    string? Subject,
    IReadOnlyDictionary<string, string> Data)
{
    /// <summary>Shared empty details map.</summary>
    public static readonly IReadOnlyDictionary<string, string> NoData = FrozenDictionary<string, string>.Empty;
}
