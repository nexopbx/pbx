// SPDX-License-Identifier: AGPL-3.0-only
namespace NexoPBX.App;

/// <summary>
/// Prevents two hosts from running on the same data directory (two writers on one database and one set of ports
/// is a recipe for corruption). Held for the lifetime of the process; the OS releases it even after a crash.
/// </summary>
public sealed class InstanceLock : IDisposable
{
    private readonly FileStream _stream;

    private InstanceLock(FileStream stream) => _stream = stream;

    /// <summary>Takes the lock, or returns null if another instance holds it.</summary>
    public static InstanceLock? TryAcquire(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "nexopbx.lock");
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new InstanceLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _stream.Dispose();
}
