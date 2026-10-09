// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Extensions.DependencyInjection;
using NexoPBX.Abstractions.Security;
using NexoPBX.Auth.Tokens;
using NexoPBX.Store.Entities;
using NexoPBX.Store.Repositories;

namespace NexoPBX.Auth.Tests;

public sealed class TokenTests : IClassFixture<AuthFixture>
{
    private readonly AuthFixture _f;

    public TokenTests(AuthFixture fixture) => _f = fixture;

    private Task<IssuedToken> IssueAsync(Guid? userId, IEnumerable<string> scopes, TimeSpan? lifetime = null, TokenKind kind = TokenKind.Api) =>
        _f.InScopeAsync(sp => sp.GetRequiredService<TokenService>().IssueAsync(userId, "test", scopes, kind, lifetime, default));

    private Task<AuthenticatedPrincipal?> AuthenticateAsync(string? token) =>
        _f.InScopeAsync(sp => sp.GetRequiredService<TokenAuthenticator>().AuthenticateAsync(token, default));

    [Fact]
    public async Task Issued_tokens_are_opaque_and_only_their_hash_is_stored()
    {
        var issued = await IssueAsync(null, [Scopes.SystemRead]);

        Assert.StartsWith("nxp_", issued.Plaintext);
        Assert.Equal(47, issued.Plaintext.Length);
        Assert.Equal(TokenService.Hash(issued.Plaintext), issued.Entity.TokenHash);
        Assert.DoesNotContain(issued.Plaintext, issued.Entity.TokenHash, StringComparison.Ordinal);
        Assert.Equal(issued.Plaintext[..8], issued.Entity.Prefix);
    }

    [Fact]
    public async Task Two_tokens_are_never_equal()
    {
        var a = await IssueAsync(null, [Scopes.SystemRead]);
        var b = await IssueAsync(null, [Scopes.SystemRead]);

        Assert.NotEqual(a.Plaintext, b.Plaintext);
    }

    [Fact]
    public async Task A_valid_token_authenticates_with_exactly_its_scopes()
    {
        var issued = await IssueAsync(null, [Scopes.SystemRead, Scopes.EventsSubscribe]);

        var principal = await AuthenticateAsync(issued.Plaintext);

        Assert.NotNull(principal);
        Assert.Equal(new[] { Scopes.EventsSubscribe, Scopes.SystemRead }, principal.Scopes.Order());
        Assert.Null(principal.UserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nxp_tooshort")]
    [InlineData("Bearer abc")]
    public async Task Garbage_does_not_authenticate(string? token)
    {
        Assert.Null(await AuthenticateAsync(token));
    }

    [Fact]
    public async Task A_well_formed_but_unknown_token_does_not_authenticate()
    {
        Assert.Null(await AuthenticateAsync("nxp_" + new string('A', 43)));
    }

    [Fact]
    public async Task Revoked_tokens_stop_working()
    {
        var issued = await IssueAsync(null, [Scopes.SystemRead]);
        Assert.NotNull(await AuthenticateAsync(issued.Plaintext));

        Assert.True(await _f.InScopeAsync(sp => sp.GetRequiredService<TokenService>().RevokeAsync(issued.Entity.Id, default)));

        Assert.Null(await AuthenticateAsync(issued.Plaintext));
    }

    [Fact]
    public async Task Tokens_expire_when_the_clock_passes_their_lifetime()
    {
        var issued = await IssueAsync(null, [Scopes.SystemRead], TimeSpan.FromHours(1));
        Assert.NotNull(await AuthenticateAsync(issued.Plaintext));

        _f.Clock.Advance(TimeSpan.FromMinutes(59));
        Assert.NotNull(await AuthenticateAsync(issued.Plaintext));

        _f.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await AuthenticateAsync(issued.Plaintext));
    }

    [Fact]
    public async Task A_token_cannot_carry_more_than_its_users_role_allows()
    {
        var agent = await _f.CreateUserAsync("agent-scopes", Role.Agent);
        var issued = await IssueAsync(agent.Id, [Scopes.SystemRead, Scopes.UsersWrite]);

        var principal = await AuthenticateAsync(issued.Plaintext);

        Assert.NotNull(principal);
        Assert.Contains(Scopes.SystemRead, principal.Scopes);
        Assert.DoesNotContain(Scopes.UsersWrite, principal.Scopes);
        Assert.Equal(Role.Agent, principal.Role);
    }

    [Fact]
    public async Task Api_client_users_get_exactly_the_scopes_of_their_token()
    {
        var client = await _f.CreateUserAsync("machine-1", Role.ApiClient);
        var issued = await IssueAsync(client.Id, [Scopes.AuditRead]);

        var principal = await AuthenticateAsync(issued.Plaintext);

        Assert.Equal([Scopes.AuditRead], principal!.Scopes);
    }

    [Fact]
    public async Task Deactivating_a_user_kills_their_tokens_immediately()
    {
        var user = await _f.CreateUserAsync("soon-gone", Role.Admin);
        var issued = await IssueAsync(user.Id, [Scopes.SystemRead]);
        Assert.NotNull(await AuthenticateAsync(issued.Plaintext));

        await _f.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<IUserRepository>();
            var u = (await users.FindByIdAsync(user.Id, default))!;
            u.IsActive = false;
            await users.UpdateAsync(u, null, default);
            return 0;
        });

        Assert.Null(await AuthenticateAsync(issued.Plaintext));
    }

    [Fact]
    public async Task Demoting_a_user_narrows_existing_tokens()
    {
        var user = await _f.CreateUserAsync("demoted", Role.Admin);
        var issued = await IssueAsync(user.Id, [Scopes.SystemRead, Scopes.UsersWrite]);
        Assert.Contains(Scopes.UsersWrite, (await AuthenticateAsync(issued.Plaintext))!.Scopes);

        await _f.InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<IUserRepository>();
            var u = (await users.FindByIdAsync(user.Id, default))!;
            u.Role = Role.ReadOnly;
            await users.UpdateAsync(u, null, default);
            return 0;
        });

        var after = await AuthenticateAsync(issued.Plaintext);
        Assert.DoesNotContain(Scopes.UsersWrite, after!.Scopes);
        Assert.Contains(Scopes.SystemRead, after.Scopes);
    }

    [Fact]
    public async Task Unknown_scopes_cannot_be_issued()
    {
        await Assert.ThrowsAsync<NexoValidationException>(() => IssueAsync(null, ["bogus.scope"]));
    }

    [Fact]
    public async Task Last_used_is_recorded_but_only_at_coarse_granularity()
    {
        var issued = await IssueAsync(null, [Scopes.SystemRead]);
        await AuthenticateAsync(issued.Plaintext);
        var first = await _f.InScopeAsync(sp => sp.GetRequiredService<ITokenRepository>().FindByIdAsync(issued.Entity.Id, default));
        Assert.NotNull(first!.LastUsedAt);

        _f.Clock.Advance(TimeSpan.FromMinutes(1));
        await AuthenticateAsync(issued.Plaintext);
        var second = await _f.InScopeAsync(sp => sp.GetRequiredService<ITokenRepository>().FindByIdAsync(issued.Entity.Id, default));
        Assert.Equal(first.LastUsedAt, second!.LastUsedAt);

        _f.Clock.Advance(TimeSpan.FromMinutes(10));
        await AuthenticateAsync(issued.Plaintext);
        var third = await _f.InScopeAsync(sp => sp.GetRequiredService<ITokenRepository>().FindByIdAsync(issued.Entity.Id, default));
        Assert.True(third!.LastUsedAt > first.LastUsedAt);
    }
}
