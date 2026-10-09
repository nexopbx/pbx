// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.Auth;

/// <summary>The request is well-formed but not acceptable (weak password, unknown scope...).</summary>
public sealed class NexoValidationException : Exception
{
    /// <summary>Creates the exception for a field.</summary>
    public NexoValidationException(string field, string message)
        : base(message)
    {
        Field = field;
    }

    /// <summary>Name of the offending field.</summary>
    public string Field { get; }
}
