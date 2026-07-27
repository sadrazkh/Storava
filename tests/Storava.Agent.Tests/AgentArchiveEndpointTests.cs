using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Storava.Agent.Channel;
using Storava.Agent.Identity;
using Storava.Contracts.Agent;
using Storava.Contracts.Workspace;

namespace Storava.Agent.Tests;

/// <summary>
/// Downloading a walk the Agent ran as a portable <c>.storava</c> file.
/// <para>
/// Without this the Agent is a dead end: it can measure a machine no browser can reach, and then
/// nothing can leave the process. The archive is the format all three editions already agree on,
/// so this endpoint is what makes an agent scan ordinary data rather than a special case.
/// </para>
/// </summary>
[Collection(AgentServerCollection.Name)]
public sealed class AgentArchiveEndpointTests : IAsyncLifetime
{
    private const string Origin = "https://storava.example";

    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly string _secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _tree = Path.Combine(Path.GetTempPath(), "storava-archive-tree-" + Guid.NewGuid().ToString("N"));

    private readonly string _database = Path.Combine(
        Path.GetTempPath(),
        $"storava-archive-scans-{Guid.NewGuid():N}.db");

    private AgentServer _server = null!;
    private Task<int> _running = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_tree, "project", "node_modules"));
        await File.WriteAllBytesAsync(Path.Combine(_tree, "project", "node_modules", "big.bin"), new byte[256 * 1024]);
        await File.WriteAllBytesAsync(Path.Combine(_tree, "project", "app.ts"), new byte[2048]);

        _server = new AgentServer(
            new AgentRegistration
            {
                ServerBaseUrl = $"{Origin}/",
                DeviceId = _deviceId,
                DeviceName = "Archiving PC",
                ChannelSecret = _secret,
                PairedAtUtc = DateTimeOffset.UtcNow
            },
            "AAAA BBBB CCCC DDDD",
            _database);

        _running = _server.RunAsync(_shutdown.Token);

        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
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

    private async Task<AgentScanProgress> ScanAsync()
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
        return progress;
    }

    private static StoravaArchiveManifest ManifestOf(byte[] archive)
    {
        using var buffer = new MemoryStream(archive);
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);

        var entry = zip.GetEntry(StoravaArchiveEntries.Manifest);
        Assert.NotNull(entry);

        using var reader = entry!.Open();
        return JsonSerializer.Deserialize<StoravaArchiveManifest>(reader)!;
    }

    [Fact]
    public async Task A_finished_walk_comes_back_as_a_real_archive()
    {
        var progress = await ScanAsync();

        using var response = await _client.GetAsync(AgentScanPaths.Archive(progress.ScanId));
        response.EnsureSuccessStatusCode();

        byte[] archive = await response.Content.ReadAsByteArrayAsync();
        var manifest = ManifestOf(archive);

        Assert.Equal(StoravaArchiveManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal(_tree, manifest.RootPath);
        Assert.True(manifest.ItemCount > 0);

        // Every payload entry the readers expect, so this is importable rather than merely a zip.
        using var buffer = new MemoryStream(archive);
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry(StoravaArchiveEntries.Scan));
        Assert.NotNull(zip.GetEntry(StoravaArchiveEntries.Items));
    }

    /// <summary>
    /// The Agent runs the desktop application's entire archive stack. Left to that stack's own
    /// default it would sign its exports as desktop-written, and where a file came from is the one
    /// thing the manifest exists to say.
    /// </summary>
    [Fact]
    public async Task The_archive_says_the_agent_wrote_it()
    {
        var progress = await ScanAsync();

        using var response = await _client.GetAsync(AgentScanPaths.Archive(progress.ScanId));
        response.EnsureSuccessStatusCode();

        var manifest = ManifestOf(await response.Content.ReadAsByteArrayAsync());

        Assert.Equal(ArchiveProducer.Agent, manifest.ProducedBy);
        Assert.NotEqual(ArchiveProducer.Desktop, manifest.ProducedBy);
        Assert.Equal(ArchivePathKind.Absolute, manifest.PathKind);
    }

    /// <summary>
    /// The archive carries no credentials and no settings, and says so in the file itself — the
    /// point being that a user can hand one to someone else without auditing it first.
    /// </summary>
    [Fact]
    public async Task The_archive_carries_nothing_secret()
    {
        var progress = await ScanAsync();

        using var response = await _client.GetAsync(AgentScanPaths.Archive(progress.ScanId));
        var manifest = ManifestOf(await response.Content.ReadAsByteArrayAsync());

        Assert.False(manifest.ContainsSecrets);
        Assert.False(manifest.ContainsSettings);
    }

    /// <summary>
    /// The page reads the name off the response. Cross-origin that header is invisible unless it is
    /// exposed, and every download would land as an unnamed blob.
    /// </summary>
    [Fact]
    public async Task The_download_names_itself_and_the_page_may_read_the_name()
    {
        var progress = await ScanAsync();

        // Sent as the page sends it: cross-origin, so the reply carries the CORS headers that
        // decide what the page is allowed to read back. Expose-Headers rides the real response,
        // not the preflight.
        using var request = new HttpRequestMessage(HttpMethod.Get, AgentScanPaths.Archive(progress.ScanId));
        request.Headers.Add("Origin", Origin);
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string? fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;

        Assert.NotNull(fileName);
        Assert.EndsWith(StoravaArchiveEntries.Extension, fileName!.Trim('"'), StringComparison.Ordinal);

        Assert.Contains(
            "Content-Disposition",
            string.Join(",", response.Headers.TryGetValues("Access-Control-Expose-Headers", out var exposed)
                ? exposed
                : []),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A walk still running has folder rows that have not been totalled. Writing those into an
    /// archive would produce a file that reads cleanly and reports wrong sizes, which is worse than
    /// one that refuses.
    /// </summary>
    [Fact]
    public async Task An_unfinished_or_unknown_walk_has_no_archive()
    {
        using var unknown = await _client.GetAsync(AgentScanPaths.Archive("no-such-scan"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var started = await _client.PostAsJsonAsync(
            AgentScanPaths.Scans,
            new { rootPath = _tree, mode = "deep" });
        var progress = (await started.Content.ReadFromJsonAsync<AgentScanProgress>())!;

        if (progress.State == AgentScanState.Running)
        {
            using var early = await _client.GetAsync(AgentScanPaths.Archive(progress.ScanId));
            Assert.Equal(HttpStatusCode.NotFound, early.StatusCode);
        }

        for (int attempt = 0; attempt < 600 && progress.State == AgentScanState.Running; attempt++)
        {
            await Task.Delay(50);
            using var poll = await _client.GetAsync(AgentScanPaths.Scan(progress.ScanId));
            progress = (await poll.Content.ReadFromJsonAsync<AgentScanProgress>())!;
        }
    }

    [Fact]
    public async Task The_archive_needs_a_pass_like_everything_else()
    {
        using var anonymous = new HttpClient
        {
            BaseAddress = new Uri(AgentEndpoints.BaseAddress(_server.Port)),
            Timeout = TimeSpan.FromSeconds(10)
        };

        // The whole tree, with every real path on it — the most revealing thing the Agent can hand
        // out, and so the last endpoint that could be left open.
        using var response = await anonymous.GetAsync(AgentScanPaths.Archive("anything"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Writes the Agent-written archive the browser edition's tests read.
    /// <para>
    /// The desktop already contributes a fixture, but only this path produces a file stamped
    /// <c>Agent</c>, and only this path takes it over HTTP. A reader that quietly assumed the
    /// desktop was the only producer would pass every test on both sides and still refuse the one
    /// file a user of the Agent actually gets.
    /// </para>
    /// <para>
    /// Written only when missing or when <c>STORAVA_REFRESH_FIXTURES</c> is set. A test that
    /// rewrote a checked-in file every run would make a format change look like no change at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_shared_fixture_the_browser_reads_is_written_by_the_agent()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, "src")))
            repository = repository.Parent;

        Assert.NotNull(repository);

        string fixture = Path.Combine(
            repository!.FullName, "src", "Storava.Web", "ClientApp", "test", "fixtures", "agent-v2.storava");

        if (Environment.GetEnvironmentVariable("STORAVA_REFRESH_FIXTURES") is { Length: > 0 } || !File.Exists(fixture))
        {
            var progress = await ScanAsync();

            using var response = await _client.GetAsync(AgentScanPaths.Archive(progress.ScanId));
            response.EnsureSuccessStatusCode();

            Directory.CreateDirectory(Path.GetDirectoryName(fixture)!);
            await File.WriteAllBytesAsync(fixture, await response.Content.ReadAsByteArrayAsync());
        }

        var manifest = ManifestOf(await File.ReadAllBytesAsync(fixture));

        Assert.Equal(ArchiveProducer.Agent, manifest.ProducedBy);
        Assert.Equal(StoravaArchiveManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.True(manifest.ItemCount > 0);
    }

    /// <summary>
    /// The archive is built into a temporary file so a million-row walk is never held in memory.
    /// That file has to go away afterwards, including when a download is abandoned half-way —
    /// otherwise every export quietly leaves a copy of the user's folder tree in the temp folder.
    /// </summary>
    [Fact]
    public async Task The_temporary_copy_does_not_survive_the_download()
    {
        var progress = await ScanAsync();

        using var response = await _client.GetAsync(AgentScanPaths.Archive(progress.ScanId));
        response.EnsureSuccessStatusCode();
        await response.Content.ReadAsByteArrayAsync();

        // Given up to a moment, because the handle closes as the response completes rather than
        // before the client's last read returns.
        for (int attempt = 0; attempt < 100 && Leftovers().Length > 0; attempt++)
            await Task.Delay(50);

        Assert.Empty(Leftovers());

        static string[] Leftovers() => Directory.GetFiles(
            Path.GetTempPath(),
            $"storava-agent-*{StoravaArchiveEntries.Extension}");
    }
}
