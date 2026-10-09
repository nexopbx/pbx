// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NexoPBX.Abstractions.Events;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Security;
using NexoPBX.Auth.Tokens;
using NexoPBX.Auth.Totp;
using NexoPBX.Store;
using NexoPBX.Store.Entities;
using NexoPBX.Store.Repositories;
using OtpNet;

namespace NexoPBX.Auth.Tests;

public sealed class AuthServiceTests : IClassFixture<AuthFixture>
{
    private const string Password = "correct horse battery";
    private readonly AuthFixture _f;

    public AuthServiceTests(AuthFixture fixture) => _f = fixture;

    private Task<LoginResult> LoginAsync(string user, string password, string? code = null) =>
        _f.InScopeAsync(sp => sp.GetRequiredService<AuthService>().LoginAsync(user, password, code, "203.0.113.9", default));

    private Task<IReadOnlyList<AuditEvent>> AuditAsync() =>
        _f.InScopeAsync<IReadOnlyList<AuditEvent>>(async sp => (await sp.GetRequiredService<IAuditRepository>().ListAsync(200, null, default)).Items);

    [Fact]
    public async Task Login_succeeds_and_the_token_authenticates_as_that_user()
    {
        var user = await _f.CreateUserAsync("login-ok", Role.Supervisor);

        var result = await LoginAsync("LOGIN-OK", Password);

        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.NotNull(result.Token);
        Assert.Equal(_f.Clock.UtcNow.AddHours(12), result.ExpiresAt);

        var principal = await _f.InScopeAsync(sp => sp.GetRequiredService<TokenAuthenticator>().AuthenticateAsync(result.Token, default));
        Assert.Equal(user.Id, principal!.UserId);
        Assert.Equal(TokenKind.Session, principal.Kind);
        Assert.Equal(Scopes.ForRole(Role.Supervisor).Order(), principal.Scopes.Order());
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_look_the_same()
    {
        await _f.CreateUserAsync("login-bad", Role.Agent);

        var wrong = await LoginAsync("login-bad", "not the password at all");
        var unknown = await LoginAsync("nobody-here", "not the password at all");

        Assert.Equal(LoginStatus.InvalidCredentials, wrong.Status);
        Assert.Equal(LoginStatus.InvalidCredentials, unknown.Status);
        Assert.Null(wrong.Token);
    }

    [Fact]
    public async Task Five_failures_lock_the_account_until_the_lockout_expires()
    {
        await _f.CreateUserAsync("lockme", Role.Agent);

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(LoginStatus.InvalidCredentials, (await LoginAsync("lockme", "wrong password here")).Status);
        }

        var fifth = await LoginAsync("lockme", "wrong password here");
        Assert.Equal(LoginStatus.Locked, fifth.Status);
        Assert.Equal(_f.Clock.UtcNow.AddMinutes(15), fifth.LockedUntil);

        // Even the right password is refused while locked.
        Assert.Equal(LoginStatus.Locked, (await LoginAsync("lockme", Password)).Status);

        _f.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(LoginStatus.Success, (await LoginAsync("lockme", Password)).Status);
    }

