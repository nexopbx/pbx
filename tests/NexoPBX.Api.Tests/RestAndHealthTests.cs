// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NexoPBX.Api.Tests;

public sealed class RestAndHealthTests : IClassFixture<HostFixture>
{
    private readonly HostFixture _host;

    public RestAndHealthTests(HostFixture host) => _host = host;

    [Fact]
    public async Task Liveness_and_readiness_are_public()
    {
        using var http = _host.NewHttpClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/live")).StatusCode);
        var ready = await http.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task System_endpoints_need_a_token()
    {
        using var anonymous = _host.NewHttpClient();
        using var bogus = _host.NewHttpClient("nxp_" + new string('A', 43));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/v1/system/info")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bogus.GetAsync("/v1/system/info")).StatusCode);
    }

    [Fact]
    public async Task A_logged_in_owner_can_read_system_info_over_rest()
    {
        var token = await _host.LoginAsOwnerAsync();
        using var http = _host.NewHttpClient(token);

        var info = await http.GetFromJsonAsync<JsonElement>("/v1/system/info");

        Assert.Equal("NexoPBX", info.GetProperty("product").GetString());
        Assert.Equal("headless", info.GetProperty("runMode").GetString());
        Assert.Equal("sqlite", info.GetProperty("databaseProvider").GetString());

        var health = await http.GetFromJsonAsync<JsonElement>("/v1/system/health");
        Assert.Equal("HEALTH_STATUS_HEALTHY", health.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Login_errors_are_machine_readable_and_use_proper_http_codes()
    {
        using var http = _host.NewHttpClient();

        var response = await http.PostAsJsonAsync("/v1/auth:login", new { username = "owner", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var info = body.GetProperty("details")[0];
        Assert.Equal("INVALID_CREDENTIALS", info.GetProperty("reason").GetString());
        Assert.Equal("nexopbx.v1", info.GetProperty("domain").GetString());
    }

    [Fact]
    public async Task A_token_without_the_scope_gets_403_not_data()
    {
        var token = await _host.CreateUserAndLoginAsync("rest-agent", Abstractions.Security.Role.Agent);
        using var http = _host.NewHttpClient(token);

        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/v1/system/info")).StatusCode);
    }

    [Fact]
    public async Task The_openapi_document_describes_every_operation_with_its_required_scope()
    {
        using var http = _host.NewHttpClient();

        var doc = await http.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        Assert.StartsWith("3.1", doc.GetProperty("openapi").GetString());
        var login = doc.GetProperty("paths").GetProperty("/v1/auth:login").GetProperty("post");
        Assert.Equal(0, login.GetProperty("security").GetArrayLength());
        var create = doc.GetProperty("paths").GetProperty("/v1/users").GetProperty("post");
        Assert.Equal("users.write", create.GetProperty("x-required-scopes")[0].GetString());
        Assert.True(doc.GetProperty("components").GetProperty("schemas").TryGetProperty("nexopbx.v1.User", out _));
    }

    [Fact]
    public async Task Security_headers_do_not_leak_the_server_banner()
    {
        using var http = _host.NewHttpClient();

        var response = await http.GetAsync("/health/live");

        Assert.False(response.Headers.Contains("Server"));
    }
}
