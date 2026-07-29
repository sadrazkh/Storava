using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Storava.Agent.Channel;
using Storava.Agent.Identity;
using Storava.Contracts.Agent;

namespace Storava.Agent.Tests;

/// <summary>
/// Exercises the loopback listener for real: a running Kestrel, real HTTP, real tokens.
/// <para>
/// The port is reachable by anything running as this user, so what matters is not that a valid
/// request works but that everything else is turned away — and that the unauthenticated probe
/// gives away nothing but the fact that an Agent is here.
/// </para>
/// </summary>
[Collection(AgentServerCollection.Name)]
public sealed class AgentServerTests : IAsyncLifetime
{
    private const string Origin = "https://storava.example";

    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly string _secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly CancellationTokenSource _shutdown = new();

    private AgentServer _server = null!;
    private Task<int> _running = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _server = new AgentServer(
            new AgentRegistration
            {
                ServerBaseUrl = $"{Origin}/",
                DeviceId = _deviceId,
                DeviceName = "Test PC",
                ChannelSecret = _secret,
                PairedAtUtc = DateTimeOffset.UtcNow
            },
            "AAAA BBBB CCCC DDDD",
            // Never the real agent database, even though nothing here scans.
            Path.Combine(Path.GetTempPath(), $"storava-agent-channel-{Guid.NewGuid():N}.db"));

        _running = _server.RunAsync(_shutdown.Token);

        // Kestrel binds asynchronously; wait for the port rather than guessing at a delay.
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (int attempt = 0; attempt < 100 && _server.Port == 0; attempt++)
            await Task.Delay(20);

        _client.BaseAddress = new Uri(AgentEndpoints.BaseAddress(_server.Port));

        for (int attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                using var probe = await _client.GetAsync(AgentEndpoints.HelloPath);
                if (probe.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException("The agent did not start listening.");
    }

    public async Task DisposeAsync()
    {
        await _shutdown.CancelAsync();
        _client.Dispose();

        try
        {
            await _running;
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
    }

    private string ValidToken(Guid? device = null, string? origin = null, DateTimeOffset? issuedAt = null) =>
        AgentAccessToken.Issue(
            _secret,
            device ?? _deviceId,
            origin ?? Origin,
            issuedAt ?? DateTimeOffset.UtcNow);

    private async Task<HttpResponseMessage> GetStatusAsync(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, AgentEndpoints.StatusPath);
        if (token is not null)
            request.Headers.Add("Authorization", $"Bearer {token}");

        return await _client.SendAsync(request);
    }

    [Fact]
    public void It_listens_on_one_of_the_agreed_ports()
    {
        // The page walks the same list; a port outside it would be unreachable by design.
        Assert.Contains(_server.Port, AgentEndpoints.Ports);
    }

    [Fact]
    public async Task The_probe_says_an_agent_is_here_and_nothing_about_the_machine()
    {
        using var response = await _client.GetAsync(AgentEndpoints.HelloPath);
        response.EnsureSuccessStatusCode();

        var hello = await response.Content.ReadFromJsonAsync<AgentHello>();
        Assert.Equal(AgentEndpoints.Product, hello!.Product);
        Assert.Equal(AgentEndpoints.ProtocolVersion, hello.Protocol);
        Assert.Equal(_deviceId, hello.DeviceId);

        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Test PC", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.MachineName, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_secret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_valid_pass_is_accepted()
    {
        using var response = await GetStatusAsync(ValidToken());
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<AgentStatus>();
        Assert.Equal(_deviceId, status!.DeviceId);
        Assert.Equal("Test PC", status.DeviceName);
    }

    /// <summary>
    /// The agent discards its own old scans automatically, on its own database. Nothing else on the
    /// machine says so — the desktop's Settings page governs a different one — so a page asking for
    /// status has to be able to find out, or the deletion is invisible to everybody.
    /// </summary>
    [Fact]
    public async Task Status_says_how_many_scans_this_agent_keeps()
    {
        using var response = await GetStatusAsync(ValidToken());
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<AgentStatus>();

        Assert.True(status!.KeepRecentScans >= 1, "Keeping none would discard the scan just taken.");
        Assert.True(status.StoredScans >= 0);
    }

    [Fact]
    public async Task No_pass_at_all_is_refused()
    {
        using var response = await GetStatusAsync(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_pass_signed_with_another_secret_is_refused()
    {
        string foreign = AgentAccessToken.Issue(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            _deviceId,
            Origin,
            DateTimeOffset.UtcNow);

        using var response = await GetStatusAsync(foreign);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_pass_for_another_device_is_refused()
    {
        using var response = await GetStatusAsync(ValidToken(device: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_pass_minted_for_another_site_is_refused()
    {
        using var response = await GetStatusAsync(ValidToken(origin: "https://storava.evil"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_pass_is_refused()
    {
        using var response = await GetStatusAsync(ValidToken(issuedAt: DateTimeOffset.UtcNow.AddHours(-2)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Nonsense_in_the_authorization_header_is_refused_rather_than_crashing()
    {
        foreach (string header in new[] { "Bearer", "Bearer    ", "Basic abc", "storava1.a.b" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, AgentEndpoints.StatusPath);
            request.Headers.TryAddWithoutValidation("Authorization", header);

            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_refusal_never_echoes_the_secret_or_the_pass()
    {
        string token = ValidToken(device: Guid.NewGuid());

        using var response = await GetStatusAsync(token);
        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(_secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_paired_site_may_read_the_answer()
    {
        var allowed = new HttpRequestMessage(HttpMethod.Get, AgentEndpoints.HelloPath);
        allowed.Headers.Add("Origin", Origin);
        using var allowedResponse = await _client.SendAsync(allowed);

        Assert.Equal(
            Origin,
            Assert.Single(allowedResponse.Headers.GetValues("Access-Control-Allow-Origin")));

        var stranger = new HttpRequestMessage(HttpMethod.Get, AgentEndpoints.HelloPath);
        stranger.Headers.Add("Origin", "https://storava.evil");
        using var strangerResponse = await _client.SendAsync(stranger);

        // Without the header the browser discards the response, so another site learns nothing —
        // not even that an Agent answered.
        Assert.False(strangerResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_preflight_from_the_paired_site_permits_the_authorization_header()
    {
        var preflight = new HttpRequestMessage(HttpMethod.Options, AgentEndpoints.StatusPath);
        preflight.Headers.Add("Origin", Origin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");
        preflight.Headers.Add("Access-Control-Request-Private-Network", "true");

        using var response = await _client.SendAsync(preflight);

        Assert.Equal(Origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains(
            "authorization",
            string.Join(',', response.Headers.GetValues("Access-Control-Allow-Headers")),
            StringComparison.OrdinalIgnoreCase);

        // Answered for browsers still on the pre-Chrome-142 behaviour, where its absence would
        // fail the preflight outright.
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Private-Network")));
    }

    [Theory]
    [InlineData("https://storava.example/", "https://storava.example")]
    [InlineData("https://storava.example/app/", "https://storava.example")]
    [InlineData("http://localhost:5120/", "http://localhost:5120")]
    public void The_allowed_origin_is_reduced_to_scheme_and_authority(string baseUrl, string expected)
    {
        // A token is bound to an origin, so two spellings of one server must not look different.
        Assert.Equal(expected, AgentServer.OriginOf(baseUrl));
    }
}
