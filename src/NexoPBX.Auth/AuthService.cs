// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.Options;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Abstractions.Security;
using NexoPBX.Abstractions.Time;
using NexoPBX.Auth.Passwords;
using NexoPBX.Auth.Tokens;
using NexoPBX.Auth.Totp;
using NexoPBX.Store;
using NexoPBX.Store.Entities;
using NexoPBX.Store.Repositories;

namespace NexoPBX.Auth;

/// <summary>Result of a login attempt.</summary>
public enum LoginStatus
{
    /// <summary>Logged in; a session token was issued.</summary>
    Success,

    /// <summary>Unknown user or wrong password (deliberately indistinguishable).</summary>
    InvalidCredentials,

    /// <summary>Too many failures; try again later.</summary>
    Locked,

    /// <summary>Password was right but a second-factor code is needed.</summary>
    TotpRequired,

    /// <summary>The second-factor code was wrong, expired or already used.</summary>
    InvalidTotp,
}

/// <summary>Outcome of <see cref="AuthService.LoginAsync"/>.</summary>
public sealed record LoginResult(LoginStatus Status, string? Token = null, DateTime? ExpiresAt = null, User? User = null, DateTime? LockedUntil = null);

/// <summary>Start of a TOTP enrolment: show the URI as a QR code and ask for the first code.</summary>
public sealed record TotpEnrollment(string Secret, string ProvisioningUri);

/// <summary>User lifecycle and login. Every outcome is audited.</summary>
public sealed class AuthService
{
    private readonly IUserRepository _users;
    private readonly ITokenRepository _tokenRepository;
    private readonly TokenService _tokens;
    private readonly IPasswordHasher _hasher;
    private readonly PasswordPolicy _policy;
    private readonly ITotpService _totp;
    private readonly ISecretProtector _secrets;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly AuthOptions _options;

    // A valid hash of a random string, verified when the user does not exist so that
    // "no such user" and "wrong password" take the same time.
    private readonly string _decoyHash;

    /// <summary>Creates the service.</summary>
    public AuthService(
        IUserRepository users,
        ITokenRepository tokenRepository,
        TokenService tokens,
        IPasswordHasher hasher,
        PasswordPolicy policy,
        ITotpService totp,
        ISecretProtector secrets,
        IAuditWriter audit,
        IClock clock,
        IIdGenerator ids,
        IOptions<AuthOptions> options)
    {
        _users = users;
        _tokenRepository = tokenRepository;
        _tokens = tokens;
        _hasher = hasher;
        _policy = policy;
        _totp = totp;
        _secrets = secrets;
        _audit = audit;
        _clock = clock;
        _ids = ids;
        _options = options.Value;
        _decoyHash = hasher.Hash(Guid.NewGuid().ToString("N"));
    }

    /// <summary>Creates a user. The caller must already be allowed to (see <see cref="Scopes.UsersWrite"/>).</summary>
    public async Task<User> CreateUserAsync(
        string username,
        string displayName,
        string password,
        Role role,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        username = (username ?? string.Empty).Trim();
        if (username.Length is < 3 or > 100 || username.Any(char.IsWhiteSpace))
        {
            throw new NexoValidationException("username", "Username must be 3-100 characters without spaces.");
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200)
        {
            throw new NexoValidationException("display_name", "Display name is required (up to 200 characters).");
        }

        _policy.EnsureAcceptable(password, username);

        var user = new User
        {
            Id = _ids.NewId(),
            Username = username,
            DisplayName = displayName.Trim(),
            PasswordHash = _hasher.Hash(password),
            Role = role,
            CreatedAt = _clock.UtcNow,
        };

        try
        {
            await _users.AddAsync(user, cancellationToken);
        }
        catch (DuplicateException)
        {
            await _audit.WriteAsync(new AuditEntry("user.create", AuditOutcome.Failure, actorUserId, TargetType: "user", Details: Reason("duplicate_username")), cancellationToken);
            throw;
        }

        await _audit.WriteAsync(
            new AuditEntry("user.create", AuditOutcome.Success, actorUserId, TargetType: "user", TargetId: user.Id.ToString(), Details: new Dictionary<string, string> { ["role"] = role.ToString() }),
            cancellationToken);
        return user;
    }

    /// <summary>Creates the first Owner. Refused once any user exists, so it cannot be abused after setup.</summary>
    public async Task<User> BootstrapOwnerAsync(string username, string displayName, string password, CancellationToken cancellationToken)
    {
        if (await _users.CountAsync(cancellationToken) > 0)
        {
            throw new InvalidOperationException("The installation already has users; bootstrap is only for a fresh install.");
        }

        return await CreateUserAsync(username, displayName, password, Role.Owner, actorUserId: null, cancellationToken);
    }

