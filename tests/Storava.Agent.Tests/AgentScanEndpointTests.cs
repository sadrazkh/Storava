using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Storava.Agent.Channel;
using Storava.Agent.Identity;
using Storava.Contracts.Agent;

namespace Storava.Agent.Tests;

/// <summary>
/// The endpoints that actually read the machine, exercised against a real tree over real HTTP.
/// <para>
/// This is the first point where something outside the Agent names a path, so the tests are as
/// much about what the Agent refuses to be told as about what it returns.
/// </para>
/// </summary>
[Collection(AgentServerCollection.Name)]
public sealed class AgentScanEndpointTests : IAsyncLifetime
{
    private const string Origin = "https://storava.example";

    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly string _secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _tree = Path.Combine(Path.GetTempPath(), "storava-agent-tree-" + Guid.NewGuid().ToString("N"));

    /// <summary>Its own database per test, so nothing here touches the real agent's scans.</summary>
    private readonly string _database = Path.Combine(
        Path.GetTempPath(),
        $"storava-agent-scans-{Guid.NewGuid():N}.db");

    private AgentServer _server = null!;
    private Task<int> _running = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_tree, "project", "node_modules"));
        Directory.CreateDirectory(Path.Combine(_tree, "media"));
        await File.WriteAllBytesAsync(Path.Combine(_tree, "project", "node_modules", "big.bin"), new byte[512 * 1024]);
        await File.WriteAllBytesAsync(Path.Combine(_tree, "project", "app.ts"), new byte[2048]);
        await File.WriteAllBytesAsync(Path.Combine(_tree, "media", "clip.bin"), new byte[4096]);

        _server = new AgentServer(
            new AgentRegistration
            {
                ServerBaseUrl = $"{Origin}/",
                DeviceId = _deviceId,
                DeviceName = "Scanning PC",
                ChannelSecret = _secret,
                PairedAtUtc = DateTimeOffset.UtcNow
            },
            "AAAA BBBB CCCC DDDD",
            _database);

        _running = _server.RunAsync(_shutdown.Token);

        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        for (int attempt = 0; attempt < 200 && _server.Port == 0; attempt++)
            await Task.Delay(20);

        _client.BaseAddress = new Uri(AgentEndpoints.BaseAddress(_server.Port));
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Pass()}");

        for (int attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                using var probe = await _client.GetAsync(AgentEndpoints.HelloPath);
                if (probe.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
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
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_tree))
                Directory.Delete(_tree, recursive: true);
        }
        catch (IOException)
        {
        }

        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                if (File.Exists(_database + suffix))
                    File.Delete(_database + suffix);
            }
            catch (IOException)
            {
            }
        }
    }

    private string Pass() =>
        AgentAccessToken.Issue(_secret, _deviceId, Origin, DateTimeOffset.UtcNow);

    /// <summary>Starts a walk and polls until it stops, so a test reads as one action.</summary>
    private async Task<AgentScanProgress> ScanAsync(string rootPath, string mode = "quick")
    {
        using var started = await _client.PostAsJsonAsync(
            AgentScanPaths.Scans,
            new { rootPath, mode });
        started.EnsureSuccessStatusCode();

        return await WaitAsync((await started.Content.ReadFromJsonAsync<AgentScanProgress>())!);
    }

    /// <summary>
    /// Polls one walk to a stop. Separate from starting because the Agent allows only one at a
    /// time: a test that has already started one has to wait for it, not ask for another.
    /// </summary>
    private async Task<AgentScanProgress> WaitAsync(AgentScanProgress progress)
    {
        for (int attempt = 0; attempt < 600 && progress.State == AgentScanState.Running; attempt++)
        {
            await Task.Delay(50);
            using var poll = await _client.GetAsync(AgentScanPaths.Scan(progress.ScanId));
            poll.EnsureSuccessStatusCode();
            progress = (await poll.Content.ReadFromJsonAsync<AgentScanProgress>())!;
        }

        return progress;
    }

    [Fact]
    public async Task It_lists_the_machines_real_drives()
    {
        using var response = await _client.GetAsync(AgentScanPaths.Drives);
        response.EnsureSuccessStatusCode();

        var drives = await response.Content.ReadFromJsonAsync<List<AgentDrive>>();

        Assert.NotEmpty(drives!);
        // A browser cannot enumerate drives at all; this is the first thing the Agent adds.
        Assert.Contains(drives!, drive => drive.IsReady && drive.TotalBytes > 0);
        Assert.All(drives!, drive => Assert.False(string.IsNullOrWhiteSpace(drive.Name)));
    }

    [Fact]
    public async Task A_walk_reports_what_it_found()
    {
        var progress = await ScanAsync(_tree);

        Assert.Equal(AgentScanState.Completed, progress.State);
        Assert.Equal(3, progress.Files);
        Assert.Equal(4, progress.Folders); // root + project + node_modules + media
        Assert.Equal(512 * 1024 + 2048 + 4096, progress.Bytes);
        Assert.Equal(0, progress.Errors);
        Assert.Null(progress.Error);
    }

    [Fact]
    public async Task The_results_carry_operating_system_paths()
    {
        var progress = await ScanAsync(_tree);

        using var response = await _client.GetAsync($"{AgentScanPaths.Items(progress.ScanId)}?limit=50");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<AgentScanItems>();
        var biggest = page!.Items.First();

        // This is the point of the whole phase: a real absolute path, which the browser edition
        // can never produce on its own.
        Assert.True(Path.IsPathFullyQualified(biggest.Path));
        Assert.StartsWith(_tree, biggest.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(page.Items, item => item.Path.EndsWith("big.bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_desktop_rule_catalog_classifies_what_the_agent_walks()
    {
        var progress = await ScanAsync(_tree);

        using var response = await _client.GetAsync($"{AgentScanPaths.Items(progress.ScanId)}?limit=100");
        var page = await response.Content.ReadFromJsonAsync<AgentScanItems>();

        // node_modules is a known package cache; the Agent gets that for free by reusing the
        // desktop rule engine rather than carrying a second copy of it.
        var cache = Assert.Single(page!.Items, item =>
            item.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(cache.RuleId);
        Assert.NotEqual("Unknown", cache.Category);
    }

    [Fact]
    public async Task Results_are_only_offered_once_the_walk_has_finished()
    {
        using var started = await _client.PostAsJsonAsync(
            AgentScanPaths.Scans,
            new { rootPath = _tree, mode = "quick" });
        var progress = (await started.Content.ReadFromJsonAsync<AgentScanProgress>())!;

        if (progress.State == AgentScanState.Running)
        {
            // A partial tree has folder rows that have not been totalled yet; reporting those as
            // sizes would be wrong rather than merely incomplete.
            using var early = await _client.GetAsync(AgentScanPaths.Items(progress.ScanId));
            Assert.Equal(HttpStatusCode.NotFound, early.StatusCode);
        }

        var finished = await WaitAsync(progress);
        Assert.Equal(AgentScanState.Completed, finished.State);

        using var afterwards = await _client.GetAsync(AgentScanPaths.Items(finished.ScanId));
        afterwards.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_second_walk_is_refused_while_one_is_running()
    {
        using var first = await _client.PostAsJsonAsync(
            AgentScanPaths.Scans,
            new { rootPath = _tree, mode = "deep" });
        first.EnsureSuccessStatusCode();
        var running = (await first.Content.ReadFromJsonAsync<AgentScanProgress>())!;

        using var second = await _client.PostAsJsonAsync(
            AgentScanPaths.Scans,
            new { rootPath = _tree, mode = "quick" });

        // Only meaningful while the first is genuinely still going; a tree this small can finish
        // before the second request lands, and that is not a failure.
        if (second.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await second.Content.ReadFromJsonAsync<AgentProblem>();
            Assert.Equal("already_scanning", problem!.Reason);
        }
        else
        {
            second.EnsureSuccessStatusCode();
        }

        await WaitAsync(running);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-absolute-path")]
    [InlineData("relative\\folder")]
    public async Task A_path_that_is_not_a_full_path_is_refused(string rootPath)
    {
        using var response = await _client.PostAsJsonAsync(
            AgentScanPaths.Scans,
            new { rootPath, mode = "quick" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<AgentProblem>();
        Assert.Contains(problem!.Reason, new[] { "no_path", "bad_path", "not_found" });
    }

    [Fact]
    public async Task A_folder_that_does_not_exist_is_refused()
    {
        string missing = Path.Combine(Path.GetTempPath(), "storava-absent-" + Guid.NewGuid().ToString("N"));

        using var response = await _client.PostAsJsonAsync(
            AgentScanPaths.Scans,
            new { rootPath = missing, mode = "quick" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("not_found", (await response.Content.ReadFromJsonAsync<AgentProblem>())!.Reason);
    }

    [Fact]
    public async Task An_unknown_scan_is_not_reported_on()
    {
        using var progress = await _client.GetAsync(AgentScanPaths.Scan("no-such-scan"));
        using var items = await _client.GetAsync(AgentScanPaths.Items("no-such-scan"));

        Assert.Equal(HttpStatusCode.NotFound, progress.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, items.StatusCode);
    }

    [Fact]
    public async Task A_walk_can_be_cancelled()
    {
        using var started = await _client.PostAsJsonAsync(
            AgentScanPaths.Scans,
            new { rootPath = _tree, mode = "deep" });
        var progress = (await started.Content.ReadFromJsonAsync<AgentScanProgress>())!;

        using var cancelled = await _client.PostAsync(AgentScanPaths.Cancel(progress.ScanId), content: null);

        // A walk that already finished cannot be cancelled, and says so rather than pretending.
        Assert.Contains(cancelled.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });

        for (int attempt = 0; attempt < 200 && progress.State == AgentScanState.Running; attempt++)
        {
            await Task.Delay(50);
            using var poll = await _client.GetAsync(AgentScanPaths.Scan(progress.ScanId));
            progress = (await poll.Content.ReadFromJsonAsync<AgentScanProgress>())!;
        }

        Assert.NotEqual(AgentScanState.Running, progress.State);
    }

    [Fact]
    public async Task The_page_size_cannot_be_pushed_past_the_ceiling()
    {
        var progress = await ScanAsync(_tree);

        using var response = await _client.GetAsync($"{AgentScanPaths.Items(progress.ScanId)}?limit=100000");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<AgentScanItems>();
        Assert.True(page!.Items.Count <= Storava.Agent.Scanning.AgentScanService.MaximumItems);
    }

    /// <summary>
    /// The page matches the state against string literals. Serialized as integers — which is the
    /// framework default — every comparison silently fails and a finished walk looks like one that
    /// never ends. Asserted on the raw body because deserializing into the enum accepts both.
    /// </summary>
    [Fact]
    public async Task The_scan_state_goes_out_as_a_name_not_a_number()
    {
        var progress = await ScanAsync(_tree);

        using var response = await _client.GetAsync(AgentScanPaths.Scan(progress.ScanId));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"state\":\"Completed\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"state\":1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_scanning_endpoint_needs_a_pass()
    {
        using var anonymous = new HttpClient
        {
            BaseAddress = new Uri(AgentEndpoints.BaseAddress(_server.Port)),
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var drives = await anonymous.GetAsync(AgentScanPaths.Drives);
        using var start = await anonymous.PostAsJsonAsync(AgentScanPaths.Scans, new { rootPath = _tree });
        using var poll = await anonymous.GetAsync(AgentScanPaths.Scan("anything"));
        using var items = await anonymous.GetAsync(AgentScanPaths.Items("anything"));
        using var cancel = await anonymous.PostAsync(AgentScanPaths.Cancel("anything"), content: null);

        // Reading the machine is exactly what a pass is for; none of this is probe-visible.
        foreach (var response in new[] { drives, start, poll, items, cancel })
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
