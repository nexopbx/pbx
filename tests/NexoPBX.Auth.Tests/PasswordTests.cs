// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.Options;
using NexoPBX.Auth.Passwords;

namespace NexoPBX.Auth.Tests;

public sealed class PasswordTests
{
    private static Argon2PasswordHasher Hasher(int memory = 64, int iterations = 1) =>
        new(Options.Create(new AuthOptions { Argon2MemoryKiB = memory, Argon2Iterations = iterations }));

    [Fact]
    public void Hash_is_phc_formatted_and_verifies()
    {
        var hasher = Hasher();
        var hash = hasher.Hash("correct horse battery");

        Assert.StartsWith("$argon2id$v=19$m=64,t=1,p=1$", hash);
        Assert.True(hasher.Verify("correct horse battery", hash));
        Assert.False(hasher.Verify("correct horse batterz", hash));
        Assert.DoesNotContain("correct", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_password_gets_a_different_salt_every_time()
    {
        var hasher = Hasher();

        Assert.NotEqual(hasher.Hash("same password here"), hasher.Hash("same password here"));
    }

    [Fact]
    public void Garbage_stored_hashes_fail_verification_without_throwing()
    {
        var hasher = Hasher();

        foreach (var garbage in new[] { "", "plaintext", "$argon2id$v=19$m=64,t=1,p=1$AAAA", "$argon2id$v=19$m=x,t=1,p=1$AAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAA", "$bcrypt$whatever" })
        {
            Assert.False(hasher.Verify("anything", garbage), garbage);
        }
    }

    [Fact]
    public void Hashes_made_with_weaker_parameters_are_flagged_for_rehash()
    {
        var weak = Hasher(memory: 64, iterations: 1).Hash("some long password");
        var strong = Hasher(memory: 128, iterations: 2);

        Assert.True(strong.NeedsRehash(weak));
        Assert.False(strong.NeedsRehash(strong.Hash("some long password")));
        Assert.True(strong.NeedsRehash("garbage"));
        Assert.True(strong.Verify("some long password", weak)); // old hashes keep working
    }

    [Fact]
    public void Production_defaults_meet_the_owasp_minimum()
    {
        var defaults = new AuthOptions();

        Assert.True(defaults.Argon2MemoryKiB >= 19 * 1024);
        Assert.True(defaults.Argon2Iterations >= 2);
        Assert.True(Hasher(defaults.Argon2MemoryKiB, defaults.Argon2Iterations).Verify("x-long-enough-password", Hasher(defaults.Argon2MemoryKiB, defaults.Argon2Iterations).Hash("x-long-enough-password")));
    }

    [Theory]
    [InlineData("short", "alice")]
    [InlineData("aaaaaaaaaaaaaaaa", "alice")]
    [InlineData("my-alice-password-1", "alice")]
    public void Unacceptable_passwords_are_refused(string password, string username)
    {
        var policy = new PasswordPolicy(Options.Create(new AuthOptions()));

        Assert.Throws<NexoValidationException>(() => policy.EnsureAcceptable(password, username));
    }

    [Fact]
    public void Overlong_passwords_are_refused_to_bound_hashing_cost()
    {
        var policy = new PasswordPolicy(Options.Create(new AuthOptions()));

        Assert.Throws<NexoValidationException>(() => policy.EnsureAcceptable(new string('x', 10) + new string('y', 300), "bob"));
    }

    [Fact]
    public void A_long_passphrase_is_accepted()
    {
        var policy = new PasswordPolicy(Options.Create(new AuthOptions()));

        policy.EnsureAcceptable("correct horse battery staple", "bob");
    }
}
