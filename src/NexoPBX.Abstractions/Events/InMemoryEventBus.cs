// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Time;

namespace NexoPBX.Abstractions.Events;

/// <summary>
/// Default <see cref="IEventBus"/>. Subscribers are held in an immutable array swapped under a lock only on
/// subscribe/unsubscribe, so the publish path takes no lock. Each subscriber has a bounded channel that drops the
/// oldest event when full and tells the reader to resynchronise.
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    /// <summary>Name of the meter exposing bus metrics.</summary>
    public const string MeterName = "NexoPBX.Events";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> PublishedCounter = Meter.CreateCounter<long>("nexopbx_events_published");
    private static readonly Counter<long> DroppedCounter = Meter.CreateCounter<long>("nexopbx_events_dropped");

    private readonly Lock _gate = new();
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private ImmutableArray<Subscription> _subscriptions = [];

    /// <summary>Creates a bus using the given clock and id generator for synthetic events.</summary>
    public InMemoryEventBus(IClock clock, IIdGenerator ids)
    {
        _clock = clock;
        _ids = ids;
    }

    /// <summary>Number of active subscriptions (for diagnostics and leak tests).</summary>
    public int SubscriptionCount => _subscriptions.Length;

    /// <inheritdoc />
    public void Publish(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        PublishedCounter.Add(1);
        foreach (var subscription in _subscriptions)
        {
            subscription.TryDeliver(envelope);
        }
    }

    /// <inheritdoc />
    public IEventSubscription Subscribe(SubscriptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Capacity, 2);

        var subscription = new Subscription(this, options);
        lock (_gate)
        {
            _subscriptions = _subscriptions.Add(subscription);
        }

        return subscription;
    }

    private void Remove(Subscription subscription)
    {
        lock (_gate)
        {
            _subscriptions = _subscriptions.Remove(subscription);
        }
    }

    private sealed class Subscription : IEventSubscription
    {
        private readonly InMemoryEventBus _bus;
        private readonly SubscriptionOptions _options;
        private readonly Channel<EventEnvelope> _channel;
        private long _dropped;
        private long _reported;

        public Subscription(InMemoryEventBus bus, SubscriptionOptions options)
        {
            _bus = bus;
            _options = options;
            _channel = Channel.CreateBounded<EventEnvelope>(
                new BoundedChannelOptions(options.Capacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                },
                _ =>
                {
                    Interlocked.Increment(ref _dropped);
                    DroppedCounter.Add(1);
                });
        }

        public long DroppedCount => Interlocked.Read(ref _dropped);

        public void TryDeliver(EventEnvelope envelope)
        {
            if (envelope.TenantId != _options.TenantId || !Matches(envelope.Type))
            {
                return;
            }

            _channel.Writer.TryWrite(envelope);
        }

        public async IAsyncEnumerable<EventEnvelope> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var reader = _channel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // Tell the client first if it missed something: it must re-read state, not trust the stream.
                var dropped = Interlocked.Read(ref _dropped);
                if (dropped > _reported)
                {
                    var missed = dropped - _reported;
                    _reported = dropped;
                    yield return new EventEnvelope(
                        _bus._ids.NewId(),
                        EventTypes.StreamResync,
                        _options.TenantId,
                        _bus._clock.UtcNow,
                        Subject: null,
                        new Dictionary<string, string> { ["missed"] = missed.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                }

                while (reader.TryRead(out var envelope))
                {
                    yield return envelope;
                }
            }
        }

        public void Dispose()
        {
            _bus.Remove(this);
            _channel.Writer.TryComplete();
        }

        private bool Matches(string type)
        {
            if (_options.TypePrefixes.Count == 0)
            {
                return true;
            }

            foreach (var prefix in _options.TypePrefixes)
            {
                if (type.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
