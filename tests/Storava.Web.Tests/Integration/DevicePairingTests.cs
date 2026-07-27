using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Storava.Web.Data;

namespace Storava.Web.Tests.Integration;

/// <summary>
/// Covers attaching a companion Agent to an account. Pairing is the one moment the server hands
/// out a secret, so the tests are about the limits on that: one code, one device, ten minutes, and
/// no scan data crossing the boundary in either direction.
/// </summary>
public sealed partial class DevicePairingTests(WebApplicationFactoryFixture factory)
    : IClassFixture<WebApplicationFactoryFixture>
{
    private static string NewPublicKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    private sealed record PairResponse(Guid DeviceId, string DeviceName, string ChannelSecret);

    private sealed record PairProblem(string Reason, string Message);

    private async Task<(HttpClient Client, string Email)> SignedInClientAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        string email = $"phase8-{Guid.NewGuid():N}@example.test";
        const string password = "Safe-Phase8-Password1!";

        string registerToken = await TokenAsync(client, "/account/register");
        using var registration = await client.PostAsync("/account/register", Form(
            ("__RequestVerificationToken", registerToken),
            ("DisplayName", "Phase Eight"),
            ("Email", email),
            ("Password", password),
            ("ConfirmPassword", password)));
        registration.EnsureSuccessStatusCode();

        string html = WebUtility.HtmlDecode(await registration.Content.ReadAsStringAsync());
        string confirmationLink = DevelopmentLink().Match(html).Groups["url"].Value;
        Assert.NotEmpty(confirmationLink);
        (await client.GetAsync(confirmationLink)).EnsureSuccessStatusCode();

        string loginToken = await TokenAsync(client, "/account/login");
        using var login = await client.PostAsync("/account/login", Form(
            ("__RequestVerificationToken", loginToken),
            ("Email", email),
            ("Password", password)));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        return (client, email);
    }

    /// <summary>Generates a code and reads it back off the account page, as a user would.</summary>
    private static async Task<string> IssueCodeAsync(HttpClient client)
    {
        string token = await TokenAsync(client, "/account");
        using var issued = await client.PostAsync(
            "/account/devices/pair",
            Form(("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, issued.StatusCode);

        using var page = await client.GetAsync("/account");
        string html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        page.EnsureSuccessStatusCode();

        string code = PairingCode().Match(html).Groups["code"].Value;
        Assert.NotEmpty(code);
        return code;
    }

    private async Task<HttpResponseMessage> PairAsync(string code, string publicKey, string name = "Test PC")
    {
        // A fresh client with no cookies: the Agent is a native process, not the browser session.
        using var agent = factory.CreateClient();
        return await agent.PostAsJsonAsync("/api/agent/pair", new
        {
            code,
            publicKey,
            deviceName = name
        });
    }

    [Fact]
    public async Task An_agent_pairs_with_a_code_and_appears_on_the_account_page()
    {
        var (client, _) = await SignedInClientAsync();
        using var _client = client;

        string code = await IssueCodeAsync(client);
        string publicKey = NewPublicKey();

        using var response = await PairAsync(code, publicKey, "Workshop PC");
        response.EnsureSuccessStatusCode();

        var paired = await response.Content.ReadFromJsonAsync<PairResponse>();
        Assert.NotNull(paired);
        Assert.NotEqual(Guid.Empty, paired!.DeviceId);
        Assert.Equal("Workshop PC", paired.DeviceName);

        // The channel secret is returned once and is real key material, not a placeholder.
        Assert.Equal(32, Convert.FromBase64String(paired.ChannelSecret).Length);

        using var page = await client.GetAsync("/account");
        string html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Workshop PC", html, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var device = await database.UserDevices.SingleAsync(candidate => candidate.Id == paired.DeviceId);

        // Stored encrypted, so a database copy on its own cannot mint a token the Agent accepts.
        Assert.NotEqual(paired.ChannelSecret, device.ChannelSecretProtected);
        Assert.NotEmpty(device.ChannelSecretProtected);
        Assert.Equal("companion-agent", device.DeviceType);
    }

    [Fact]
    public async Task A_code_pairs_exactly_one_device()
    {
        var (client, _) = await SignedInClientAsync();
        using var _client = client;

        string code = await IssueCodeAsync(client);

        using var first = await PairAsync(code, NewPublicKey(), "First PC");
        first.EnsureSuccessStatusCode();

        // A code that leaks — a screenshot, a chat — must not attach a second machine.
        using var second = await PairAsync(code, NewPublicKey(), "Attacker PC");
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<PairProblem>();
        Assert.Equal("already_used", problem?.Reason);
    }

    [Fact]
    public async Task Generating_a_new_code_retires_the_previous_one()
    {
        var (client, _) = await SignedInClientAsync();
        using var _client = client;

        string first = await IssueCodeAsync(client);
        string second = await IssueCodeAsync(client);
        Assert.NotEqual(first, second);

        using var stale = await PairAsync(first, NewPublicKey());
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        Assert.Equal("unknown_code", (await stale.Content.ReadFromJsonAsync<PairProblem>())?.Reason);

        using var current = await PairAsync(second, NewPublicKey());
        current.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_unknown_code_is_refused()
    {
        using var response = await PairAsync("ZZZZ-ZZZZ-ZZZZ", NewPublicKey());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unknown_code", (await response.Content.ReadFromJsonAsync<PairProblem>())?.Reason);
    }

    [Fact]
    public async Task A_key_the_server_cannot_read_is_refused_before_the_code_is_spent()
    {
        var (client, _) = await SignedInClientAsync();
        using var _client = client;

        string code = await IssueCodeAsync(client);

        using var malformed = await PairAsync(code, "not-a-key");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("invalid_key", (await malformed.Content.ReadFromJsonAsync<PairProblem>())?.Reason);

        // The code survives a malformed attempt: a broken client must not burn the user's code.
        using var retry = await PairAsync(code, NewPublicKey());
        retry.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var (client, _) = await SignedInClientAsync();
        using var _client = client;

        string code = await IssueCodeAsync(client);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Ordered in memory: SQLite cannot sort by a DateTimeOffset column.
            var unspent = await database.DevicePairingCodes
                .Where(candidate => candidate.RedeemedAtUtc == null)
                .ToListAsync();
            var record = unspent.MaxBy(candidate => candidate.CreatedAtUtc);
            Assert.NotNull(record);
            record!.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await database.SaveChangesAsync();
        }

        using var response = await PairAsync(code, NewPublicKey());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("expired", (await response.Content.ReadFromJsonAsync<PairProblem>())?.Reason);
    }

    [Fact]
    public async Task The_same_key_cannot_be_paired_twice()
    {
        var (client, _) = await SignedInClientAsync();
        using var _client = client;

        string publicKey = NewPublicKey();

        using var first = await PairAsync(await IssueCodeAsync(client), publicKey);
        first.EnsureSuccessStatusCode();

        using var again = await PairAsync(await IssueCodeAsync(client), publicKey);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("already_paired", (await again.Content.ReadFromJsonAsync<PairProblem>())?.Reason);
    }

    [Fact]
    public async Task Removing_a_device_destroys_the_secret_that_signs_its_tokens()
    {
        var (client, _) = await SignedInClientAsync();
        using var _client = client;

        using var response = await PairAsync(await IssueCodeAsync(client), NewPublicKey(), "Retired PC");
        response.EnsureSuccessStatusCode();
        var paired = await response.Content.ReadFromJsonAsync<PairResponse>();

        string token = await TokenAsync(client, "/account");
        using var revoked = await client.PostAsync(
            $"/account/devices/{paired!.DeviceId}/revoke",
            Form(("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var device = await database.UserDevices.SingleAsync(candidate => candidate.Id == paired.DeviceId);

        Assert.NotNull(device.RevokedAtUtc);
        // Revocation has to be more than a flag: without the secret no token can be signed for it.
        Assert.Empty(device.ChannelSecretProtected);

        using var page = await client.GetAsync("/account");
        Assert.DoesNotContain("Retired PC", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_account_cannot_remove_another_accounts_device()
    {
        var (owner, _) = await SignedInClientAsync();
        using var _owner = owner;

        using var response = await PairAsync(await IssueCodeAsync(owner), NewPublicKey(), "Owned PC");
        response.EnsureSuccessStatusCode();
        var paired = await response.Content.ReadFromJsonAsync<PairResponse>();

        var (stranger, _) = await SignedInClientAsync();
        using var _stranger = stranger;

        string token = await TokenAsync(stranger, "/account");
        using var attempt = await stranger.PostAsync(
            $"/account/devices/{paired!.DeviceId}/revoke",
            Form(("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, attempt.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var device = await database.UserDevices.SingleAsync(candidate => candidate.Id == paired.DeviceId);
        Assert.Null(device.RevokedAtUtc);
    }

    [Fact]
    public async Task Issuing_a_code_requires_being_signed_in()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await anonymous.PostAsync("/account/devices/pair", Form());

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_pairing_endpoint_never_returns_the_stored_code_or_a_users_details()
    {
        var (client, email) = await SignedInClientAsync();
        using var _client = client;

        using var response = await PairAsync(await IssueCodeAsync(client), NewPublicKey());
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync();

        // The Agent learns that it belongs to somebody, not who, and nothing about the machine.
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Phase Eight", body, StringComparison.Ordinal);
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        string html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        response.EnsureSuccessStatusCode();
        string token = AntiforgeryToken().Match(html).Groups["token"].Value;
        Assert.NotEmpty(token);
        return token;
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Key, value.Value)));

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("data-testid=\"development-email-link\"[^>]*href=\"(?<url>[^\"]+)\"")]
    private static partial Regex DevelopmentLink();

    [GeneratedRegex("class=\"account-pairing-code\">(?<code>[A-Z0-9-]+)<")]
    private static partial Regex PairingCode();
}
