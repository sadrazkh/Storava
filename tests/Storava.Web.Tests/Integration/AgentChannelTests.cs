using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Storava.Contracts.Agent;
using Storava.Web.Data;

namespace Storava.Web.Tests.Integration;

/// <summary>
/// Covers the pass the browser presents to a companion Agent. The server's whole role in the
/// channel is deciding who may connect, so these tests are about who may not.
/// </summary>
public sealed partial class AgentChannelTests(WebApplicationFactoryFixture factory)
    : IClassFixture<WebApplicationFactoryFixture>
{
    private sealed record PairResponse(Guid DeviceId, string DeviceName, string ChannelSecret);

    private sealed record TokenResponse(string Token, DateTimeOffset ExpiresAtUtc, int[] Ports, int Protocol);

    private sealed record DeviceListItem(Guid Id, string DisplayName, DateTimeOffset LastSeenAtUtc);

    private static string NewPublicKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        string email = $"channel-{Guid.NewGuid():N}@example.test";
        const string password = "Safe-Phase8-Password1!";

        string registerToken = await TokenAsync(client, "/account/register");
        using var registration = await client.PostAsync("/account/register", Form(
            ("__RequestVerificationToken", registerToken),
            ("DisplayName", "Channel Tester"),
            ("Email", email),
            ("Password", password),
            ("ConfirmPassword", password)));
        registration.EnsureSuccessStatusCode();

        string html = WebUtility.HtmlDecode(await registration.Content.ReadAsStringAsync());
        (await client.GetAsync(DevelopmentLink().Match(html).Groups["url"].Value)).EnsureSuccessStatusCode();

        string loginToken = await TokenAsync(client, "/account/login");
        using var login = await client.PostAsync("/account/login", Form(
            ("__RequestVerificationToken", loginToken),
            ("Email", email),
            ("Password", password)));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        return client;
    }

    /// <summary>Signs in, pairs an Agent, and hands back everything the tests need about it.</summary>
    private async Task<(HttpClient Client, PairResponse Device)> PairedAsync()
    {
        var client = await SignedInClientAsync();

        string pageToken = await TokenAsync(client, "/account");
        using var issued = await client.PostAsync(
            "/account/devices/pair",
            Form(("__RequestVerificationToken", pageToken)));
        Assert.Equal(HttpStatusCode.Redirect, issued.StatusCode);

        using var page = await client.GetAsync("/account");
        string html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        string code = PairingCode().Match(html).Groups["code"].Value;
        Assert.NotEmpty(code);

        using var agent = factory.CreateClient();
        using var paired = await agent.PostAsJsonAsync("/api/agent/pair", new
        {
            code,
            publicKey = NewPublicKey(),
            deviceName = "Channel PC"
        });
        paired.EnsureSuccessStatusCode();

        return (client, (await paired.Content.ReadFromJsonAsync<PairResponse>())!);
    }

    private static async Task<HttpResponseMessage> AskForTokenAsync(HttpClient client, Guid deviceId)
    {
        string antiforgery = await TokenAsync(client, "/scan");
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/account/devices/{deviceId}/access-token");
        request.Headers.Add("X-Storava-Antiforgery", antiforgery);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task A_signed_in_page_gets_a_pass_its_own_agent_will_accept()
    {
        var (client, device) = await PairedAsync();
        using var _client = client;

        using var response = await AskForTokenAsync(client, device.DeviceId);
        response.EnsureSuccessStatusCode();

        var issued = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(issued);
        Assert.Equal(AgentEndpoints.ProtocolVersion, issued!.Protocol);
        Assert.Equal(AgentEndpoints.Ports, issued.Ports);

        // The Agent holds the same secret and verifies without asking anyone — which is the point:
        // the server is not in the path between the page and the Agent.
        var verified = AgentAccessToken.Verify(
            issued.Token,
            device.ChannelSecret,
            device.DeviceId,
            "http://localhost",
            DateTimeOffset.UtcNow);

        Assert.Equal(AgentTokenStatus.Valid, verified.Status);
    }

    [Fact]
    public async Task The_pass_is_bound_to_the_site_that_issued_it()
    {
        var (client, device) = await PairedAsync();
        using var _client = client;

        using var response = await AskForTokenAsync(client, device.DeviceId);
        var issued = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;

        // An Agent paired to a different deployment must not accept this, however it was obtained.
        var elsewhere = AgentAccessToken.Verify(
            issued.Token,
            device.ChannelSecret,
            device.DeviceId,
            "https://storava.evil",
            DateTimeOffset.UtcNow);

        Assert.Equal(AgentTokenStatus.WrongOrigin, elsewhere.Status);
    }

    [Fact]
    public async Task The_pass_expires_quickly()
    {
        var (client, device) = await PairedAsync();
        using var _client = client;

        using var response = await AskForTokenAsync(client, device.DeviceId);
        var issued = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;

        Assert.True(issued.ExpiresAtUtc <= DateTimeOffset.UtcNow.Add(AgentAccessToken.DefaultLifetime).AddSeconds(5));

        var later = AgentAccessToken.Verify(
            issued.Token,
            device.ChannelSecret,
            device.DeviceId,
            "http://localhost",
            DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(AgentTokenStatus.Expired, later.Status);
    }

    [Fact]
    public async Task A_removed_device_gets_no_further_passes()
    {
        var (client, device) = await PairedAsync();
        using var _client = client;

        string pageToken = await TokenAsync(client, "/account");
        using var revoked = await client.PostAsync(
            $"/account/devices/{device.DeviceId}/revoke",
            Form(("__RequestVerificationToken", pageToken)));
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);

        using var response = await AskForTokenAsync(client, device.DeviceId);

        // Revocation destroyed the secret, so there is nothing left to sign with.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task One_account_cannot_get_a_pass_for_another_accounts_agent()
    {
        var (owner, device) = await PairedAsync();
        using var _owner = owner;

        using var stranger = await SignedInClientAsync();
        using var response = await AskForTokenAsync(stranger, device.DeviceId);

        // Same answer as an unknown id: nothing here should confirm that the device exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_device_is_not_distinguishable_from_someone_elses()
    {
        using var client = await SignedInClientAsync();

        using var response = await AskForTokenAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_page_gets_no_pass_and_no_device_list()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var list = await anonymous.GetAsync("/api/account/devices");
        using var token = await anonymous.PostAsync(
            $"/api/account/devices/{Guid.NewGuid()}/access-token",
            content: null);

        Assert.NotEqual(HttpStatusCode.OK, list.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, token.StatusCode);
    }

    [Fact]
    public async Task A_page_without_the_antiforgery_header_gets_no_pass()
    {
        var (client, device) = await PairedAsync();
        using var _client = client;

        using var response = await client.PostAsync(
            $"/api/account/devices/{device.DeviceId}/access-token",
            content: null);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_device_list_shows_the_users_own_agents()
    {
        var (client, device) = await PairedAsync();
        using var _client = client;

        using var response = await client.GetAsync("/api/account/devices");
        response.EnsureSuccessStatusCode();

        var devices = await response.Content.ReadFromJsonAsync<List<DeviceListItem>>();
        var listed = Assert.Single(devices!);
        Assert.Equal(device.DeviceId, listed.Id);
        Assert.Equal("Channel PC", listed.DisplayName);
    }

    [Fact]
    public async Task Asking_for_a_pass_records_that_the_page_tried()
    {
        var (client, device) = await PairedAsync();
        using var _client = client;

        DateTimeOffset before;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            before = (await database.UserDevices.SingleAsync(d => d.Id == device.DeviceId)).LastSeenAtUtc;
        }

        await Task.Delay(20);
        (await AskForTokenAsync(client, device.DeviceId)).EnsureSuccessStatusCode();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var after = (await database.UserDevices.SingleAsync(d => d.Id == device.DeviceId)).LastSeenAtUtc;

            // The server never hears from the Agent, so this is the only "last active" it can
            // honestly record — and the account page labels it that way.
            Assert.True(after > before);
        }
    }

    [Fact]
    public async Task The_page_may_open_a_connection_to_a_loopback_agent()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/scan");
        response.EnsureSuccessStatusCode();

        string csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        // Without these the browser blocks the request before the Agent is ever reached.
        foreach (string origin in AgentEndpoints.ConnectSources())
            Assert.Contains(origin, csp, StringComparison.Ordinal);

        // And the permission stays narrow: loopback only, and only the fixed ports.
        Assert.DoesNotContain("http://127.0.0.1:*", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_scan_page_gives_an_anonymous_visitor_no_antiforgery_token()
    {
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync("/scan");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-signed-in=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("data-antiforgery-token=\"\"", html, StringComparison.Ordinal);
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        string html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        response.EnsureSuccessStatusCode();

        // The scan page carries its token in a data attribute rather than a form field.
        string token = AntiforgeryToken().Match(html).Groups["token"].Value;
        if (token.Length == 0)
            token = ScanPageToken().Match(html).Groups["token"].Value;

        Assert.NotEmpty(token);
        return token;
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Key, value.Value)));

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("data-antiforgery-token=\"(?<token>[^\"]+)\"")]
    private static partial Regex ScanPageToken();

    [GeneratedRegex("data-testid=\"development-email-link\"[^>]*href=\"(?<url>[^\"]+)\"")]
    private static partial Regex DevelopmentLink();

    [GeneratedRegex("class=\"account-pairing-code\">(?<code>[A-Z0-9-]+)<")]
    private static partial Regex PairingCode();
}