    [Fact]
    public async Task A_success_resets_the_failure_counter()
    {
        await _f.CreateUserAsync("resetme", Role.Agent);
        for (var i = 0; i < 3; i++)
        {
            await LoginAsync("resetme", "wrong password here");
        }

        Assert.Equal(LoginStatus.Success, (await LoginAsync("resetme", Password)).Status);

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(LoginStatus.InvalidCredentials, (await LoginAsync("resetme", "wrong password here")).Status);
        }
    }

    [Fact]
    public async Task Inactive_users_cannot_log_in()
    {
        var user = await _f.CreateUserAsync("inactive-one", Role.Agent);
        await _f.InScopeAsync(async sp =>
        {
            var repo = sp.GetRequiredService<IUserRepository>();
            var u = (await repo.FindByIdAsync(user.Id, default))!;
            u.IsActive = false;
            await repo.UpdateAsync(u, null, default);
            return 0;
        });

        Assert.Equal(LoginStatus.InvalidCredentials, (await LoginAsync("inactive-one", Password)).Status);
    }

    [Fact]
    public async Task Two_factor_flow_requires_a_fresh_valid_code_each_time()
    {
        var user = await _f.CreateUserAsync("twofactor", Role.Admin);
        var enrollment = await _f.InScopeAsync(sp => sp.GetRequiredService<AuthService>().BeginTotpEnrollmentAsync(user.Id, default));
        var totp = new OtpNet.Totp(Base32Encoding.ToBytes(enrollment.Secret));

        Assert.StartsWith("otpauth://totp/", enrollment.ProvisioningUri);
        Assert.False(await _f.InScopeAsync(sp => sp.GetRequiredService<AuthService>().ConfirmTotpEnrollmentAsync(user.Id, "000000", default)));
        Assert.True(await _f.InScopeAsync(sp => sp.GetRequiredService<AuthService>().ConfirmTotpEnrollmentAsync(user.Id, totp.ComputeTotp(_f.Clock.UtcNow), default)));

        // The secret is encrypted at rest.
        var stored = await _f.InScopeAsync(sp => sp.GetRequiredService<IUserRepository>().FindByIdAsync(user.Id, default));
        Assert.DoesNotContain(enrollment.Secret, stored!.TotpSecretProtected!, StringComparison.Ordinal);

        _f.Clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(LoginStatus.TotpRequired, (await LoginAsync("twofactor", Password)).Status);
        Assert.Equal(LoginStatus.InvalidTotp, (await LoginAsync("twofactor", Password, "123456")).Status);

        var code = totp.ComputeTotp(_f.Clock.UtcNow);
        Assert.Equal(LoginStatus.Success, (await LoginAsync("twofactor", Password, code)).Status);
        Assert.Equal(LoginStatus.InvalidTotp, (await LoginAsync("twofactor", Password, code)).Status); // replay
    }

    [Fact]
    public async Task Bootstrap_creates_the_first_owner_exactly_once()
    {
        // Other tests already created users in this database, so bootstrap must now refuse.
        await _f.CreateUserAsync("someone", Role.Agent);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _f.InScopeAsync(sp => sp.GetRequiredService<AuthService>().BootstrapOwnerAsync("owner", "Owner", Password, default)));
    }

    [Fact]
    public async Task Duplicate_usernames_and_weak_passwords_are_refused()
    {
        await _f.CreateUserAsync("taken-name", Role.Agent);

        await Assert.ThrowsAsync<DuplicateException>(() => _f.CreateUserAsync("TAKEN-NAME", Role.Agent));
        await Assert.ThrowsAsync<NexoValidationException>(() => _f.CreateUserAsync("weakling", Role.Agent, "short"));
        await Assert.ThrowsAsync<NexoValidationException>(() => _f.CreateUserAsync("x y", Role.Agent));
    }

    [Fact]
    public async Task Changing_the_password_revokes_existing_sessions()
    {
        var user = await _f.CreateUserAsync("changer", Role.Agent);
        var session = (await LoginAsync("changer", Password)).Token;

        await _f.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<AuthService>().ChangePasswordAsync(user.Id, Password, "an entirely new passphrase", default);
            return 0;
        });

        Assert.Null(await _f.InScopeAsync(sp => sp.GetRequiredService<TokenAuthenticator>().AuthenticateAsync(session, default)));
        Assert.Equal(LoginStatus.InvalidCredentials, (await LoginAsync("changer", Password)).Status);
        Assert.Equal(LoginStatus.Success, (await LoginAsync("changer", "an entirely new passphrase")).Status);
    }

    [Fact]
    public async Task Changing_the_password_requires_the_old_one()
    {
        var user = await _f.CreateUserAsync("changer2", Role.Agent);

        await Assert.ThrowsAsync<NexoValidationException>(() => _f.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<AuthService>().ChangePasswordAsync(user.Id, "wrong old password", "an entirely new passphrase", default);
            return 0;
        }));
    }

    [Fact]
    public async Task Every_outcome_is_audited_and_no_secret_ever_reaches_the_audit_log()
    {
        using var sub = _f.Bus.Subscribe(new SubscriptionOptions { TenantId = TenantIds.Default, TypePrefixes = [EventTypes.AuditRecorded] });
        await _f.CreateUserAsync("audited", Role.Agent);
        await LoginAsync("audited", "definitely the wrong one");
        var ok = await LoginAsync("audited", Password);

        var audit = await AuditAsync();
        var actions = audit.Select(a => (a.Action, a.Outcome)).ToList();
        Assert.Contains(("user.create", AuditOutcome.Success), actions);
        Assert.Contains(("auth.login", AuditOutcome.Failure), actions);
        Assert.Contains(("auth.login", AuditOutcome.Success), actions);

        var everything = JsonSerializer.Serialize(audit);
        Assert.DoesNotContain(Password, everything, StringComparison.Ordinal);
        Assert.DoesNotContain("definitely the wrong one", everything, StringComparison.Ordinal);
        Assert.DoesNotContain(ok.Token!, everything, StringComparison.Ordinal);

        // And the same records were announced on the bus.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var seen = new List<string>();
        await foreach (var e in sub.ReadAllAsync(cts.Token))
        {
            seen.Add(e.Data["action"]);
            if (seen.Count == 3)
            {
                break;
            }
        }

        Assert.Equal(["user.create", "auth.login", "auth.login"], seen);
    }
}
