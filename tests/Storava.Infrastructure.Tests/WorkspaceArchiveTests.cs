using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Application.Services;
using Storava.Contracts.Workspace;
using Storava.Domain.Entities;
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

    /// <summary>
    /// An archive keeps what decides how a folder is moved.
    /// <para>
    /// The migration methods are facts about the technology, not about the machine that wrote them:
    /// npm's cache honours a path setting wherever that folder is read. Narrowing them out of the
    /// shared shape made a plan built from an imported archive fall back to a different mechanism
    /// than the catalog documented — quietly, on the very desktop that produced the archive.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recommendations_KeepWhatDecidesHowAFolderIsMoved()
    {
        using var tree = new TestTree();
        string path = ArchivePath();

        Recommendation original;

        using (var host = new TestHost(withRules: true))
        {
            var scan = await ScanAndAnalyzeAsync(host, tree);
            var stored = await host.Get<IRecommendationRepository>().GetBySessionAsync(scan.SessionId);

            // Something the catalog has an opinion about beyond "this can go".
            original = Assert.Single(stored, r => r.OfficialMigrationMethod != MigrationMethod.None
                || r.FallbackMigrationMethod != MigrationMethod.None
                || r.Category != StorageCategory.Unknown);

            await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");
        }

        using (var host = new TestHost(withRules: true))
        {
            var import = await host.Get<IWorkspaceArchiveService>().ImportAsync(path);
            var imported = await host.Get<IRecommendationRepository>()
                .GetBySessionAsync(import.Value.SessionId);

            var same = Assert.Single(imported, r => r.RuleId == original.RuleId);

            Assert.Equal(original.OfficialMigrationMethod, same.OfficialMigrationMethod);
            Assert.Equal(original.FallbackMigrationMethod, same.FallbackMigrationMethod);
            Assert.Equal(original.OfficialMigrationHint, same.OfficialMigrationHint);
            Assert.Equal(original.Category, same.Category);
            Assert.Equal(original.Technology, same.Technology);
            Assert.Equal(original.Warning, same.Warning);

            // And the plan built from it reaches the same conclusion, which is the point of all of
            // the above: the fields exist so that this decision comes out the same way.
            Assert.Equal(
                PlanCandidate.FromRecommendation(original).OfficialMigrationMethod,
                PlanCandidate.FromRecommendation(same).OfficialMigrationMethod);
        }
    }

    /// <summary>
    /// The advice inside an archive is written in the shape the other editions read.
    /// <para>
    /// A round trip through this edition alone cannot show this: the importer understands the old
    /// shape too, so writing the domain entity would still read back perfectly here while remaining
    /// unreadable everywhere else. That is exactly what happened — the browser dropped the entry
    /// for as long as it existed, and nothing on this side noticed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recommendations_AreWrittenInTheShapeOtherEditionsRead()
    {
        using var tree = new TestTree();
        string path = ArchivePath();

        using var host = new TestHost(withRules: true);
        var scan = await ScanAndAnalyzeAsync(host, tree);
        await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");

        using var archive = ZipFile.OpenRead(path);
        using var entry = archive.GetEntry(StoravaArchiveEntries.Recommendations)!.Open();

        var written = JsonSerializer.Deserialize<List<JsonElement>>(entry)!;
        Assert.NotEmpty(written);

        var first = written[0];

        // The interchange names, exactly. The desktop's own entity spells these ScanItemId,
        // RiskLevel and EstimatedSpace, and nothing outside this codebase knows those.
        Assert.True(first.TryGetProperty("itemId", out var itemId));
        Assert.False(string.IsNullOrEmpty(itemId.GetString()));
        Assert.True(first.TryGetProperty("reason", out _));
        Assert.True(first.TryGetProperty("risk", out _));
        Assert.True(first.TryGetProperty("estimatedBytes", out _));
        Assert.True(first.TryGetProperty("source", out _));

        // And not the internals of a decision made against one machine's catalog.
        Assert.False(first.TryGetProperty("Score", out _));
        Assert.False(first.TryGetProperty("ScanItemId", out _));
        Assert.False(first.TryGetProperty("SuggestedAction", out _));
    }

    /// <summary>
    /// Archives written before recommendations had a shared shape still import their advice.
    /// <para>
    /// They hold the desktop's own entity — <c>ScanItemId</c>, <c>RiskLevel</c>,
    /// <c>EstimatedSpace</c> — whose names differ enough from the interchange shape that reading
    /// one as the other yields a list of blanks rather than a failure. Importing advice with no
    /// item and no reason attached would be worse than importing none, so the fallback is real
    /// rather than assumed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recommendations_WrittenBeforeTheSharedShape_AreStillImported()
    {
        using var tree = new TestTree();
        string path = ArchivePath();

        string sessionId;
        using (var host = new TestHost(withRules: true))
        {
            var scan = await ScanAndAnalyzeAsync(host, tree);
            sessionId = scan.SessionId;
            await host.Get<IWorkspaceArchiveService>().ExportAsync(sessionId, path, "en-US");

            // Rewrite the entry the way the desktop used to: its own entity, straight to JSON.
            var stored = await host.Get<IRecommendationRepository>().GetBySessionAsync(sessionId);
            Assert.NotEmpty(stored);
            ReplaceEntry(path, StoravaArchiveEntries.Recommendations,
                JsonSerializer.SerializeToUtf8Bytes(stored, new JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                }));
        }

        using (var host = new TestHost(withRules: true))
        {
            var import = await host.Get<IWorkspaceArchiveService>().ImportAsync(path);
            Assert.True(import.IsSuccess);

            var imported = await host.Get<IRecommendationRepository>()
                .GetBySessionAsync(import.Value.SessionId);

            Assert.NotEmpty(imported);
            Assert.All(imported, r => Assert.False(string.IsNullOrEmpty(r.ScanItemId)));
            Assert.All(imported, r => Assert.False(string.IsNullOrEmpty(r.Reason)));
            Assert.Contains(imported, r => r.EstimatedSpace > 0);
        }
    }

    /// <summary>
    /// Swaps one entry's bytes inside an existing archive, and restamps the manifest hash for it.
    /// <para>
    /// The manifest hashes every entry and the importer checks them, so a rewritten entry without a
    /// matching hash is refused before it is ever parsed — which is the format working as intended
    /// and not what this test is about.
    /// </para>
    /// </summary>
    private static void ReplaceEntry(string archivePath, string entryName, byte[] bytes)
    {
        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);

        archive.GetEntry(entryName)?.Delete();
        using (var entryStream = archive.CreateEntry(entryName).Open())
            entryStream.Write(bytes);

        var manifestEntry = archive.GetEntry(StoravaArchiveEntries.Manifest)!;

        StoravaArchiveManifest manifest;
        using (var reader = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<StoravaArchiveManifest>(reader)!;

        manifest.Hashes[entryName] = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(bytes));

        manifestEntry.Delete();
        using (var rewritten = archive.CreateEntry(StoravaArchiveEntries.Manifest).Open())
            rewritten.Write(JsonSerializer.SerializeToUtf8Bytes(manifest));
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

            // Matched by shape rather than by the current number, so bumping the schema does not
            // quietly turn this test into one that edits nothing.
            string replaced = System.Text.RegularExpressions.Regex.Replace(
                json, "\"schemaVersion\":\\s*\\d+", "\"schemaVersion\": 99");
            Assert.NotEqual(json, replaced);
            json = replaced;
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

    [Fact]
    public async Task An_archive_says_which_edition_wrote_it_and_what_its_paths_mean()
    {
        using var tree = new TestTree();
        tree.AddFile("a.bin", 1024);
        string path = ArchivePath();

        using var host = new TestHost(withRules: true);
        var scan = await ScanAsync(host, tree.Root);
        await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");

        var manifest = (await host.Get<IWorkspaceArchiveService>().InspectAsync(path)).Value;

        // A reader that assumed the wrong path kind would either show locations that do not exist
        // or refuse to act on ones that do.
        Assert.Equal(ArchivePathKind.Absolute, manifest.PathKind);
        Assert.Equal(ArchiveProducer.Desktop, manifest.ProducedBy);
        Assert.Equal(StoravaArchiveManifest.CurrentSchemaVersion, manifest.SchemaVersion);
    }

    /// <summary>
    /// The interchange schema is what the browser edition reads, so the field names it depends on
    /// are part of the file format rather than an implementation detail of this class.
    /// </summary>
    [Fact]
    public async Task Items_are_written_in_the_shared_interchange_shape()
    {
        using var tree = new TestTree();
        tree.AddFile(@"proj\node_modules\big.bin", 64 * 1024);
        string path = ArchivePath();

        using var host = new TestHost(withRules: true);
        var scan = await ScanAsync(host, tree.Root);
        await host.Get<AnalysisService>().AnalyzeAsync(scan.SessionId, "en");
        await host.Get<IWorkspaceArchiveService>().ExportAsync(scan.SessionId, path, "en-US");

        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry(StoravaArchiveEntries.Items)!.Open());
        string firstLine = (await reader.ReadToEndAsync()).Split('\n')[0];

        var item = JsonSerializer.Deserialize<ArchiveItem>(firstLine);
        Assert.NotNull(item);
        Assert.NotEmpty(item!.Id);
        Assert.Contains(item.Kind, new[] { ArchiveItemKinds.File, ArchiveItemKinds.Folder });

        // camelCase, spelled out on the contract. A serializer's default naming is not something
        // to bet a file format on.
        Assert.Contains("\"id\":", firstLine, StringComparison.Ordinal);
        Assert.Contains("\"ruleIds\":", firstLine, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Id\":", firstLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// Version 1 archives were written by the first release. Refusing to open one would be the
    /// format failing at the only job it has.
    /// </summary>
    [Fact]
    public async Task A_version_one_archive_can_still_be_opened()
    {
        string path = ArchivePath("legacy");
        WriteVersionOneArchive(path);

        using var host = new TestHost(withRules: true);
        var import = await host.Get<IWorkspaceArchiveService>().ImportAsync(path);

        Assert.True(import.IsSuccess);
        Assert.Equal(2, import.Value.ItemCount);

        var items = await host.Get<IScanQueryService>()
            .GetLargestAsync(import.Value.SessionId, 50, foldersOnly: false);

        Assert.Contains(items, item => item.Name == "old.bin" && item.Size == 4096);
        Assert.Equal(ScanOrigin.Imported, (await host.Get<IScanSessionRepository>()
            .GetAsync(import.Value.SessionId))!.Origin);
    }

    /// <summary>
    /// Hand-built rather than produced by an old build, because there is no old build to run. The
    /// shapes are the entity shapes as the first release serialized them, PascalCase and all.
    /// </summary>
    private void WriteVersionOneArchive(string path)
    {
        const string sessionId = "legacysession";
        var scan = new
        {
            Id = sessionId,
            RootPath = @"C:\Legacy",
            Label = (string?)null,
            Mode = "Quick",
            Status = "Completed",
            StartedAt = DateTimeOffset.Parse("2026-01-01T10:00:00Z"),
            CompletedAt = (DateTimeOffset?)DateTimeOffset.Parse("2026-01-01T10:05:00Z"),
            TotalSize = 5120L,
            TotalFiles = 1,
            TotalFolders = 1,
            ErrorCount = 0
        };

        object Item(string id, string? parent, string name, string itemPath, string type, long size) => new
        {
            Id = id,
            ParentId = parent,
            Path = itemPath,
            SanitizedPath = (string?)null,
            Name = name,
            Extension = (string?)null,
            ItemType = type,
            Size = size,
            AllocatedSize = size,
            FileCount = 0,
            FolderCount = 0,
            Depth = 1,
            CreationTime = (DateTimeOffset?)null,
            LastWriteTime = (DateTimeOffset?)null,
            LastAccessTime = (DateTimeOffset?)null,
            Attributes = 0,
            IsHidden = false,
            IsSystem = false,
            IsReparsePoint = false,
            IsProtected = false,
            Category = "Unknown",
            DetectedTechnology = (string?)null,
            KnownRuleId = (string?)null,
            RiskLevel = "Low",
            Confidence = 0.5,
            CanDelete = false,
            CanMove = false,
            CanRegenerate = false,
            SuggestedAction = "NoAction",
            Reason = (string?)null
        };

        var items = new[]
        {
            Item("legacyroot", null, "Legacy", @"C:\Legacy", "Folder", 5120),
            Item("legacyfile", "legacyroot", "old.bin", @"C:\Legacy\old.bin", "File", 4096)
        };

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [StoravaArchiveEntries.Scan] = JsonSerializer.SerializeToUtf8Bytes(scan),
            [StoravaArchiveEntries.Items] = Encoding.UTF8.GetBytes(
                string.Concat(items.Select(i => JsonSerializer.Serialize(i) + "\n"))),
            [StoravaArchiveEntries.Categories] = "[]"u8.ToArray(),
            [StoravaArchiveEntries.Recommendations] = "[]"u8.ToArray()
        };

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (name, bytes) in entries)
        {
            using var entryStream = archive.CreateEntry(name).Open();
            entryStream.Write(bytes);
        }

        var manifest = new
        {
            schemaVersion = 1,
            appVersion = "1.0.0.0",
            createdAt = DateTimeOffset.Parse("2026-01-01T10:06:00Z"),
            scanDate = scan.StartedAt,
            os = "Windows",
            culture = "en-US",
            sessionId,
            rootPath = scan.RootPath,
            itemCount = items.Length,
            recommendationCount = 0,
            hashes = entries.ToDictionary(
                e => e.Key,
                e => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(e.Value)),
                StringComparer.Ordinal)
        };

        using var manifestStream = archive.CreateEntry(StoravaArchiveEntries.Manifest).Open();
        manifestStream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest));
    }

    /// <summary>
    /// Writes the archive the browser edition's tests read, so that side is checked against a file
    /// this code actually produced rather than one its own tests invented. Two implementations of
    /// one file format is the hazard; a shared fixture is the cheapest way to keep them honest.
    /// <para>
    /// It only writes when the fixture is missing or <c>STORAVA_REFRESH_FIXTURES</c> is set, and
    /// otherwise asserts the committed one still round-trips. A test that rewrote a checked-in file
    /// on every run would make a format change look like no change at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_shared_fixture_the_browser_reads_still_round_trips()
    {
        var repository = RepositoryRoot();
        string fixture = Path.Combine(
            repository.FullName, "src", "Storava.Web", "ClientApp", "test", "fixtures", "desktop-v2.storava");

        bool refresh = Environment.GetEnvironmentVariable("STORAVA_REFRESH_FIXTURES") is { Length: > 0 };

        if (refresh || !File.Exists(fixture))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixture)!);

            using var tree = new TestTree();

            // Over the rule catalog's 50 MB floor on purpose. Below it nothing is a candidate, so
            // this archive carried no advice at all — which is how the browser edition silently
            // dropping the recommendations entry went unnoticed for as long as it did.
            tree.AddFile(@"proj\node_modules\pkg\lib.bin", (int)RecommendationBuilder.MinimumCandidateSize + 4096);
            tree.AddFile(@"docs\notes.txt", 512);

            using var writer = new TestHost(withRules: true);
            var scan = await ScanAsync(writer, tree.Root);
            await writer.Get<AnalysisService>().AnalyzeAsync(scan.SessionId, "en");

            var written = await writer.Get<IWorkspaceArchiveService>()
                .ExportAsync(scan.SessionId, fixture, "en-US");
            Assert.True(written.IsSuccess);
        }

        using var host = new TestHost(withRules: true);
        var inspection = await host.Get<IWorkspaceArchiveService>().InspectAsync(fixture);

        Assert.True(inspection.IsSuccess, "The committed fixture is no longer readable by this build.");
        Assert.Equal(ArchivePathKind.Absolute, inspection.Value.PathKind);
        Assert.Equal(ArchiveProducer.Desktop, inspection.Value.ProducedBy);

        var import = await host.Get<IWorkspaceArchiveService>().ImportAsync(fixture);
        Assert.True(import.IsSuccess);
        Assert.True(import.Value.ItemCount > 0);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!;
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
