// SPDX-License-Identifier: AGPL-3.0-only
using NexoPBX.Abstractions.Time;

namespace NexoPBX.Testing;

/// <summary>
/// A clock that only moves when the test says so. Whole scenarios (token expiry, lockout windows, later: call flows)
/// run in milliseconds and give identical results on every run.
/// </summary>
public sealed class VirtualClock : IClock
{
    private long _ticks;

    /// <summary>Creates a clock starting at <paramref name="start"/> (default: 2026-01-01T00:00:00Z).</summary>
    public VirtualClock(DateTime? start = null)
    {
        UtcNow = DateTime.SpecifyKind(start ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTimeKind.Utc);
    }

    /// <inheritdoc />
    public DateTime UtcNow { get; private set; }

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        UtcNow += by;
        Interlocked.Add(ref _ticks, by.Ticks);
    }

    /// <inheritdoc />
    public long GetTimestamp() => Interlocked.Read(ref _ticks);

    /// <inheritdoc />
    public TimeSpan GetElapsed(long startTimestamp) => TimeSpan.FromTicks(Interlocked.Read(ref _ticks) - startTimestamp);
}
