using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Storava.Agent.Channel;
using Storava.Agent.Identity;
using Storava.Contracts.Agent;

namespace Storava.Agent.Tests;

/// <summary>
/// Acting on several folders under one approval, exercised against real folders.
/// <para>
/// Written around what must <em>not</em> happen. One approval covering twelve folders is a bigger
/// thing to get wrong than one covering a single folder, so the code that grants it has to be
/// impossible to spend on a set other than the one that was on screen — and a folder the rules
/// refuse has to stay refused however many others were approved alongside it.
/// </para>
/// </summary>
[Collection(AgentServerCollection.Name)]
public sealed class AgentPlanEndpointTests : IAsyncLifetime
{
    private const string Origin = "https://storava.example";

    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly string _secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _tree = Path.Combine(Path.GetTempPath(), "storava-plan-" + Guid.NewGuid().ToString("N"));
    private readonly string _database = Path.Combine(
        Path.GetTempPath(),
        $"storava-plan-scans-{Guid.NewGuid():N}.db");

    private AgentServer _server = null!;
    private Task<int> _running = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        // The same three shapes the single-item tests use, so a plan is judged by the same rules:
        // node_modules may be deleted but not relocated, a NuGet cache may be either, and an
        // ordinary folder neither.
        Directory.CreateDirectory(Path.Combine(_tree, "one", "node_modules", "pkg"));
        Directory.CreateDirectory(Path.Combine(_tree, "two", "node_modules", "pkg"));
        Directory.CreateDirectory(Path.Combine(_tree, "documents"));
        await File.WriteAllBytesAsync(Path.Combine(_tree, "one", "node_modules", "pkg", "a.bin"), new byte[48 * 1024]);
        await File.WriteAllBytesAsync(Path.Combine(_tree, "two", "node_modules", "pkg", "b.bin"), new byte[24 * 1024]);
        await File.WriteAllBytesAsync(Path.Combine(_tree, "documents", "notes.bin"), new byte[8 * 1024]);

