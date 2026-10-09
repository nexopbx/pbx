// SPDX-License-Identifier: AGPL-3.0-only
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using NexoPBX.Abstractions.Security;
using NexoPBX.Contracts.V1;
using Role = NexoPBX.Contracts.V1.Role;
using User = NexoPBX.Contracts.V1.User;

namespace NexoPBX.Api.Tests;

public sealed class GrpcTests : IClassFixture<HostFixture>
{
    private readonly HostFixture _host;

    public GrpcTests(HostFixture host) => _host = host;

    private static string ReasonOf(RpcException ex) => ex.GetRpcStatus()!.Details[0].Unpack<ErrorInfo>().Reason;

    [Fact]
    public async Task Native_grpc_over_http2_login_and_system_info()
    {
        var token = await _host.LoginAsOwnerAsync();
        using var channel = _host.NewGrpcChannel();

        var info = await new SystemService.SystemServiceClient(channel).GetInfoAsync(new GetInfoRequest(), HostFixture.Bearer(token));

        Assert.Equal("NexoPBX", info.Product);
        Assert.True(info.Uptime.ToTimeSpan() >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Grpc_web_over_http1_works_for_browsers()
    {
        var token = await _host.LoginAsOwnerAsync();
        using var channel = _host.NewGrpcWebChannel();

        var me = await new UserService.UserServiceClient(channel).GetMeAsync(new Empty(), HostFixture.Bearer(token));

        Assert.Equal("owner", me.Username);
        Assert.Equal(Role.Owner, me.Role);
    }

    [Fact]
    public async Task Calls_without_a_token_are_unauthenticated()
    {
        using var channel = _host.NewGrpcChannel();

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await new SystemService.SystemServiceClient(channel).GetInfoAsync(new GetInfoRequest()));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task Wrong_credentials_return_a_stable_reason_code()
    {
        using var channel = _host.NewGrpcChannel();

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await new UserService.UserServiceClient(channel).LoginAsync(new LoginRequest { Username = "owner", Password = "nope" }));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", ReasonOf(ex));
    }

    [Fact]
    public async Task Owner_creates_a_user_who_cannot_then_manage_users()
    {
        var owner = await _host.LoginAsOwnerAsync();
        using var channel = _host.NewGrpcChannel();
        var users = new UserService.UserServiceClient(channel);

        var created = await users.CreateUserAsync(
            new CreateUserRequest { Username = "alice", DisplayName = "Alice", Password = "a long enough password", Role = Role.Agent },
            HostFixture.Bearer(owner));
        Assert.Equal(Role.Agent, created.Role);

        var aliceToken = (await users.LoginAsync(new LoginRequest { Username = "alice", Password = "a long enough password" })).Token;
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await users.ListUsersAsync(new ListUsersRequest(), HostFixture.Bearer(aliceToken)));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task Duplicate_usernames_report_USERNAME_TAKEN()
    {
        var owner = await _host.LoginAsOwnerAsync();
        using var channel = _host.NewGrpcChannel();
        var users = new UserService.UserServiceClient(channel);
        var request = new CreateUserRequest { Username = "dupe", DisplayName = "Dupe", Password = "a long enough password", Role = Role.Agent };
        await users.CreateUserAsync(request, HostFixture.Bearer(owner));

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await users.CreateUserAsync(request, HostFixture.Bearer(owner)));

        Assert.Equal(StatusCode.AlreadyExists, ex.StatusCode);
        Assert.Equal("USERNAME_TAKEN", ReasonOf(ex));
    }