    /// <summary>Checks credentials and, on success, issues a short-lived session token.</summary>
    public async Task<LoginResult> LoginAsync(string username, string password, string? totpCode, string? remoteIp, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var user = await _users.FindByUsernameAsync(username ?? string.Empty, cancellationToken);

        if (user is null || !user.IsActive)
        {
            _ = _hasher.Verify(password ?? string.Empty, _decoyHash);
            await _audit.WriteAsync(new AuditEntry("auth.login", AuditOutcome.Failure, RemoteIp: remoteIp, Details: Reason("unknown_or_inactive")), cancellationToken);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        if (user.LockedUntil is { } until && until > now)
        {
            await _audit.WriteAsync(new AuditEntry("auth.login", AuditOutcome.Denied, user.Id, TargetType: "user", TargetId: user.Id.ToString(), RemoteIp: remoteIp, Details: Reason("locked")), cancellationToken);
            return new LoginResult(LoginStatus.Locked, LockedUntil: until);
        }

        if (!_hasher.Verify(password ?? string.Empty, user.PasswordHash))
        {
            return await RegisterFailureAsync(user, remoteIp, "bad_password", cancellationToken);
        }

        if (user.TotpEnabled)
        {
            if (string.IsNullOrWhiteSpace(totpCode))
            {
                return new LoginResult(LoginStatus.TotpRequired);
            }

            var secret = _secrets.Unprotect(user.TotpSecretProtected!);
            if (!_totp.TryVerify(secret, totpCode.Trim(), user.TotpLastStep, out var step))
            {
                var failed = await RegisterFailureAsync(user, remoteIp, "bad_totp", cancellationToken);
                return failed.Status == LoginStatus.Locked ? failed : new LoginResult(LoginStatus.InvalidTotp);
            }

            user.TotpLastStep = step;
        }

        user.FailedLogins = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        if (_hasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = _hasher.Hash(password!);
        }

        await _users.UpdateAsync(user, expectedVersion: null, cancellationToken);

        IEnumerable<string> scopes = user.Role == Role.ApiClient ? Array.Empty<string>() : Scopes.ForRole(user.Role);
        var issued = await _tokens.IssueAsync(user.Id, "session", scopes, TokenKind.Session, _options.SessionLifetime, cancellationToken);
        await _audit.WriteAsync(new AuditEntry("auth.login", AuditOutcome.Success, user.Id, issued.Entity.Id, "user", user.Id.ToString(), remoteIp), cancellationToken);
        return new LoginResult(LoginStatus.Success, issued.Plaintext, issued.Entity.ExpiresAt, user);
    }

    /// <summary>Starts TOTP enrolment. The secret is stored encrypted but not active until confirmed.</summary>
    public async Task<TotpEnrollment> BeginTotpEnrollmentAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        if (user.TotpEnabled)
        {
            throw new InvalidOperationException("Two-factor authentication is already enabled.");
        }

        var secret = _totp.GenerateSecret();
        user.TotpSecretProtected = _secrets.Protect(secret);
        user.TotpLastStep = 0;
        await _users.UpdateAsync(user, expectedVersion: null, cancellationToken);
        return new TotpEnrollment(secret, _totp.BuildProvisioningUri(secret, user.Username));
    }

    /// <summary>Activates TOTP if the code is valid. Returns false for a wrong code.</summary>
    public async Task<bool> ConfirmTotpEnrollmentAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        if (user.TotpSecretProtected is null)
        {
            throw new InvalidOperationException("Enrolment has not been started.");
        }

        var secret = _secrets.Unprotect(user.TotpSecretProtected);
        if (!_totp.TryVerify(secret, (code ?? string.Empty).Trim(), user.TotpLastStep, out var step))
        {
            await _audit.WriteAsync(new AuditEntry("auth.totp.enroll", AuditOutcome.Failure, userId, TargetType: "user", TargetId: userId.ToString()), cancellationToken);
            return false;
        }

        user.TotpEnabled = true;
        user.TotpLastStep = step;
        await _users.UpdateAsync(user, expectedVersion: null, cancellationToken);
        await _audit.WriteAsync(new AuditEntry("auth.totp.enroll", AuditOutcome.Success, userId, TargetType: "user", TargetId: userId.ToString()), cancellationToken);
        return true;
    }

    /// <summary>Changes a password, after checking the old one, and revokes every live token of the user.</summary>
    public async Task ChangePasswordAsync(Guid userId, string oldPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        if (!_hasher.Verify(oldPassword ?? string.Empty, user.PasswordHash))
        {
            await _audit.WriteAsync(new AuditEntry("auth.password.change", AuditOutcome.Failure, userId, TargetType: "user", TargetId: userId.ToString(), Details: Reason("bad_password")), cancellationToken);
            throw new NexoValidationException("old_password", "The current password is not correct.");
        }

        _policy.EnsureAcceptable(newPassword, user.Username);
        user.PasswordHash = _hasher.Hash(newPassword);
        await _users.UpdateAsync(user, expectedVersion: null, cancellationToken);
        var revoked = await _tokenRepository.RevokeAllForUserAsync(userId, _clock.UtcNow, cancellationToken);
        await _audit.WriteAsync(
            new AuditEntry("auth.password.change", AuditOutcome.Success, userId, TargetType: "user", TargetId: userId.ToString(), Details: new Dictionary<string, string> { ["tokens_revoked"] = revoked.ToString(System.Globalization.CultureInfo.InvariantCulture) }),
            cancellationToken);
    }

    private async Task<LoginResult> RegisterFailureAsync(User user, string? remoteIp, string reason, CancellationToken cancellationToken)
    {
        user.FailedLogins++;
        var locked = user.FailedLogins >= _options.MaxFailedLogins;
        if (locked)
        {
            user.LockedUntil = _clock.UtcNow + _options.LockoutDuration;
            user.FailedLogins = 0;
        }

        await _users.UpdateAsync(user, expectedVersion: null, cancellationToken);
        await _audit.WriteAsync(
            new AuditEntry("auth.login", AuditOutcome.Failure, user.Id, TargetType: "user", TargetId: user.Id.ToString(), RemoteIp: remoteIp, Details: Reason(locked ? reason + "+locked" : reason)),
            cancellationToken);
        return locked
            ? new LoginResult(LoginStatus.Locked, LockedUntil: user.LockedUntil)
            : new LoginResult(LoginStatus.InvalidCredentials);
    }

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _users.FindByIdAsync(userId, cancellationToken) ?? throw new KeyNotFoundException($"User {userId} not found.");

    private static Dictionary<string, string> Reason(string reason) => new() { ["reason"] = reason };
}
