using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Storava.Web.Data;

namespace Storava.Web.Tests.Integration;

public sealed partial class AccountTests(WebApplicationFactoryFixture factory)
    : IClassFixture<WebApplicationFactoryFixture>
{
    [Fact]
    public async Task Registration_confirmation_login_and_session_dashboard_work_end_to_end()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var email = $"phase7-{Guid.NewGuid():N}@example.test";
        const string password = "Safe-Phase7-Password1!";

        var registerToken = await GetAntiforgeryTokenAsync(client, "/account/register");
        using var registerResponse = await client.PostAsync(
            "/account/register",
            Form(
                ("__RequestVerificationToken", registerToken),
                ("DisplayName", "Phase Seven"),
                ("Email", email),
                ("Password", password),
                ("ConfirmPassword", password)));
        var registerHtml = WebUtility.HtmlDecode(await registerResponse.Content.ReadAsStringAsync());

        registerResponse.EnsureSuccessStatusCode();
        var confirmationLink = DevelopmentLink().Match(registerHtml).Groups["url"].Value;
        Assert.NotEmpty(confirmationLink);
        Assert.StartsWith(
            "https://accounts.storava.test/account/confirm-email",
            confirmationLink,
            StringComparison.Ordinal);

        using var confirmationResponse = await client.GetAsync(confirmationLink);
        var confirmationHtml = await confirmationResponse.Content.ReadAsStringAsync();
        confirmationResponse.EnsureSuccessStatusCode();
        Assert.Contains("Continue to sign in", confirmationHtml, StringComparison.Ordinal);

        var loginToken = await GetAntiforgeryTokenAsync(client, "/account/login");
        using var loginResponse = await client.PostAsync(
            "/account/login",
            Form(
                ("__RequestVerificationToken", loginToken),
                ("Email", email),
                ("Password", password),
                ("RememberMe", "true")));

        var loginHtml = await loginResponse.Content.ReadAsStringAsync();
        Assert.True(
            loginResponse.StatusCode == HttpStatusCode.Redirect,
            $"Expected login redirect, got {(int)loginResponse.StatusCode}: {loginHtml}");
        Assert.Equal("/account", loginResponse.Headers.Location?.OriginalString);

        using var dashboardResponse = await client.GetAsync("/account");
        var dashboardHtml = await dashboardResponse.Content.ReadAsStringAsync();
        dashboardResponse.EnsureSuccessStatusCode();
        Assert.Contains("Phase Seven", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("Current session", dashboardHtml, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await database.Users.CountAsync(user => user.Email == email));
        Assert.Equal(1, await database.AccountSessions.CountAsync(session => session.RevokedAtUtc == null));
    }

    [Fact]
    public async Task Anonymous_account_dashboard_redirects_to_login()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/account");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(
            "http://localhost/account/login",
            response.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        response.EnsureSuccessStatusCode();
        var token = AntiforgeryToken().Match(html).Groups["token"].Value;
        Assert.NotEmpty(token);
        return token;
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Key, value.Value)));

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("data-testid=\"development-email-link\"[^>]*href=\"(?<url>[^\"]+)\"")]
    private static partial Regex DevelopmentLink();
}