        _server = new AgentServer(
            new AgentRegistration
            {
                ServerBaseUrl = $"{Origin}/",
                DeviceId = _deviceId,
                DeviceName = "Planning PC",
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

    [Fact]
    public async Task One_code_removes_every_folder_in_the_plan()
    {
        var (scanId, items) = await ScanAsync();
        var first = Find(items, "node_modules", inside: "one");
        var second = Find(items, "node_modules", inside: "two");

        var preview = await PreviewAsync(scanId,
            (first.Id, "delete", null),
            (second.Id, "delete", null));

        Assert.Equal(2, preview.RunnableCount);
        Assert.All(preview.Steps, step => Assert.True(step.CanRun));

        var outcome = await ExecuteAsync(preview, PlanCodeFor(preview));

        Assert.Equal(2, outcome.SucceededCount);
        Assert.Equal(0, outcome.FailedCount);
        Assert.False(Directory.Exists(first.Path));
        Assert.False(Directory.Exists(second.Path));
    }

    /// <summary>
    /// The gate itself. Anything other than the code shown on the plan leaves the disk untouched.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("ABCDEF")]
    [InlineData("node_modules")]
    public async Task Nothing_happens_without_the_code_the_plan_shows(string typed)
    {
        var (scanId, items) = await ScanAsync();
        var folder = Find(items, "node_modules", inside: "one");

        var preview = await PreviewAsync(scanId, (folder.Id, "delete", null));

        using var response = await _client.PostAsJsonAsync(AgentPlanPaths.Execute, new
        {
            planId = preview.PlanId,
            fingerprint = preview.Fingerprint,
            typedPhrase = typed,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(Directory.Exists(folder.Path));
    }

    /// <summary>
    /// An approval read against one plan cannot be spent on another. The fingerprint is what binds
    /// them, so a stale one is refused even when the code typed is genuinely the plan's own.
    /// </summary>
    [Fact]
    public async Task An_approval_cannot_be_spent_on_a_different_plan()
    {
        var (scanId, items) = await ScanAsync();
        var folder = Find(items, "node_modules", inside: "one");

        var preview = await PreviewAsync(scanId, (folder.Id, "delete", null));

        using var response = await _client.PostAsJsonAsync(AgentPlanPaths.Execute, new
        {
            planId = preview.PlanId,
            fingerprint = new string('0', 64),
            typedPhrase = PlanCodeFor(preview),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(Directory.Exists(folder.Path));
    }

    /// <summary>Approved once. A replayed approval finds nothing waiting for it.</summary>
    [Fact]
    public async Task An_approval_cannot_be_spent_twice()
    {
        var (scanId, items) = await ScanAsync();
        var folder = Find(items, "node_modules", inside: "one");

        var preview = await PreviewAsync(scanId, (folder.Id, "delete", null));
        var code = PlanCodeFor(preview);

        var first = await ExecuteAsync(preview, code);
        Assert.Equal(1, first.SucceededCount);

        using var replay = await _client.PostAsJsonAsync(AgentPlanPaths.Execute, new
        {
            planId = preview.PlanId,
            fingerprint = preview.Fingerprint,
            typedPhrase = code,
        });

        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
    }

    /// <summary>
    /// A folder the rules refuse stays refused, and it is listed with its reason rather than
    /// dropped: a folder that quietly vanishes from the plan is worse than one shown as refused.
    /// </summary>
    [Fact]
    public async Task A_folder_the_rules_refuse_is_listed_but_never_run()
    {
        var (scanId, items) = await ScanAsync();
        var allowed = Find(items, "node_modules", inside: "one");
        var ordinary = Find(items, "documents");

        var preview = await PreviewAsync(scanId,
            (allowed.Id, "delete", null),
            (ordinary.Id, "delete", null));

        Assert.Equal(2, preview.Steps.Count);
        Assert.Equal(1, preview.RunnableCount);

        var refused = Assert.Single(preview.Steps, step => !step.CanRun);
        Assert.Equal("not_permitted", refused.RefusedReason);

        var outcome = await ExecuteAsync(preview, PlanCodeFor(preview));

        Assert.Equal(1, outcome.SucceededCount);
        Assert.True(Directory.Exists(ordinary.Path));
        Assert.False(Directory.Exists(allowed.Path));

        // The refused folder is on the run as skipped, not absent from it. The totals have to
        // account for every folder the user chose, and only the one that was allowed is attempted.
        Assert.Equal(1, outcome.SkippedCount);
        Assert.Single(outcome.Steps);
        Assert.DoesNotContain(outcome.Steps, step => step.SourcePath == ordinary.Path);
    }

    /// <summary>
    /// The measured total is what the plan claims it would free, and it has to be the disk's own
    /// figure rather than the scan's — the panel shows it right above the approval.
    /// </summary>
    [Fact]
    public async Task The_plan_reports_what_it_would_free()
    {
        var (scanId, items) = await ScanAsync();
        var first = Find(items, "node_modules", inside: "one");
        var second = Find(items, "node_modules", inside: "two");

        var preview = await PreviewAsync(scanId,
            (first.Id, "delete", null),
            (second.Id, "delete", null));

        Assert.Equal((48 + 24) * 1024, preview.TotalBytes);
    }

    [Fact]
    public async Task The_same_folder_cannot_appear_twice()
    {
        var (scanId, items) = await ScanAsync();
        var folder = Find(items, "node_modules", inside: "one");

        using var response = await _client.PostAsJsonAsync(AgentPlanPaths.Preview, new
        {
            scanId,
            items = new[]
            {
                new { itemId = folder.Id, action = "delete", destinationPath = (string?)null, moveMethod = (string?)null },
                new { itemId = folder.Id, action = "delete", destinationPath = (string?)null, moveMethod = (string?)null },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<AgentProblem>())!;
        Assert.Equal("duplicate_item", problem.Reason);
    }

    [Fact]
    public async Task An_empty_plan_is_refused()
    {
        var (scanId, _) = await ScanAsync();

        using var response = await _client.PostAsJsonAsync(AgentPlanPaths.Preview, new
        {
            scanId,
            items = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A plan of only refused folders offers nothing to approve, so it is not offered.</summary>
    [Fact]
    public async Task A_plan_with_nothing_runnable_is_refused()
    {
        var (scanId, items) = await ScanAsync();
        var ordinary = Find(items, "documents");

        using var response = await _client.PostAsJsonAsync(AgentPlanPaths.Preview, new
        {
            scanId,
            items = new[]
            {
                new { itemId = ordinary.Id, action = "delete", destinationPath = (string?)null, moveMethod = (string?)null },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<AgentProblem>())!;
        Assert.Equal("nothing_runnable", problem.Reason);
    }

    // --- helpers -----------------------------------------------------------------------------

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

    private static AgentScanItem Find(IReadOnlyList<AgentScanItem> items, string name, string? inside = null) =>
        Assert.Single(items, item =>
            item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && (inside is null || item.Path.Contains($"{Path.DirectorySeparatorChar}{inside}{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)));

    private async Task<AgentPlanPreview> PreviewAsync(
        string scanId,
        params (string ItemId, string Action, string? Destination)[] wanted)
    {
        using var response = await _client.PostAsJsonAsync(AgentPlanPaths.Preview, new
        {
            scanId,
            items = wanted.Select(item => new
            {
                itemId = item.ItemId,
                action = item.Action,
                destinationPath = item.Destination,
                moveMethod = (string?)null,
            }).ToArray(),
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentPlanPreview>())!;
    }

    private async Task<AgentPlanOutcome> ExecuteAsync(AgentPlanPreview preview, string code)
    {
        using var response = await _client.PostAsJsonAsync(AgentPlanPaths.Execute, new
        {
            planId = preview.PlanId,
            fingerprint = preview.Fingerprint,
            typedPhrase = code,
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentPlanOutcome>())!;
    }

    /// <summary>
    /// The code as the page would read it: straight off the preview. Recomputing it here instead
    /// would test the algorithm against itself rather than testing that the panel is shown the
    /// code that actually works.
    /// </summary>
    private static string PlanCodeFor(AgentPlanPreview preview) => preview.ConfirmationPhrase;
}
