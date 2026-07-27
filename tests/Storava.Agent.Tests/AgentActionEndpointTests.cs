using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Storava.Agent.Channel;
using Storava.Agent.Identity;
using Storava.Contracts.Agent;

namespace Storava.Agent.Tests;

/// <summary>
/// The only endpoints that change the disk, exercised against real folders.
/// <para>
/// These tests are written around what must <em>not</em> happen. A page can point at something the
/// Agent measured; it cannot name a path, cannot act without the user typing the folder's name,
/// cannot spend an approval on a different step than the one it was granted for, and cannot make
/// anything disappear permanently — removal is always the Recycle Bin.
/// </para>
/// </summary>
[Collection(AgentServerCollection.Name)]
public sealed class AgentActionEndpointTests : IAsyncLifetime
{
    private const string Origin = "https://storava.example";

    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly string _secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _tree = Path.Combine(Path.GetTempPath(), "storava-act-" + Guid.NewGuid().ToString("N"));
    private readonly string _database = Path.Combine(
        Path.GetTempPath(),
        $"storava-act-scans-{Guid.NewGuid():N}.db");

    private AgentServer _server = null!;
    private Task<int> _running = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        // Three shapes the rule catalog judges differently, so the tests exercise a real
        // distinction rather than a fabricated one: node_modules may be deleted but not relocated,
        // a NuGet package cache may be either, and an ordinary folder neither.
        Directory.CreateDirectory(Path.Combine(_tree, "project", "node_modules", "pkg"));
        Directory.CreateDirectory(Path.Combine(_tree, ".nuget", "packages", "somepkg"));
        Directory.CreateDirectory(Path.Combine(_tree, "documents"));
        await File.WriteAllBytesAsync(Path.Combine(_tree, "project", "node_modules", "pkg", "lib.bin"), new byte[64 * 1024]);
        await File.WriteAllBytesAsync(Path.Combine(_tree, ".nuget", "packages", "somepkg", "pkg.bin"), new byte[32 * 1024]);
        await File.WriteAllBytesAsync(Path.Combine(_tree, "documents", "notes.bin"), new byte[8 * 1024]);

