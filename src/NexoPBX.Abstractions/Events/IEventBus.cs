// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.Abstractions.Events;

/// <summary>
/// In-process publish/subscribe bus. <see cref="Publish"/> never blocks and never throws because of a slow
/// subscriber: every subscription owns a bounded queue with an explicit overflow policy (ARCHITECTURE §5).
/// </summary>
public interface IEventBus
{
    /// <summary>Delivers an event to every matching subscription. Thread-safe and non-blocking.</summary>
    void Publish(EventEnvelope envelope);

    /// <summary>Creates a subscription. Dispose it to stop receiving events and release its queue.</summary>
    IEventSubscription Subscribe(SubscriptionOptions options);
}

/// <summary>A live subscription to the bus.</summary>
public interface IEventSubscription : IDisposable
{
    /// <summary>Reads events until the subscription is disposed or the token is cancelled.</summary>
    IAsyncEnumerable<EventEnvelope> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>Total events dropped for this subscriber because its queue was full.</summary>
    long DroppedCount { get; }
}

/// <summary>What a subscriber wants to receive and how it behaves when it falls behind.</summary>
public sealed record SubscriptionOptions
{
    /// <summary>Only events of this tenant are delivered.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Delivers only events whose type starts with one of these prefixes. Empty means all types.</summary>
    public IReadOnlyList<string> TypePrefixes { get; init; } = [];

    /// <summary>Queue capacity. When full, the OLDEST event is dropped and a <see cref="EventTypes.StreamResync"/> marker is queued.</summary>
    public int Capacity { get; init; } = 1024;
}
