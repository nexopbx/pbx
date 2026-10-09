// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.Abstractions.Time;

/// <summary>
/// The only source of time for domain logic. Production uses <see cref="SystemClock"/>;
/// tests use a virtual clock so that whole scenarios run deterministically (ADR-005).
/// </summary>
public interface IClock
{
    /// <summary>Current wall-clock time in UTC.</summary>
    DateTime UtcNow { get; }

    /// <summary>Monotonic timestamp, immune to wall-clock adjustments. Use with <see cref="GetElapsed"/>.</summary>
    long GetTimestamp();

    /// <summary>Time elapsed since a timestamp obtained from <see cref="GetTimestamp"/>.</summary>
    TimeSpan GetElapsed(long startTimestamp);
}