        _server = new AgentServer(
            new AgentRegistration
            {
                ServerBaseUrl = $"{Origin}/",
                DeviceId = _deviceId,
                DeviceName = "Acting PC",
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
        _client.DefaultRequestHeaders.Add(
            "Authorization",
            $"Bearer {AgentAccessToken.Issue(_secret, _deviceId, Origin, DateTimeOffset.UtcNow)}");

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

        foreach (string path in new[] { _tree })
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
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

    private async Task<(string ScanId, IReadOnlyList<AgentScanItem> Items)> ScanAsync()
    {
        using var started = await _client.PostAsJsonAsync(
            AgentScanPaths.Scans,
            new { rootPath = _tree, mode = "quick" });
        started.EnsureSuccessStatusCode();

        var progress = (await started.Content.ReadFromJsonAsync<AgentScanProgress>())!;
        for (int attempt = 0; attempt < 600 && progress.State == AgentScanState.Running; attempt++)
        {
            await Task.Delay(50);
            using var poll = await _client.GetAsync(AgentScanPaths.Scan(progress.ScanId));
            progress = (await poll.Content.ReadFromJsonAsync<AgentScanProgress>())!;
        }

        Assert.Equal(AgentScanState.Completed, progress.State);

        using var items = await _client.GetAsync($"{AgentScanPaths.Items(progress.ScanId)}?limit=500");
        var page = (await items.Content.ReadFromJsonAsync<AgentScanItems>())!;
        return (progress.ScanId, page.Items);
    }

    private static AgentScanItem Find(IReadOnlyList<AgentScanItem> items, string name) =>
        Assert.Single(items, item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private async Task<HttpResponseMessage> PreviewAsync(
        string scanId, string itemId, string action, string? destination = null) =>
        await _client.PostAsJsonAsync(AgentActionPaths.Preview, new
        {
            scanId,
            itemId,
            action,
            destinationPath = destination
        });

    private async Task<HttpResponseMessage> ExecuteAsync(string stepId, string fingerprint, string typedName) =>
        await _client.PostAsJsonAsync(AgentActionPaths.Execute, new
        {
            stepId,
            fingerprint,
            typedName
        });

    [Fact]
    public async Task A_delete_sends_the_folder_to_the_recycle_bin_after_the_name_is_typed()
    {
        var (scanId, items) = await ScanAsync();
        var cache = Find(items, "node_modules");
        Assert.True(cache.CanDelete);

        using var previewed = await PreviewAsync(scanId, cache.Id, "delete");
        previewed.EnsureSuccessStatusCode();
        var preview = (await previewed.Content.ReadFromJsonAsync<AgentActionPreview>())!;

        // Measured now rather than taken from the scan, and the phrase is the folder's own name.
        Assert.Equal("node_modules", preview.ConfirmationPhrase);
        Assert.True(preview.MeasuredBytes > 0);

        using var executed = await ExecuteAsync(preview.StepId, preview.Fingerprint, "node_modules");
        executed.EnsureSuccessStatusCode();
        var outcome = (await executed.Content.ReadFromJsonAsync<AgentActionOutcome>())!;

        Assert.True(outcome.Succeeded);
        Assert.Equal("Completed", outcome.Status);
        Assert.True(outcome.BytesFreed > 0);
        Assert.False(Directory.Exists(cache.Path));

        // Recorded so the user can find it again — removal means the Recycle Bin, not oblivion.
        Assert.Equal(cache.Path, outcome.RecycledPath);
    }

    [Fact]
    public async Task Nothing_happens_without_the_folders_own_name()
    {
        var (scanId, items) = await ScanAsync();
        var cache = Find(items, "node_modules");

        using var previewed = await PreviewAsync(scanId, cache.Id, "delete");
        var preview = (await previewed.Content.ReadFromJsonAsync<AgentActionPreview>())!;

        foreach (string typed in new[] { "", "yes", "NODE_MODULE", "node_modules " + "x" })
        {
            using var attempt = await ExecuteAsync(preview.StepId, preview.Fingerprint, typed);
            var outcome = await attempt.Content.ReadFromJsonAsync<AgentActionOutcome>();

            Assert.False(outcome!.Succeeded);
            Assert.Equal("exec.not_confirmed", outcome.ErrorCode);
        }

        // Untouched, and still offerable: a refusal is not a failure.
        Assert.True(Directory.Exists(cache.Path));
    }

    [Fact]
    public async Task An_approval_cannot_be_spent_on_a_different_step()
    {
        var (scanId, items) = await ScanAsync();
        var cache = Find(items, "node_modules");

        using var previewed = await PreviewAsync(scanId, cache.Id, "delete");
        var preview = (await previewed.Content.ReadFromJsonAsync<AgentActionPreview>())!;

        using var forged = await ExecuteAsync(preview.StepId, "0000000000000000", "node_modules");
        var outcome = await forged.Content.ReadFromJsonAsync<AgentActionOutcome>();

        Assert.False(outcome!.Succeeded);
        Assert.Equal("exec.confirmation_stale", outcome.ErrorCode);
        Assert.True(Directory.Exists(cache.Path));
    }

    [Fact]
    public async Task An_approval_is_spent_once()
    {
        var (scanId, items) = await ScanAsync();
        var cache = Find(items, "node_modules");

        using var previewed = await PreviewAsync(scanId, cache.Id, "delete");
        var preview = (await previewed.Content.ReadFromJsonAsync<AgentActionPreview>())!;

        using var first = await ExecuteAsync(preview.StepId, preview.Fingerprint, "node_modules");
        first.EnsureSuccessStatusCode();

        using var again = await ExecuteAsync(preview.StepId, preview.Fingerprint, "node_modules");

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task The_rule_catalog_decides_what_may_be_acted_on()
    {
        var (scanId, items) = await ScanAsync();
        var ordinary = Find(items, "documents");

        // Not a recognised cache, so the local rules permit nothing — and the Agent may not widen
        // them just because a page asked.
        Assert.False(ordinary.CanDelete);

        using var refused = await PreviewAsync(scanId, ordinary.Id, "delete");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("not_permitted", (await refused.Content.ReadFromJsonAsync<AgentProblem>())!.Reason);
        Assert.True(Directory.Exists(ordinary.Path));
    }

    [Fact]
    public async Task The_agent_will_not_act_on_something_it_did_not_measure()
    {
        var (scanId, items) = await ScanAsync();

        using var unknownItem = await PreviewAsync(scanId, "not-an-item", "delete");
        using var unknownScan = await PreviewAsync("not-a-scan", items[0].Id, "delete");

        // A page can point at what the Agent found; it cannot name a path of its own.
        Assert.Equal(HttpStatusCode.BadRequest, unknownItem.StatusCode);
        Assert.Equal("unknown_item", (await unknownItem.Content.ReadFromJsonAsync<AgentProblem>())!.Reason);
        Assert.Equal(HttpStatusCode.BadRequest, unknownScan.StatusCode);
        Assert.Equal("unknown_scan", (await unknownScan.Content.ReadFromJsonAsync<AgentProblem>())!.Reason);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("rename")]
    [InlineData("")]
    public async Task Only_move_and_delete_are_actions(string action)
    {
        var (scanId, items) = await ScanAsync();

        using var refused = await PreviewAsync(scanId, Find(items, "node_modules").Id, action);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("bad_action", (await refused.Content.ReadFromJsonAsync<AgentProblem>())!.Reason);
    }

    [Fact]
    public async Task A_cache_the_rules_will_not_relocate_cannot_be_moved()
    {
        var (scanId, items) = await ScanAsync();
        var cache = Find(items, "node_modules");

        // Deletable but not movable: npm restores it, so relocating it is not something the
        // catalog offers. The Agent honours that distinction rather than flattening it.
        Assert.True(cache.CanDelete);
        Assert.False(cache.CanMove);

        using var refused = await PreviewAsync(scanId, cache.Id, "move", Path.Combine(_tree, "elsewhere"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("not_permitted", (await refused.Content.ReadFromJsonAsync<AgentProblem>())!.Reason);
    }

    [Fact]
    public async Task A_move_to_the_same_drive_is_refused_because_it_frees_nothing()
    {
        var (scanId, items) = await ScanAsync();
        var packages = Find(items, "packages");
        Assert.True(packages.CanMove);

        using var refused = await PreviewAsync(scanId, packages.Id, "move", Path.Combine(_tree, "moved-here"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(
            "exec.destination_same_volume",
            (await refused.Content.ReadFromJsonAsync<AgentProblem>())!.Reason);
    }

    [Fact]
    public async Task A_move_with_no_destination_is_refused()
    {
        var (scanId, items) = await ScanAsync();

        using var refused = await PreviewAsync(scanId, Find(items, "packages").Id, "move");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(
            "exec.destination_required",
            (await refused.Content.ReadFromJsonAsync<AgentProblem>())!.Reason);
    }

    [Fact]
    public async Task A_move_destination_inside_the_folder_being_moved_is_refused()
    {
        var (scanId, items) = await ScanAsync();
        var packages = Find(items, "packages");

        string inside = Path.Combine(packages.Path, "nested");
        using var refused = await PreviewAsync(scanId, packages.Id, "move", inside);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(
            "exec.destination_inside_source",
            (await refused.Content.ReadFromJsonAsync<AgentProblem>())!.Reason);
    }

    [Fact]
    public async Task A_folder_that_vanished_after_the_preview_is_not_acted_on()
    {
        var (scanId, items) = await ScanAsync();
        var cache = Find(items, "node_modules");

        using var previewed = await PreviewAsync(scanId, cache.Id, "delete");
        var preview = (await previewed.Content.ReadFromJsonAsync<AgentActionPreview>())!;

        // Between reading and confirming, the world moved on.
        Directory.Delete(cache.Path, recursive: true);

        using var executed = await ExecuteAsync(preview.StepId, preview.Fingerprint, "node_modules");
        var outcome = await executed.Content.ReadFromJsonAsync<AgentActionOutcome>();

        Assert.False(outcome!.Succeeded);
        Assert.Equal(0, outcome.BytesFreed);
    }

    [Fact]
    public async Task Both_acting_endpoints_need_a_pass()
    {
        using var anonymous = new HttpClient
        {
            BaseAddress = new Uri(AgentEndpoints.BaseAddress(_server.Port)),
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var preview = await anonymous.PostAsJsonAsync(
            AgentActionPaths.Preview,
            new { scanId = "s", itemId = "i", action = "delete" });
        using var execute = await anonymous.PostAsJsonAsync(
            AgentActionPaths.Execute,
            new { stepId = "s", fingerprint = "f", typedName = "n" });

        Assert.Equal(HttpStatusCode.Unauthorized, preview.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, execute.StatusCode);
    }

    [Fact]
    public async Task A_preview_changes_nothing_on_disk()
    {
        var (scanId, items) = await ScanAsync();
        var cache = Find(items, "node_modules");

        string[] before = Directory.GetFileSystemEntries(_tree, "*", SearchOption.AllDirectories);

        using var previewed = await PreviewAsync(scanId, cache.Id, "delete");
        previewed.EnsureSuccessStatusCode();

        // The dry run is exactly that: it measures and reports, and touches nothing.
        Assert.Equal(before, Directory.GetFileSystemEntries(_tree, "*", SearchOption.AllDirectories));
    }
}
