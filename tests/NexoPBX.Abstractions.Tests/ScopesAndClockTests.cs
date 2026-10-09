// SPDX-License-Identifier: AGPL-3.0-only
using NexoPBX.Abstractions.Security;
using NexoPBX.Testing;

namespace NexoPBX.Abstractions.Tests;

public sealed class ScopesAndClockTests
{
    [Fact]
    public void Owner_and_admin_get_every_scope()
    {
        Assert.Equal(Scopes.All.Count, Scopes.ForRole(Role.Owner).Count);
        Assert.Equal(Scopes.All.Count, Scopes.ForRole(Role.Admin).Count);
    }

    [Fact]
    public void Read_only_cannot_write_and_api_client_starts_with_nothing()
    {
        Assert.DoesNotContain(Scopes.UsersWrite, Scopes.ForRole(Role.ReadOnly));
        Assert.Empty(Scopes.ForRole(Role.ApiClient));
    }

    [Fact]
    public void Every_role_has_a_scope_set()
    {
        foreach (var role in Enum.GetValues<Role>())
        {
            Assert.NotNull(Scopes.ForRole(role));
        }
    }

    [Fact]
    public void Unknown_scopes_are_rejected()
    {
        Assert.True(Scopes.AreValid([Scopes.SystemRead, Scopes.EventsSubscribe]));
        Assert.False(Scopes.AreValid([Scopes.SystemRead, "nonsense.scope"]));
    }

    [Fact]
    public void Virtual_clock_moves_only_when_told()
    {
        var clock = new VirtualClock();
        var start = clock.UtcNow;
        var stamp = clock.GetTimestamp();

        Assert.Equal(start, clock.UtcNow);

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(start.AddMinutes(5), clock.UtcNow);
        Assert.Equal(TimeSpan.FromMinutes(5), clock.GetElapsed(stamp));
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
    }
}
