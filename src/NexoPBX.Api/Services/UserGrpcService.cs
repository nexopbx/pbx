// SPDX-License-Identifier: AGPL-3.0-only
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using NexoPBX.Abstractions.Security;
using NexoPBX.Abstractions.Time;
using NexoPBX.Api.Infrastructure;
using NexoPBX.Auth;
using NexoPBX.Auth.Http;
using NexoPBX.Auth.Tokens;
using NexoPBX.Contracts.V1;
using NexoPBX.Store;
using NexoPBX.Store.Entities;
using NexoPBX.Store.Repositories;
using ContractRole = NexoPBX.Contracts.V1.Role;
using DomainRole = NexoPBX.Abstractions.Security.Role;
using ProtoUser = NexoPBX.Contracts.V1.User;

namespace NexoPBX.Api.Services;

/// <summary>Implements <c>nexopbx.v1.UserService</c>: login, users and API tokens.</summary>
public sealed class UserGrpcService : UserService.UserServiceBase
{
    private static readonly HashSet<string> UpdatablePaths = new(["display_name", "role", "is_active"], StringComparer.Ordinal);
    private readonly AuthService _auth;
    private readonly IUserRepository _users;
    private readonly ITokenRepository _tokens;
    private readonly TokenService _tokenService;
    private readonly IAuditWriter _audit;

    /// <summary>Creates the service.</summary>
    public UserGrpcService(AuthService auth, IUserRepository users, ITokenRepository tokens, TokenService tokenService, IAuditWriter audit)
    {
        _auth = auth;
        _users = users;
        _tokens = tokens;
        _tokenService = tokenService;
        _audit = audit;
    }

    /// <inheritdoc />
    [AllowAnonymous]
    public override async Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        var ip = context.GetHttpContext().Connection.RemoteIpAddress?.ToString();
        var result = await _auth.LoginAsync(
            request.Username,
            request.Password,
            string.IsNullOrWhiteSpace(request.TotpCode) ? null : request.TotpCode,
            ip,
            context.CancellationToken);

