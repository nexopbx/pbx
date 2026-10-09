// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NexoPBX.Abstractions.Time;
using OtpNet;

namespace NexoPBX.Auth.Totp;

/// <summary>Encrypts small secrets for storage (TOTP seeds). Backed by ASP.NET data protection.</summary>
public interface ISecretProtector
{
    /// <summary>Encrypts a secret.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a secret produced by <see cref="Protect"/>. Throws if tampered with.</summary>
    string Unprotect(string protectedText);
}

/// <summary><see cref="ISecretProtector"/> using a dedicated data-protection purpose.</summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    /// <summary>Creates the protector.</summary>
    public DataProtectionSecretProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("NexoPBX.Secrets.v1");

    /// <inheritdoc />
    public string Protect(string plaintext) => _protector.Protect(plaintext);

    /// <inheritdoc />
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}

/// <summary>Time-based one-time passwords (RFC 6238) with replay protection.</summary>
public interface ITotpService
{
    /// <summary>Creates a new random base32 secret.</summary>
    string GenerateSecret();

    /// <summary>Builds the <c>otpauth://</c> URI that authenticator apps import (usually as a QR code).</summary>
    string BuildProvisioningUri(string secret, string accountName);

    /// <summary>
    /// Verifies a code allowing one step of clock drift either way. The matched time step must be newer than
    /// <paramref name="lastAcceptedStep"/>, so a code cannot be used twice.
    /// </summary>
    bool TryVerify(string secret, string code, long lastAcceptedStep, out long matchedStep);
}

/// <summary>Default <see cref="ITotpService"/>.</summary>
public sealed class TotpService : ITotpService
{
    private static readonly VerificationWindow Window = new(previous: 1, future: 1);
    private readonly IClock _clock;
    private readonly IOptions<AuthOptions> _options;

    /// <summary>Creates the service.</summary>
    public TotpService(IClock clock, IOptions<AuthOptions> options)
    {
        _clock = clock;
        _options = options;
    }

    /// <inheritdoc />
    public string GenerateSecret() => Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));

    /// <inheritdoc />
    public string BuildProvisioningUri(string secret, string accountName)
    {
        var issuer = Uri.EscapeDataString(_options.Value.TotpIssuer);
        var account = Uri.EscapeDataString(accountName);
        return $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
    }

    /// <inheritdoc />
    public bool TryVerify(string secret, string code, long lastAcceptedStep, out long matchedStep)
    {
        matchedStep = 0;
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var totp = new OtpNet.Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(_clock.UtcNow, code, out matchedStep, Window) && matchedStep > lastAcceptedStep;
    }
}