    [Fact]
    public async Task Weak_passwords_are_rejected_with_a_field_violation()
    {
        var owner = await _host.LoginAsOwnerAsync();
        using var channel = _host.NewGrpcChannel();

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await new UserService.UserServiceClient(channel).CreateUserAsync(
            new CreateUserRequest { Username = "weak", DisplayName = "Weak", Password = "short", Role = Role.Agent },
            HostFixture.Bearer(owner)));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        var bad = ex.GetRpcStatus()!.Details.First(d => d.Is(BadRequest.Descriptor)).Unpack<BadRequest>();
        Assert.Equal("password", bad.FieldViolations[0].Field);
    }

    [Fact]
    public async Task Stale_etags_are_refused_and_fresh_ones_accepted()
    {
        var owner = await _host.LoginAsOwnerAsync();
        using var channel = _host.NewGrpcChannel();
        var users = new UserService.UserServiceClient(channel);
        var created = await users.CreateUserAsync(
            new CreateUserRequest { Username = "etag-user", DisplayName = "One", Password = "a long enough password", Role = Role.Agent },
            HostFixture.Bearer(owner));

        UpdateUserRequest Update(string name, string etag) => new()
        {
            Id = created.Id,
            User = new User { DisplayName = name },
            UpdateMask = new FieldMask { Paths = { "display_name" } },
            Etag = etag,
        };

        var first = await users.UpdateUserAsync(Update("Two", created.Etag), HostFixture.Bearer(owner));
        Assert.Equal("Two", first.DisplayName);
        Assert.NotEqual(created.Etag, first.Etag);

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await users.UpdateUserAsync(Update("Three", created.Etag), HostFixture.Bearer(owner)));
        Assert.Equal(StatusCode.Aborted, ex.StatusCode);
        Assert.Equal("ETAG_MISMATCH", ReasonOf(ex));
    }

    [Fact]
    public async Task Nobody_can_change_their_own_role_or_demote_an_owner_they_are_not()
    {
        var owner = await _host.LoginAsOwnerAsync();
        var adminToken = await _host.CreateUserAndLoginAsync("an-admin", Abstractions.Security.Role.Admin);
        using var channel = _host.NewGrpcChannel();
        var users = new UserService.UserServiceClient(channel);
        var ownerId = (await users.GetMeAsync(new Empty(), HostFixture.Bearer(owner))).Id;
        var adminId = (await users.GetMeAsync(new Empty(), HostFixture.Bearer(adminToken))).Id;

        var selfEx = await Assert.ThrowsAsync<RpcException>(async () => await users.UpdateUserAsync(new UpdateUserRequest
        {
            Id = adminId,
            User = new User { Role = Role.Owner },
            UpdateMask = new FieldMask { Paths = { "role" } },
        }, HostFixture.Bearer(adminToken)));
        Assert.Equal(StatusCode.FailedPrecondition, selfEx.StatusCode);
        Assert.Equal("CANNOT_MODIFY_SELF", ReasonOf(selfEx));

        var ownerEx = await Assert.ThrowsAsync<RpcException>(async () => await users.UpdateUserAsync(new UpdateUserRequest
        {
            Id = ownerId,
            User = new User { DisplayName = "Pwned" },
            UpdateMask = new FieldMask { Paths = { "display_name" } },
        }, HostFixture.Bearer(adminToken)));
        Assert.Equal(StatusCode.PermissionDenied, ownerEx.StatusCode);
        Assert.Equal("OWNER_REQUIRED", ReasonOf(ownerEx));
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_session_immediately()
    {
        var owner = await _host.LoginAsOwnerAsync();
        var victimToken = await _host.CreateUserAndLoginAsync("victim", Abstractions.Security.Role.Agent);
        using var channel = _host.NewGrpcChannel();
        var users = new UserService.UserServiceClient(channel);
        var victimId = (await users.GetMeAsync(new Empty(), HostFixture.Bearer(victimToken))).Id;

        await users.UpdateUserAsync(new UpdateUserRequest
        {
            Id = victimId,
            User = new User { IsActive = false },
            UpdateMask = new FieldMask { Paths = { "is_active" } },
        }, HostFixture.Bearer(owner));

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await users.GetMeAsync(new Empty(), HostFixture.Bearer(victimToken)));
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task Api_tokens_work_but_can_never_exceed_the_creators_permissions()
    {
        var agent = await _host.CreateUserAndLoginAsync("token-agent", Abstractions.Security.Role.Agent);
        using var channel = _host.NewGrpcChannel();
        var users = new UserService.UserServiceClient(channel);

        var ok = await users.CreateApiTokenAsync(
            new CreateApiTokenRequest { Name = "mine", Scopes = { Scopes.SystemRead } }, HostFixture.Bearer(agent));
        var info = await new SystemService.SystemServiceClient(channel).GetInfoAsync(new GetInfoRequest(), HostFixture.Bearer(ok.Token));
        Assert.Equal("NexoPBX", info.Product);
        Assert.StartsWith("nxp_", ok.Token);

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await users.CreateApiTokenAsync(
            new CreateApiTokenRequest { Name = "greedy", Scopes = { Scopes.UsersWrite } }, HostFixture.Bearer(agent)));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Equal("SCOPE_NOT_GRANTABLE", ReasonOf(ex));

        // The list never reveals the secret, only a prefix.
        var listed = await users.ListApiTokensAsync(new ListApiTokensRequest(), HostFixture.Bearer(agent));
        Assert.Contains(listed.ApiTokens, t => t.Name == "mine" && t.Prefix == ok.Token[..8]);

        await users.RevokeApiTokenAsync(new RevokeApiTokenRequest { Id = ok.ApiToken.Id }, HostFixture.Bearer(agent));
        var revoked = await Assert.ThrowsAsync<RpcException>(async () =>
            await new SystemService.SystemServiceClient(channel).GetInfoAsync(new GetInfoRequest(), HostFixture.Bearer(ok.Token)));
        Assert.Equal(StatusCode.Unauthenticated, revoked.StatusCode);
    }

    [Fact]
    public async Task One_user_cannot_see_or_revoke_anothers_tokens()
    {
        var a = await _host.CreateUserAndLoginAsync("tok-a", Abstractions.Security.Role.Agent);
        var b = await _host.CreateUserAndLoginAsync("tok-b", Abstractions.Security.Role.Agent);
        using var channel = _host.NewGrpcChannel();
        var users = new UserService.UserServiceClient(channel);
        var created = await users.CreateApiTokenAsync(new CreateApiTokenRequest { Name = "a-token", Scopes = { Scopes.SystemRead } }, HostFixture.Bearer(a));

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await users.RevokeApiTokenAsync(new RevokeApiTokenRequest { Id = created.ApiToken.Id }, HostFixture.Bearer(b)));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Two_factor_login_end_to_end()
    {
        await _host.CreateUserAndLoginAsync("totp-user", Abstractions.Security.Role.Agent);
        var token = await _host.LoginAsync("totp-user", HostFixture.OwnerPassword);
        using var channel = _host.NewGrpcChannel();
        var users = new UserService.UserServiceClient(channel);

        var enrollment = await users.BeginTotpEnrollmentAsync(new Empty(), HostFixture.Bearer(token));
        var totp = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(enrollment.Secret));
        await users.ConfirmTotpEnrollmentAsync(new ConfirmTotpRequest { Code = totp.ComputeTotp() }, HostFixture.Bearer(token));

        var required = await Assert.ThrowsAsync<RpcException>(async () =>
            await users.LoginAsync(new LoginRequest { Username = "totp-user", Password = HostFixture.OwnerPassword }));
        Assert.Equal("TOTP_REQUIRED", ReasonOf(required));
    }

    [Fact]
    public async Task Event_stream_delivers_audit_events_live_over_grpc()
    {
        var owner = await _host.LoginAsOwnerAsync();
        using var channel = _host.NewGrpcChannel();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var call = new EventService.EventServiceClient(channel)
            .Subscribe(new SubscribeRequest { TypePrefixes = { "audit." } }, HostFixture.Bearer(owner), cancellationToken: cts.Token);
        await call.ResponseHeadersAsync; // subscription is live once headers arrive

        using var login = _host.NewGrpcChannel();
        await Assert.ThrowsAsync<RpcException>(async () =>
            await new UserService.UserServiceClient(login).LoginAsync(new LoginRequest { Username = "owner", Password = "wrong" }));

        Assert.True(await call.ResponseStream.MoveNext(cts.Token));
        Assert.Equal("audit.recorded", call.ResponseStream.Current.Type);
        Assert.Equal("auth.login", call.ResponseStream.Current.Data["action"]);
    }

    [Fact]
    public async Task Event_stream_requires_its_scope()
    {
        var noEvents = (await new UserService.UserServiceClient(_host.NewGrpcChannel()).CreateApiTokenAsync(
            new CreateApiTokenRequest { Name = "narrow", Scopes = { Scopes.SystemRead } },
            HostFixture.Bearer(await _host.LoginAsOwnerAsync()))).Token;
        using var channel = _host.NewGrpcChannel();
        using var call = new EventService.EventServiceClient(channel).Subscribe(new SubscribeRequest(), HostFixture.Bearer(noEvents));

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(CancellationToken.None));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }
}
