using System.IO.Compression;
using System.Text;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Application.Services;
using Storava.Contracts.Workspace;
using Storava.Domain.Enums;
using Storava.Rules;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// Covers the portable <c>.storava</c> archive: a scan can be written out, moved elsewhere and
/// read back, and a file that has been tampered with is refused.
/// </summary>
public sealed class WorkspaceArchiveTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "storava-archives-" + Guid.NewGuid().ToString("N"));

    public WorkspaceArchiveTests() => Directory.CreateDirectory(_directory);

    private string ArchivePath(string name = "scan") =>
        Path.Combine(_directory, name + StoravaArchiveEntries.Extension);

    private static async Task<ScanResult> ScanAsync(TestHost host, string root)
    {
        var coordinator = host.Get<ScanCoordinator>();
        return await coordinator.RunAsync(
            new ScanRequest { RootPath = root },
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            CancellationToken.None);
    }

    /// <summary>A scanned tree with a recognisable cache, plus its analysis.</summary>
    private static async Task<ScanResult> ScanAndAnalyzeAsync(TestHost host, TestTree tree)
    {
        tree.AddFile(@"proj\node_modules\big.bin", 60 * 1024 * 1024);
        tree.AddFile(@"proj\src\app.ts", 2048);
        tree.AddFile(@"media\clip.mp4", 4096);

        var result = await ScanAsync(host, tree.Root);
        await host.Get<AnalysisService>().AnalyzeAsync(result.SessionId, "en");
        return result;
    }

    [Fact]
    public async Task Export_ThenImport_RestoresTheScan()
    {
        using var tree = new TestTree();
        string path = ArchivePath();
        string sessionId;
        int originalItemCount;
        long originalSize;

        using (var host = new TestHost(withRules: true))
        {
            var scan = await ScanAndAnalyzeAsync(host, tree);
            sessionId = scan.SessionId;
            originalSize = scan.TotalSize;
            originalItemCount = (await host.Get<IScanQueryService>()
                .GetLargestAsync(sessionId, 10_000, foldersOnly: false)).Count;

            var export = await host.Get<IWorkspaceArchiveService>().ExportAsync(sessionId, path, "en-US");

            Assert.True(export.IsSuccess);
            Assert.True(File.Exists(path));
            Assert.Equal(originalItemCount, export.Value.ItemCount);
        }

        // A completely separate database, as if the file had been moved to another machine.
        using (var host = new TestHost(withRules: true))
        {
            var import = await host.Get<IWorkspaceArchiveService>().ImportAsync(path);

            Assert.True(import.IsSuccess);
            Assert.Equal(sessionId, import.Value.SessionId);
            Assert.Equal(originalItemCount, import.Value.ItemCount);

            var session = await host.Get<IScanSessionRepository>().GetAsync(sessionId);
            Assert.NotNull(session);
            Assert.Equal(originalSize, session!.TotalSize);
            Assert.Equal(ScanOrigin.Imported, session.Origin);
            Assert.NotNull(session.ImportedAt);
        }
    }

    [Fact]
    public async Task ImportedScan_IsFullyBrowsable()
    {
        using var tree = new TestTree();
        string path = ArchivePath();

        using (var host = new TestHost(withRules: true))
        {
            var scan = await ScanAndAnalyzeAsync(host, tree);
            await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");
        }

        using (var host = new TestHost(withRules: true))
        {
            var import = await host.Get<IWorkspaceArchiveService>().ImportAsync(path);
            string sessionId = import.Value.SessionId;
            var query = host.Get<IScanQueryService>();

            // The tree structure survives, so Explorer can walk it.
            var roots = await query.GetRootsAsync(sessionId);
            var root = Assert.Single(roots);
            var children = await query.GetChildrenAsync(sessionId, root.Id);
            Assert.NotEmpty(children);

            // Classification survives, so Analysis and Recommendations still work.
            var nodeModules = await query.SearchAsync(sessionId, "node_modules", 10);
            Assert.Equal("npm.node-modules", Assert.Single(nodeModules).KnownRuleId);

            var categories = await query.GetCategoryUsageAsync(sessionId);
            Assert.Contains(categories, c => c.Category == StorageCategory.PackageCaches);
        }
    }

    [Fact]
    public async Task ImportedRecommendations_AreStillOnlyAdvice()
    {
        using var tree = new TestTree();
        string path = ArchivePath();

        using (var host = new TestHost(withRules: true))
        {
            var scan = await ScanAndAnalyzeAsync(host, tree);
            await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");
        }

        using (var host = new TestHost(withRules: true))
        {
            var import = await host.Get<IWorkspaceArchiveService>().ImportAsync(path);
            var stored = await host.Get<IRecommendationRepository>().GetBySessionAsync(import.Value.SessionId);

            Assert.NotEmpty(stored);
            Assert.All(stored, r => Assert.Equal(SuggestedAction.NoAction, r.SuggestedAction));
        }
    }

    [Fact]
    public async Task Archive_ContainsNoSettingsAndNoApiKey()
    {
        using var tree = new TestTree();
        string path = ArchivePath();

        using var host = new TestHost(withRules: true);
        const string secret = "sk-or-secret-key-must-not-be-exported";
        host.Get<ISecretStore>().Set(SecretNames.OpenRouterApiKey, secret);

        var settings = host.Get<ISettingsService>();
        await settings.LoadAsync();
        var updated = settings.Current.Clone();
        updated.Ai.Enabled = true;
        await settings.SaveAsync(updated);

        var scan = await ScanAndAnalyzeAsync(host, tree);
        await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");

        // Inspect every byte of the archive.
        using var archive = ZipFile.OpenRead(path);
        Assert.DoesNotContain(archive.Entries, e => e.Name.Contains("setting", StringComparison.OrdinalIgnoreCase));

        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            string content = await reader.ReadToEndAsync();
            Assert.DoesNotContain(secret, content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("api-key", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Manifest_DescribesTheArchive()
    {
        using var tree = new TestTree();
        string path = ArchivePath();

        using var host = new TestHost(withRules: true);
        var scan = await ScanAndAnalyzeAsync(host, tree);
        await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "fa-IR");

        var inspection = await host.Get<IWorkspaceArchiveService>().InspectAsync(path);

        Assert.True(inspection.IsSuccess);
        var manifest = inspection.Value;
        Assert.Equal(StoravaArchiveManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal("fa-IR", manifest.Culture);
        Assert.Equal(scan.SessionId, manifest.SessionId);
        Assert.Equal(tree.Root, manifest.RootPath);
        Assert.False(manifest.ContainsSecrets);
        Assert.False(manifest.ContainsSettings);

        // Every payload entry is covered by a hash.
        Assert.Contains(StoravaArchiveEntries.Items, manifest.Hashes.Keys);
        Assert.Contains(StoravaArchiveEntries.Scan, manifest.Hashes.Keys);
        Assert.Contains(StoravaArchiveEntries.Recommendations, manifest.Hashes.Keys);
    }

    [Fact]
    public async Task Import_RefusesATamperedArchive()
    {
        using var tree = new TestTree();
        string path = ArchivePath();

        using (var host = new TestHost(withRules: true))
        {
            var scan = await ScanAndAnalyzeAsync(host, tree);
            await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");
        }

        // Edit the item data after the fact, leaving the manifest's hash stale.
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry(StoravaArchiveEntries.Items)!;
            using var stream = entry.Open();
            stream.Seek(0, SeekOrigin.End);
            using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync("""{"Id":"injected","Path":"C:\\Windows","Name":"Windows"}""");
        }

        using (var host = new TestHost(withRules: true))
        {
            var import = await host.Get<IWorkspaceArchiveService>().ImportAsync(path);

            Assert.True(import.IsFailure);
            Assert.Equal(ArchiveErrors.HashMismatch.Code, import.Error.Code);
        }
    }

    [Fact]
    public async Task Import_RejectsANewerSchemaVersion()
    {
        using var tree = new TestTree();
        string path = ArchivePath();

        using (var host = new TestHost(withRules: true))
        {
            var scan = await ScanAndAnalyzeAsync(host, tree);
            await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");
        }

        // Claim a schema this build cannot know about.
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry(StoravaArchiveEntries.Manifest)!;
            string json;
            using (var reader = new StreamReader(entry.Open()))
                json = await reader.ReadToEndAsync();

            json = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
            entry.Delete();

            var replacement = archive.CreateEntry(StoravaArchiveEntries.Manifest);
            using var writer = new StreamWriter(replacement.Open());
            await writer.WriteAsync(json);
        }

        using (var host = new TestHost(withRules: true))
        {
            var import = await host.Get<IWorkspaceArchiveService>().ImportAsync(path);

            Assert.True(import.IsFailure);
            Assert.Equal(ArchiveErrors.UnsupportedVersion.Code, import.Error.Code);
        }
    }

    [Fact]
    public async Task Import_RejectsAFileThatIsNotAnArchive()
    {
        string path = Path.Combine(_directory, "not-really" + StoravaArchiveEntries.Extension);
        await File.WriteAllTextAsync(path, "this is just text");

        using var host = new TestHost();
        var import = await host.Get<IWorkspaceArchiveService>().ImportAsync(path);

        Assert.True(import.IsFailure);
        Assert.Equal(ArchiveErrors.NotAnArchive.Code, import.Error.Code);
    }

    [Fact]
    public async Task Import_ReportsAMissingFile()
    {
        using var host = new TestHost();
        var import = await host.Get<IWorkspaceArchiveService>()
            .ImportAsync(Path.Combine(_directory, "absent.storava"));

        Assert.True(import.IsFailure);
        Assert.Equal(ArchiveErrors.NotFound.Code, import.Error.Code);
    }

    [Fact]
    public async Task Import_Twice_ReplacesRatherThanDuplicates()
    {
        using var tree = new TestTree();
        string path = ArchivePath();

        using (var host = new TestHost(withRules: true))
        {
            var scan = await ScanAndAnalyzeAsync(host, tree);
            await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");
        }

        using (var host = new TestHost(withRules: true))
        {
            var service = host.Get<IWorkspaceArchiveService>();
            var first = await service.ImportAsync(path);
            var second = await service.ImportAsync(path);

            Assert.True(second.IsSuccess);
            Assert.Equal(first.Value.ItemCount, second.Value.ItemCount);

            // One session, and its items are not doubled.
            var sessions = await host.Get<IScanSessionRepository>().GetRecentAsync(10);
            Assert.Single(sessions);

            var items = await host.Get<IScanQueryService>()
                .GetLargestAsync(second.Value.SessionId, 10_000, foldersOnly: false);
            Assert.Equal(second.Value.ItemCount, items.Count);
        }
    }

    [Fact]
    public async Task Export_ReportsAnUnknownSession()
    {
        using var host = new TestHost();

        var export = await host.Get<IWorkspaceArchiveService>()
            .ExportAsync("no-such-session", ArchivePath(), "en-US");

        Assert.True(export.IsFailure);
        Assert.Equal(ArchiveErrors.SessionNotFound.Code, export.Error.Code);
    }

    [Fact]
    public async Task Export_ReportsProgress()
    {
        using var tree = new TestTree();
        for (int i = 0; i < 30; i++)
            tree.AddFile($@"sub{i}\file.bin", 1024);

        using var host = new TestHost(withRules: true);
        var scan = await ScanAsync(host, tree.Root);

        var stages = new List<string>();
        await host.Get<IWorkspaceArchiveService>().ExportAsync(
            scan.SessionId, ArchivePath(), "en-US",
            new Progress<ArchiveProgress>(p => stages.Add(p.Stage)));

        Assert.Contains("items", stages);
        Assert.Contains("recommendations", stages);
    }

    [Fact]
    public async Task Export_LeavesNoPartialFileBehind()
    {
        using var tree = new TestTree();
        tree.AddFile("a.bin", 1024);

        using var host = new TestHost(withRules: true);
        var scan = await ScanAsync(host, tree.Root);
        string path = ArchivePath();

        await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");

        Assert.False(File.Exists(path + ".partial"));
    }

    /// <summary>
    /// The item payload is JSON Lines, separated by "\n". Writing the platform newline instead
    /// would still round-trip on the machine that produced the file — the reader accepts both —
    /// but the entry hash is taken over "\n", so an archive written on Windows would be refused
    /// by its own integrity check and one written elsewhere would not match.
    /// </summary>
    [Fact]
    public async Task Items_AreSeparatedByLineFeedsOnly()
    {
        using var tree = new TestTree();
        tree.AddFile(@"a\one.bin", 1024);
        tree.AddFile(@"a\two.bin", 2048);
        string path = ArchivePath();

        using var host = new TestHost(withRules: true);
        var scan = await ScanAsync(host, tree.Root);
        await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");

        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry(StoravaArchiveEntries.Items)!.Open());
        string content = await reader.ReadToEndAsync();

        Assert.DoesNotContain('\r', content);
        Assert.Contains('\n', content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
