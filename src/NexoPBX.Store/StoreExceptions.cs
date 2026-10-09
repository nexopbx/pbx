// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.Store;

/// <summary>The row was changed by someone else since it was read (optimistic concurrency).</summary>
public sealed class ConcurrencyConflictException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ConcurrencyConflictException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>A uniqueness rule was violated (for example, the username is taken).</summary>
public sealed class DuplicateException : Exception
{
    /// <summary>Creates the exception.</summary>
    public DuplicateException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
