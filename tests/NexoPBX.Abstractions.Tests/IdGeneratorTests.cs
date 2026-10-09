// SPDX-License-Identifier: AGPL-3.0-only
using NexoPBX.Abstractions.Identity;
using NexoPBX.Testing;

namespace NexoPBX.Abstractions.Tests;

public sealed class IdGeneratorTests
{
    // Ids sort by their RFC text form (this is also how SQLite and PostgreSQL order them), not by Guid.CompareTo.
    private static string Key(Guid id) => id.ToString("D");

    [Fact]
    public void Ids_created_within_the_same_millisecond_still_sort_in_creation_order()
    {
        var clock = new VirtualClock(); // frozen: every id shares one millisecond
        var generator = new GuidV7IdGenerator(clock);

        var ids = Enumerable.Range(0, 3000).Select(_ => generator.NewId()).ToList();

        Assert.Equal(ids.Select(Key).Order(StringComparer.Ordinal), ids.Select(Key));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void More_than_the_per_millisecond_capacity_borrows_the_next_millisecond_instead_of_repeating()
    {
        var generator = new GuidV7IdGenerator(new VirtualClock());

        var ids = Enumerable.Range(0, 20_000).Select(_ => generator.NewId()).ToList();

        Assert.Equal(ids.Select(Key).Order(StringComparer.Ordinal), ids.Select(Key));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void A_clock_that_goes_backwards_never_makes_ids_go_backwards()
    {
        var clock = new SettableClock(new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var generator = new GuidV7IdGenerator(clock);
        var ids = Enumerable.Range(0, 10).Select(_ => generator.NewId()).ToList();

        clock.UtcNow = clock.UtcNow.AddHours(-1); // e.g. an NTP correction
        ids.AddRange(Enumerable.Range(0, 10).Select(_ => generator.NewId()));

        Assert.Equal(ids.Select(Key).Order(StringComparer.Ordinal), ids.Select(Key));
        Assert.Equal(20, ids.Distinct().Count());
    }

    [Fact]
    public void Ids_carry_the_clock_time_and_the_version_7_and_variant_bits()
    {
        var clock = new VirtualClock(new DateTime(2030, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        var id = new GuidV7IdGenerator(clock).NewId();

        var bytes = id.ToByteArray(bigEndian: true);
        var milliseconds = ((long)bytes[0] << 40) | ((long)bytes[1] << 32) | ((long)bytes[2] << 24) | ((long)bytes[3] << 16) | ((long)bytes[4] << 8) | bytes[5];

        Assert.Equal(new DateTimeOffset(clock.UtcNow).ToUnixTimeMilliseconds(), milliseconds);
        Assert.Equal(7, bytes[6] >> 4);
        Assert.Equal(0b10, bytes[8] >> 6);
        Assert.Equal(7, id.Version);
    }

    [Fact]
    public async Task Concurrent_generation_never_repeats_an_id_and_stays_ordered_per_thread()
    {
        var generator = new GuidV7IdGenerator();

        var perThread = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            Enumerable.Range(0, 20_000).Select(_ => generator.NewId()).ToList())));

        Assert.All(perThread, ids => Assert.Equal(ids.Select(Key).Order(StringComparer.Ordinal), ids.Select(Key)));
        Assert.Equal(8 * 20_000, perThread.SelectMany(ids => ids).Distinct().Count());
    }

    private sealed class SettableClock : NexoPBX.Abstractions.Time.IClock
    {
        public SettableClock(DateTime start) => UtcNow = start;

        public DateTime UtcNow { get; set; }

        public long GetTimestamp() => 0;

        public TimeSpan GetElapsed(long startTimestamp) => TimeSpan.Zero;
    }
}
