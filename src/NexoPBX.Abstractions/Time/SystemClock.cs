// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;

namespace NexoPBX.Abstractions.Time;

/// <summary>Real clock backed by the operating system.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>Shared instance; the clock is stateless.</summary>
    public static SystemClock Instance { get; } = new();

    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    public long GetTimestamp() => Stopwatch.GetTimestamp();

    /// <inheritdoc />
    public TimeSpan GetElapsed(long startTimestamp) => Stopwatch.GetElapsedTime(startTimestamp);
}
