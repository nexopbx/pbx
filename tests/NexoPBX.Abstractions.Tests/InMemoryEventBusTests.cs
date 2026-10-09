// SPDX-License-Identifier: AGPL-3.0-only
using NexoPBX.Abstractions.Events;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Testing;

namespace NexoPBX.Abstractions.Tests;

public sealed class InMemoryEventBusTests
{
    private static readonly Guid TenantA = TenantIds.Default;
    private static readonly Guid TenantB = new("00000000-0000-0000-0000-0000000000b2");

    private readonly VirtualClock _clock = new();
    private readonly GuidV7IdGenerator _ids = new();

    private InMemoryEventBus NewBus() => new(_clock, _ids);

    private EventEnvelope Event(string type, Guid? tenant = null, string? subject = null) =>
        new(_ids.NewId(), type, tenant ?? TenantA, _clock.UtcNow, subject, EventEnvelope.NoData);

    private static async Task<List<EventEnvelope>> ReadAsync(IEventSubscription sub, int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var list = new List<EventEnvelope>();
        await foreach (var e in sub.ReadAllAsync(cts.Token))
        {
            list.Add(e);
            if (list.Count == count)
            {
                break;
            }
        }

        return list;
    }

    [Fact]
    public async Task Delivers_events_in_publish_order()
    {
        var bus = NewBus();
        using var sub = bus.Subscribe(new SubscriptionOptions { TenantId = TenantA });

        bus.Publish(Event("a.one"));
        bus.Publish(Event("a.two"));
        bus.Publish(Event("a.three"));

        var got = await ReadAsync(sub, 3);
        Assert.Equal(["a.one", "a.two", "a.three"], got.Select(e => e.Type));
    }

    [Fact]
    public async Task Never_delivers_events_of_another_tenant()
    {
        var bus = NewBus();
        using var sub = bus.Subscribe(new SubscriptionOptions { TenantId = TenantA });

        bus.Publish(Event("x.other", TenantB));
        bus.Publish(Event("x.mine", TenantA));

        var got = await ReadAsync(sub, 1);
        Assert.Equal("x.mine", Assert.Single(got).Type);
    }

    [Fact]
    public async Task Filters_by_type_prefix()
    {
        var bus = NewBus();
        using var sub = bus.Subscribe(new SubscriptionOptions { TenantId = TenantA, TypePrefixes = ["audit."] });

        bus.Publish(Event("system.started"));
        bus.Publish(Event("audit.recorded"));

        var got = await ReadAsync(sub, 1);
        Assert.Equal("audit.recorded", Assert.Single(got).Type);
    }

    [Fact]
    public async Task Slow_subscriber_drops_oldest_and_is_told_to_resync()
    {
        var bus = NewBus();
        using var sub = bus.Subscribe(new SubscriptionOptions { TenantId = TenantA, Capacity = 4 });

        for (var i = 0; i < 10; i++)
        {
            bus.Publish(Event($"n.{i}"));
        }

        Assert.Equal(6, sub.DroppedCount);

        // First the resync marker, then the 4 newest events.
        var got = await ReadAsync(sub, 5);
        Assert.Equal(EventTypes.StreamResync, got[0].Type);
        Assert.Equal("6", got[0].Data["missed"]);
        Assert.Equal(["n.6", "n.7", "n.8", "n.9"], got.Skip(1).Select(e => e.Type));
    }

    [Fact]
    public async Task Publish_never_blocks_even_when_nobody_reads()
    {
        var bus = NewBus();
        using var sub = bus.Subscribe(new SubscriptionOptions { TenantId = TenantA, Capacity = 8 });

        var publisher = Task.Run(() =>
        {
            for (var i = 0; i < 100_000; i++)
            {
                bus.Publish(Event("flood"));
            }
        });

        await publisher.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(sub.DroppedCount > 0);
    }

    [Fact]
    public void Disposing_a_subscription_releases_it()
    {
        var bus = NewBus();
        var subs = Enumerable.Range(0, 50)
            .Select(_ => bus.Subscribe(new SubscriptionOptions { TenantId = TenantA }))
            .ToList();
        Assert.Equal(50, bus.SubscriptionCount);

        subs.ForEach(s => s.Dispose());

        Assert.Equal(0, bus.SubscriptionCount);
        bus.Publish(Event("after.dispose")); // must not throw
    }

    [Fact]
    public async Task Reader_stops_when_cancelled()
    {
        var bus = NewBus();
        using var sub = bus.Subscribe(new SubscriptionOptions { TenantId = TenantA });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in sub.ReadAllAsync(cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task Concurrent_publishers_and_subscribers_lose_nothing_when_capacity_is_enough()
    {
        const int publishers = 8;
        const int perPublisher = 5_000;
        const int total = publishers * perPublisher;
        var bus = NewBus();

        var subs = Enumerable.Range(0, 4)
            .Select(_ => bus.Subscribe(new SubscriptionOptions { TenantId = TenantA, Capacity = total + 1 }))
            .ToList();

        await Task.WhenAll(Enumerable.Range(0, publishers).Select(p => Task.Run(() =>
        {
            for (var i = 0; i < perPublisher; i++)
            {
                bus.Publish(Event("stress", subject: $"{p}:{i}"));
            }
        })));

        foreach (var sub in subs)
        {
            var got = await ReadAsync(sub, total);
            Assert.Equal(total, got.Count);
            Assert.Equal(0, sub.DroppedCount);

            // Per-publisher order must be preserved.
            foreach (var group in got.GroupBy(e => e.Subject!.Split(':')[0]))
            {
                var seq = group.Select(e => int.Parse(e.Subject!.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture)).ToList();
                Assert.Equal(seq.Order().ToList(), seq);
            }

            sub.Dispose();
        }

        Assert.Equal(0, bus.SubscriptionCount);
    }
}
