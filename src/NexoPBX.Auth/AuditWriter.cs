// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using NexoPBX.Abstractions.Events;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Time;
using NexoPBX.Store.Entities;
using NexoPBX.Store.Repositories;

namespace NexoPBX.Auth;

/// <summary>What to record in the audit trail. Never put secrets in <paramref name="Details"/>.</summary>
public sealed record AuditEntry(
    string Action,
    AuditOutcome Outcome,
    Guid? ActorUserId = null,
    Guid? ActorTokenId = null,
    string? TargetType = null,
    string? TargetId = null,
    string? RemoteIp = null,
    IReadOnlyDictionary<string, string>? Details = null);

/// <summary>Writes audit records and announces them on the event bus.</summary>
public interface IAuditWriter
{
    /// <summary>Appends a record for the current tenant.</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);
}

/// <summary>Default <see cref="IAuditWriter"/>.</summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly IAuditRepository _repository;
    private readonly IEventBus _bus;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly ITenantContext _tenant;

    /// <summary>Creates the writer.</summary>
    public AuditWriter(IAuditRepository repository, IEventBus bus, IClock clock, IIdGenerator ids, ITenantContext tenant)
    {
        _repository = repository;
        _bus = bus;
        _clock = clock;
        _ids = ids;
        _tenant = tenant;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var now = _clock.UtcNow;
        var record = new AuditEvent
        {
            Id = _ids.NewId(),
            TenantId = _tenant.TenantId,
            At = now,
            ActorUserId = entry.ActorUserId,
            ActorTokenId = entry.ActorTokenId,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            Outcome = entry.Outcome,
            RemoteIp = entry.RemoteIp,
            Details = entry.Details is { Count: > 0 } ? JsonSerializer.Serialize(entry.Details) : null,
        };
        await _repository.AppendAsync(record, cancellationToken);

        _bus.Publish(new EventEnvelope(
            _ids.NewId(),
            EventTypes.AuditRecorded,
            record.TenantId,
            now,
            record.Id.ToString(),
            new Dictionary<string, string>
            {
                ["action"] = entry.Action,
                ["outcome"] = entry.Outcome.ToString(),
            }));
    }
}
