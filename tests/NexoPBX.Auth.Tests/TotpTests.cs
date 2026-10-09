// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.Options;
using NexoPBX.Auth.Totp;
using NexoPBX.Testing;
using OtpNet;

namespace NexoPBX.Auth.Tests;

public sealed class TotpTests
{
    private readonly VirtualClock _clock = new();

    private TotpService Service() => new(_clock, Options.Create(new AuthOptions()));

    private string CodeAt(string secret, TimeSpan offset) =>
        new OtpNet.Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(_clock.UtcNow + offset);

    [Fact]
    public void A_current_code_is_accepted()
    {
        var service = Service();
        var secret = service.GenerateSecret();

        Assert.True(service.TryVerify(secret, CodeAt(secret, TimeSpan.Zero), 0, out var step));
        Assert.True(step > 0);
    }

    [Fact]
    public void The_same_code_cannot_be_used_twice()
    {
        var service = Service();
        var secret = service.GenerateSecret();
        var code = CodeAt(secret, TimeSpan.Zero);

        Assert.True(service.TryVerify(secret, code, 0, out var step));
        Assert.False(service.TryVerify(secret, code, step, out _));
    }

    [Fact]
    public void One_step_of_clock_drift_is_tolerated_but_not_two()
    {
        var service = Service();
        var secret = service.GenerateSecret();

        Assert.True(service.TryVerify(secret, CodeAt(secret, TimeSpan.FromSeconds(-30)), 0, out _));
        Assert.True(service.TryVerify(secret, CodeAt(secret, TimeSpan.FromSeconds(30)), 0, out _));
        Assert.False(service.TryVerify(secret, CodeAt(secret, TimeSpan.FromSeconds(90)), 0, out _));
        Assert.False(service.TryVerify(secret, CodeAt(secret, TimeSpan.FromSeconds(-90)), 0, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("      ")]
    public void Malformed_codes_are_rejected(string code)
    {
        var service = Service();

        Assert.False(service.TryVerify(service.GenerateSecret(), code, 0, out _));
    }

    [Fact]
    public void Provisioning_uri_is_importable_by_authenticator_apps()
    {
        var uri = Service().BuildProvisioningUri("JBSWY3DPEHPK3PXP", "alice@example.com");

        Assert.StartsWith("otpauth://totp/NexoPBX:alice%40example.com?", uri);
        Assert.Contains("secret=JBSWY3DPEHPK3PXP", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=NexoPBX", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Secrets_are_random_and_long_enough()
    {
        var service = Service();
        var a = service.GenerateSecret();

        Assert.NotEqual(a, service.GenerateSecret());
        Assert.Equal(20, Base32Encoding.ToBytes(a).Length);
    }
}
