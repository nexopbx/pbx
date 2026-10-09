// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace NexoPBX.Auth.Passwords;

/// <summary>Hashes and verifies passwords.</summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a password into a self-describing string (algorithm, parameters, salt, hash).</summary>
    string Hash(string password);

    /// <summary>Checks a password against a stored hash in constant time.</summary>
    bool Verify(string password, string storedHash);

    /// <summary>True if the stored hash was made with weaker parameters than the current ones.</summary>
    bool NeedsRehash(string storedHash);
}

/// <summary>Argon2id with PHC string format: <c>$argon2id$v=19$m=..,t=..,p=..$salt$hash</c>.</summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Version = 19;
    private readonly IOptions<AuthOptions> _options;

    /// <summary>Creates the hasher.</summary>
    public Argon2PasswordHasher(IOptions<AuthOptions> options) => _options = options;

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var o = _options.Value;
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(password, salt, o.Argon2MemoryKiB, o.Argon2Iterations, o.Argon2Parallelism, HashBytes);
        return $"$argon2id$v={Version}$m={o.Argon2MemoryKiB},t={o.Argon2Iterations},p={o.Argon2Parallelism}${B64(salt)}${B64(hash)}";
    }

    /// <inheritdoc />
    public bool Verify(string password, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!TryParse(storedHash, out var parsed))
        {
            return false;
        }

        var actual = Compute(password, parsed.Salt, parsed.MemoryKiB, parsed.Iterations, parsed.Parallelism, parsed.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(actual, parsed.Hash);
    }

    /// <inheritdoc />
    public bool NeedsRehash(string storedHash)
    {
        if (!TryParse(storedHash, out var parsed))
        {
            return true;
        }

        var o = _options.Value;
        return parsed.MemoryKiB < o.Argon2MemoryKiB || parsed.Iterations < o.Argon2Iterations || parsed.Parallelism != o.Argon2Parallelism;
    }

    private static byte[] Compute(string password, byte[] salt, int memoryKiB, int iterations, int parallelism, int length)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(length);
    }

    private static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=');

    private static byte[]? FromB64(string text)
    {
        var padded = text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=');
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool TryParse(string stored, out Parsed parsed)
    {
        parsed = default;
        // ["", "argon2id", "v=19", "m=..,t=..,p=..", salt, hash]
        var parts = stored.Split('$');
        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != $"v={Version}")
        {
            return false;
        }

        var kv = parts[3].Split(',');
        if (kv.Length != 3
            || !TryValue(kv[0], "m", out var m)
            || !TryValue(kv[1], "t", out var t)
            || !TryValue(kv[2], "p", out var p)
            || m is < 8 or > 4 * 1024 * 1024 || t is < 1 or > 100 || p is < 1 or > 64)
        {
            return false;
        }

        var salt = FromB64(parts[4]);
        var hash = FromB64(parts[5]);
        if (salt is null || hash is null || salt.Length < 8 || hash.Length < 16)
        {
            return false;
        }

        parsed = new Parsed(m, t, p, salt, hash);
        return true;
    }

    private static bool TryValue(string pair, string key, out int value)
    {
        value = 0;
        return pair.StartsWith(key + "=", StringComparison.Ordinal)
            && int.TryParse(pair.AsSpan(key.Length + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private readonly record struct Parsed(int MemoryKiB, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);
}
