// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using NexoPBX.Abstractions.Time;

namespace NexoPBX.Abstractions.Identity;

/// <summary>Generates identifiers for new entities. Time-ordered so that indexes stay compact and "newest first" is a sort by id.</summary>
public interface IIdGenerator
{
    /// <summary>Creates a new unique identifier, strictly greater than every identifier this generator created before.</summary>
    Guid NewId();
}

/// <summary>
/// UUID version 7 with a monotonic counter (RFC 9562, section 6.2, method 1). <c>Guid.CreateVersion7()</c> is only
/// ordered to the millisecond: two ids created in the same millisecond compare randomly, which would make "newest first"
/// listings wrong for events that happen close together. Here a 12-bit counter in the same millisecond keeps the order.
/// </summary>
public sealed class GuidV7IdGenerator : IIdGenerator
{
    private const int CounterLimit = 1 << 12;
    private readonly IClock _clock;
    private readonly Lock _gate = new();
    private long _lastMilliseconds;
    private int _counter;

    /// <summary>Creates a generator reading the system clock.</summary>
    public GuidV7IdGenerator()
        : this(SystemClock.Instance)
    {
    }

    /// <summary>Creates a generator reading the given clock (tests use a virtual one).</summary>
    public GuidV7IdGenerator(IClock clock) => _clock = clock;

    /// <inheritdoc />
    public Guid NewId()
    {
        long milliseconds;
        int counter;
        lock (_gate)
        {
            var now = new DateTimeOffset(DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            if (now > _lastMilliseconds)
            {
                _lastMilliseconds = now;
                // Start anywhere in the lower half so a burst has room to count up without borrowing from the next millisecond.
                _counter = RandomNumberGenerator.GetInt32(CounterLimit / 2);
            }
            else if (++_counter >= CounterLimit)
            {
                // More than 4096 ids in one millisecond (or the clock went backwards): borrow the next millisecond.
                _lastMilliseconds++;
                _counter = 0;
            }

            milliseconds = _lastMilliseconds;
            counter = _counter;
        }

        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes[8..]);
        bytes[0] = (byte)(milliseconds >> 40);
        bytes[1] = (byte)(milliseconds >> 32);
        bytes[2] = (byte)(milliseconds >> 24);
        bytes[3] = (byte)(milliseconds >> 16);
        bytes[4] = (byte)(milliseconds >> 8);
        bytes[5] = (byte)milliseconds;
        bytes[6] = (byte)(0x70 | (counter >> 8)); // version 7 + counter high bits
        bytes[7] = (byte)counter;
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }
}