        return result.Status switch
        {
            LoginStatus.Success => new LoginResponse
            {
                Token = result.Token,
                ExpireTime = Mapping.ToTimestamp(result.ExpiresAt),
                User = Mapping.ToProto(result.User!),
            },
            LoginStatus.TotpRequired => throw ApiErrors.Create(StatusCode.Unauthenticated, "TOTP_REQUIRED", "A two-factor code is required."),
            LoginStatus.InvalidTotp => throw ApiErrors.Create(StatusCode.Unauthenticated, "INVALID_TOTP", "The two-factor code is not valid."),
            LoginStatus.Locked => throw ApiErrors.Create(
                StatusCode.ResourceExhausted,
                "ACCOUNT_LOCKED",
                "Too many failed attempts. Try again later.",
                new Dictionary<string, string> { ["locked_until"] = (result.LockedUntil ?? DateTime.UtcNow).ToString("O", System.Globalization.CultureInfo.InvariantCulture) }),
            _ => throw ApiErrors.Create(StatusCode.Unauthenticated, "INVALID_CREDENTIALS", "Invalid username or password."),
        };
    }

    /// <inheritdoc />
    [Authorize(AuthenticationSchemes = NexoAuthenticationDefaults.Scheme)]
    public override async Task<Empty> Logout(Empty request, ServerCallContext context)
    {
        var caller = Caller.From(context);
        await _tokenService.RevokeAsync(caller.TokenId, context.CancellationToken);
        await _audit.WriteAsync(new AuditEntry("auth.logout", AuditOutcome.Success, caller.UserId, caller.TokenId, RemoteIp: caller.RemoteIp), context.CancellationToken);
        return new Empty();
    }

    /// <inheritdoc />
    [Authorize(AuthenticationSchemes = NexoAuthenticationDefaults.Scheme)]
    public override async Task<ProtoUser> GetMe(Empty request, ServerCallContext context)
    {
        var id = Caller.From(context).RequireUserId();
        var user = await _users.FindByIdAsync(id, context.CancellationToken) ?? throw ApiErrors.NotFound("User");
        return Mapping.ToProto(user);
    }

    /// <inheritdoc />
    [Authorize(Policy = Scopes.UsersRead)]
    public override async Task<ListUsersResponse> ListUsers(ListUsersRequest request, ServerCallContext context)
    {
        var page = await _users.ListAsync(request.PageSize <= 0 ? 50 : request.PageSize, request.PageToken, context.CancellationToken);
        var response = new ListUsersResponse { NextPageToken = page.NextPageToken ?? string.Empty };
        response.Users.AddRange(page.Items.Select(Mapping.ToProto));
        return response;
    }

    /// <inheritdoc />
    [Authorize(Policy = Scopes.UsersWrite)]
    public override async Task<ProtoUser> CreateUser(CreateUserRequest request, ServerCallContext context)
    {
        var caller = Caller.From(context);
        var role = Mapping.ToDomain(request.Role, "role");
        if (role == DomainRole.Owner && caller.Role != DomainRole.Owner)
        {
            throw ApiErrors.Denied("OWNER_REQUIRED", "Only an Owner can create another Owner.");
        }

        try
        {
            var user = await _auth.CreateUserAsync(request.Username, request.DisplayName, request.Password, role, caller.UserId, context.CancellationToken);
            return Mapping.ToProto(user);
        }
        catch (DuplicateException)
        {
            throw ApiErrors.Create(StatusCode.AlreadyExists, "USERNAME_TAKEN", "That username is already taken.");
        }
    }

    /// <inheritdoc />
    [Authorize(Policy = Scopes.UsersWrite)]
    public override async Task<ProtoUser> UpdateUser(UpdateUserRequest request, ServerCallContext context)
    {
        var caller = Caller.From(context);
        var id = ParseId(request.Id, "id");
        var target = await _users.FindByIdAsync(id, context.CancellationToken) ?? throw ApiErrors.NotFound("User");

        var paths = request.UpdateMask?.Paths.ToList() ?? [];
        if (paths.Count == 0)
        {
            throw ApiErrors.Invalid("update_mask", "update_mask is required and must list the fields to change.");
        }

        if (paths.Find(p => !UpdatablePaths.Contains(p)) is { } bad)
        {
            throw ApiErrors.Invalid("update_mask", $"'{bad}' cannot be updated. Allowed: display_name, role, is_active.");
        }

        if (target.Role == DomainRole.Owner && caller.Role != DomainRole.Owner)
        {
            throw ApiErrors.Denied("OWNER_REQUIRED", "Only an Owner can modify an Owner.");
        }

        var changesAccess = paths.Contains("role") || paths.Contains("is_active");
        if (changesAccess && caller.UserId == target.Id)
        {
            throw ApiErrors.Create(StatusCode.FailedPrecondition, "CANNOT_MODIFY_SELF", "You cannot change your own role or active state.");
        }

        var proto = request.User ?? new ProtoUser();
        var deactivated = false;
        foreach (var path in paths)
        {
            switch (path)
            {
                case "display_name":
                    if (string.IsNullOrWhiteSpace(proto.DisplayName) || proto.DisplayName.Length > 200)
                    {
                        throw ApiErrors.Invalid("user.display_name", "Display name is required (up to 200 characters).");
                    }

                    target.DisplayName = proto.DisplayName.Trim();
                    break;
                case "role":
                    var role = Mapping.ToDomain(proto.Role, "user.role");
                    if (role == DomainRole.Owner && caller.Role != DomainRole.Owner)
                    {
                        throw ApiErrors.Denied("OWNER_REQUIRED", "Only an Owner can grant the Owner role.");
                    }

                    target.Role = role;
                    break;
                case "is_active":
                    deactivated = target.IsActive && !proto.IsActive;
                    target.IsActive = proto.IsActive;
                    break;
            }
        }

        await _users.UpdateAsync(target, Mapping.ParseEtag(request.Etag), context.CancellationToken);
        if (deactivated)
        {
            await _tokens.RevokeAllForUserAsync(target.Id, DateTime.UtcNow, context.CancellationToken);
        }

        await _audit.WriteAsync(
            new AuditEntry("user.update", AuditOutcome.Success, caller.UserId, caller.TokenId, "user", target.Id.ToString(), caller.RemoteIp, new Dictionary<string, string> { ["fields"] = string.Join(',', paths) }),
            context.CancellationToken);
        return Mapping.ToProto(target);
    }

    /// <inheritdoc />
    [Authorize(AuthenticationSchemes = NexoAuthenticationDefaults.Scheme)]
    public override async Task<Empty> ChangePassword(ChangePasswordRequest request, ServerCallContext context)
    {
        await _auth.ChangePasswordAsync(Caller.From(context).RequireUserId(), request.OldPassword, request.NewPassword, context.CancellationToken);
        return new Empty();
    }

    /// <inheritdoc />
    [Authorize(AuthenticationSchemes = NexoAuthenticationDefaults.Scheme)]
    public override async Task<NexoPBX.Contracts.V1.TotpEnrollment> BeginTotpEnrollment(Empty request, ServerCallContext context)
    {
        var enrollment = await _auth.BeginTotpEnrollmentAsync(Caller.From(context).RequireUserId(), context.CancellationToken);
        return new NexoPBX.Contracts.V1.TotpEnrollment { Secret = enrollment.Secret, ProvisioningUri = enrollment.ProvisioningUri };
    }

    /// <inheritdoc />
    [Authorize(AuthenticationSchemes = NexoAuthenticationDefaults.Scheme)]
    public override async Task<Empty> ConfirmTotpEnrollment(ConfirmTotpRequest request, ServerCallContext context)
    {
        var ok = await _auth.ConfirmTotpEnrollmentAsync(Caller.From(context).RequireUserId(), request.Code, context.CancellationToken);
        return ok ? new Empty() : throw ApiErrors.Create(StatusCode.InvalidArgument, "INVALID_TOTP", "The two-factor code is not valid.");
    }

    /// <inheritdoc />
    [Authorize(Policy = Scopes.TokensManage)]
    public override async Task<CreateApiTokenResponse> CreateApiToken(CreateApiTokenRequest request, ServerCallContext context)
    {
        var caller = Caller.From(context);
        var targetId = string.IsNullOrEmpty(request.UserId) ? caller.RequireUserId() : ParseId(request.UserId, "user_id");
        if (targetId != caller.UserId && !caller.Has(Scopes.UsersWrite))
        {
            throw ApiErrors.Denied("USERS_WRITE_REQUIRED", "Creating a token for another user needs the users.write scope.");
        }

        var target = await _users.FindByIdAsync(targetId, context.CancellationToken) ?? throw ApiErrors.NotFound("User");
        var requested = request.Scopes.Distinct(StringComparer.Ordinal).ToList();
        if (requested.Count == 0)
        {
            throw ApiErrors.Invalid("scopes", "At least one scope is required.");
        }

        if (!Scopes.AreValid(requested))
        {
            throw ApiErrors.Invalid("scopes", "One or more scopes are unknown.");
        }

        // A token can never be stronger than the user it acts as, nor than the person creating it.
        var allowedForTarget = target.Role == DomainRole.ApiClient ? Scopes.All : Scopes.ForRole(target.Role);
        var notGrantable = requested.Where(s => !allowedForTarget.Contains(s) || !caller.Has(s)).ToList();
        if (notGrantable.Count > 0)
        {
            await _audit.WriteAsync(
                new AuditEntry("token.create", AuditOutcome.Denied, caller.UserId, caller.TokenId, "user", targetId.ToString(), caller.RemoteIp, new Dictionary<string, string> { ["scopes"] = string.Join(' ', notGrantable) }),
                context.CancellationToken);
            throw ApiErrors.Denied("SCOPE_NOT_GRANTABLE", $"These scopes cannot be granted: {string.Join(", ", notGrantable)}.");
        }

        if (request.LifetimeSeconds is < 0 or > 10L * 365 * 24 * 3600)
        {
            throw ApiErrors.Invalid("lifetime_seconds", "Lifetime must be between 0 (never expires) and 10 years.");
        }

        var issued = await _tokenService.IssueAsync(
            targetId,
            request.Name,
            requested,
            TokenKind.Api,
            request.LifetimeSeconds > 0 ? TimeSpan.FromSeconds(request.LifetimeSeconds) : null,
            context.CancellationToken);
        await _audit.WriteAsync(
            new AuditEntry("token.create", AuditOutcome.Success, caller.UserId, caller.TokenId, "token", issued.Entity.Id.ToString(), caller.RemoteIp, new Dictionary<string, string> { ["prefix"] = issued.Entity.Prefix }),
            context.CancellationToken);
        return new CreateApiTokenResponse { Token = issued.Plaintext, ApiToken = Mapping.ToProto(issued.Entity) };
    }

    /// <inheritdoc />
    [Authorize(Policy = Scopes.TokensManage)]
    public override async Task<ListApiTokensResponse> ListApiTokens(ListApiTokensRequest request, ServerCallContext context)
    {
        var caller = Caller.From(context);
        var targetId = string.IsNullOrEmpty(request.UserId) ? caller.RequireUserId() : ParseId(request.UserId, "user_id");
        if (targetId != caller.UserId && !caller.Has(Scopes.UsersWrite))
        {
            throw ApiErrors.Denied("USERS_WRITE_REQUIRED", "Listing another user's tokens needs the users.write scope.");
        }

        var response = new ListApiTokensResponse();
        response.ApiTokens.AddRange((await _tokens.ListByUserAsync(targetId, context.CancellationToken)).Select(Mapping.ToProto));
        return response;
    }

    /// <inheritdoc />
    [Authorize(Policy = Scopes.TokensManage)]
    public override async Task<Empty> RevokeApiToken(RevokeApiTokenRequest request, ServerCallContext context)
    {
        var caller = Caller.From(context);
        var token = await _tokens.FindByIdAsync(ParseId(request.Id, "id"), context.CancellationToken) ?? throw ApiErrors.NotFound("Token");
        if (token.UserId != caller.UserId && !caller.Has(Scopes.UsersWrite))
        {
            // Same answer as "does not exist": do not reveal other people's token ids.
            throw ApiErrors.NotFound("Token");
        }

        await _tokenService.RevokeAsync(token.Id, context.CancellationToken);
        await _audit.WriteAsync(new AuditEntry("token.revoke", AuditOutcome.Success, caller.UserId, caller.TokenId, "token", token.Id.ToString(), caller.RemoteIp), context.CancellationToken);
        return new Empty();
    }

    private static Guid ParseId(string value, string field) =>
        Guid.TryParse(value, out var id) ? id : throw ApiErrors.Invalid(field, $"{field} is not a valid id.");
}
