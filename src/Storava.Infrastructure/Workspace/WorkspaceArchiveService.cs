using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Contracts.Workspace;
using Storava.Domain.Common;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Infrastructure.Persistence;

namespace Storava.Infrastructure.Workspace;

/// <summary>
/// Reads and writes <c>.storava</c> archives: a plain ZIP holding the scan, its items and its
/// recommendations, plus a manifest with a SHA-256 per entry.
/// <para>
/// Items are streamed as JSON Lines, so exporting a scan of any size never materialises the whole
/// tree in memory. Settings and secrets are structurally absent: this service only ever reads the
/// scan tables, so an archive cannot carry an API key even by accident.
/// </para>
/// </summary>
public sealed class WorkspaceArchiveService : IWorkspaceArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions IndentedJsonOptions = new(JsonOptions)
    {
        WriteIndented = true
    };

    private readonly StoravaDbOptions _options;
    private readonly IDatabaseInitializer _initializer;
    private readonly IScanSessionRepository _sessions;
    private readonly IScanQueryService _query;
    private readonly IRecommendationRepository _recommendations;
    private readonly ILogger<WorkspaceArchiveService> _logger;
    private readonly ArchiveIdentity _identity;

    public WorkspaceArchiveService(
        StoravaDbOptions options,
        IDatabaseInitializer initializer,
        IScanSessionRepository sessions,
        IScanQueryService query,
        IRecommendationRepository recommendations,
        ArchiveIdentity identity,
        ILogger<WorkspaceArchiveService> logger)
    {
        _identity = identity;
        _options = options;
        _initializer = initializer;
        _sessions = sessions;
        _query = query;
        _recommendations = recommendations;
        _logger = logger;
    }

    // --- Export ------------------------------------------------------------------

    public async Task<Result<StoravaArchiveManifest>> ExportAsync(
        string sessionId,
        string filePath,
        string culture,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        var session = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
            return Result.Failure<StoravaArchiveManifest>(ArchiveErrors.SessionNotFound);

        var itemStore = new ScanItemStore(_options);

        try
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            int itemCount;
            int recommendationCount;
            StoravaArchiveManifest manifest;

            // Write to a temporary file first so an interrupted export cannot leave behind a
            // half-written archive under the name the user chose.
            string tempPath = filePath + ".partial";

            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                progress?.Report(new ArchiveProgress("scan", 0));
                hashes[StoravaArchiveEntries.Scan] = await WriteJsonEntryAsync(
                    archive, StoravaArchiveEntries.Scan, ToArchive(session), cancellationToken).ConfigureAwait(false);

                progress?.Report(new ArchiveProgress("items", 0));
                (itemCount, hashes[StoravaArchiveEntries.Items]) = await WriteItemsAsync(
                    archive, itemStore, sessionId, progress, cancellationToken).ConfigureAwait(false);

                progress?.Report(new ArchiveProgress("categories", itemCount));
                var categories = await _query.GetCategoryUsageAsync(sessionId, cancellationToken).ConfigureAwait(false);
                hashes[StoravaArchiveEntries.Categories] = await WriteJsonEntryAsync(
                    archive, StoravaArchiveEntries.Categories, categories, cancellationToken).ConfigureAwait(false);

                progress?.Report(new ArchiveProgress("recommendations", itemCount));
                var stored = await _recommendations.GetBySessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                recommendationCount = stored.Count;

                // Written through the shared shape rather than as the domain entity. Serialising
                // the entity produced PascalCase names and internal enums, which no other edition
                // could read — so the browser dropped this entry entirely and the advice was lost
                // on the way out of the desktop.
                hashes[StoravaArchiveEntries.Recommendations] = await WriteJsonEntryAsync(
                    archive,
                    StoravaArchiveEntries.Recommendations,
                    stored.Select(ToArchiveRecommendation).ToArray(),
                    cancellationToken).ConfigureAwait(false);

                manifest = new StoravaArchiveManifest
                {
                    AppVersion = AppVersion(),
                    CreatedAt = DateTimeOffset.Now,
                    ScanDate = session.StartedAt,
                    Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    Culture = culture,
                    SessionId = session.Id,
                    RootPath = session.RootPath,
                    // This edition walks the file system, so its paths are real locations. The
                    // browser's are not, and a reader has to be told which it is holding.
                    PathKind = ArchivePathKind.Absolute,
                    ProducedBy = _identity.Producer,
                    ItemCount = itemCount,
                    RecommendationCount = recommendationCount,
                    Hashes = hashes
                };

                // The manifest goes in last: it describes everything written before it.
                await WriteJsonEntryAsync(archive, StoravaArchiveEntries.Manifest, manifest, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Only now: closing the ZipArchive is what writes its central directory, and the file
            // handle has to be released before the temporary file can take the chosen name.
            File.Move(tempPath, filePath, overwrite: true);

            _logger.LogInformation(
                "Exported scan {SessionId}: {Items} items, {Recommendations} recommendations.",
                sessionId, itemCount, recommendationCount);

            return Result.Success(manifest);
        }
        catch (OperationCanceledException)
        {
            TryDelete(filePath + ".partial");
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(filePath + ".partial");
            _logger.LogError(ex, "Writing the archive failed.");
            return Result.Failure<StoravaArchiveManifest>(ArchiveErrors.WriteFailed);
        }
    }

    private static async Task<(int Count, string Hash)> WriteItemsAsync(
        ZipArchive archive,
        ScanItemStore store,
        string sessionId,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(StoravaArchiveEntries.Items, CompressionLevel.Optimal);

        int count = 0;
        await using var entryStream = entry.Open();
        // Hash as we write, so the payload is never read back or buffered just to digest it.
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // JSON Lines separates records with "\n". Leaving the platform default here would write
        // "\r\n" on Windows while the hash below is taken over "\n", so every import would then
        // fail its own integrity check — and an archive would not survive crossing platforms.
        await using var writer = new StreamWriter(entryStream, new UTF8Encoding(false)) { NewLine = "\n" };

        await foreach (var item in store.StreamAsync(sessionId, cancellationToken).ConfigureAwait(false))
        {
            string line = JsonSerializer.Serialize(ToArchive(item), JsonOptions);
            hasher.AppendData(Encoding.UTF8.GetBytes(line));
            hasher.AppendData("\n"u8);

            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);

            count++;
            if (count % 5000 == 0)
                progress?.Report(new ArchiveProgress("items", count));
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        return (count, Convert.ToHexString(hasher.GetHashAndReset()));
    }

    private static async Task<string> WriteJsonEntryAsync<T>(
        ZipArchive archive, string entryName, T value, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, IndentedJsonOptions);

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    // --- Inspect -----------------------------------------------------------------

    public async Task<Result<StoravaArchiveManifest>> InspectAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return Result.Failure<StoravaArchiveManifest>(ArchiveErrors.NotFound);

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var manifest = await ReadManifestAsync(archive, cancellationToken).ConfigureAwait(false);
            if (manifest is null)
                return Result.Failure<StoravaArchiveManifest>(ArchiveErrors.NotAnArchive);

            // Newer than this build can read is a refusal; older is not. An archive is meant to
            // outlive the release that wrote it.
            if (manifest.SchemaVersion > StoravaArchiveManifest.CurrentSchemaVersion ||
                manifest.SchemaVersion < StoravaArchiveManifest.MinimumReadableSchemaVersion)
            {
                return Result.Failure<StoravaArchiveManifest>(ArchiveErrors.UnsupportedVersion);
            }

            return Result.Success(manifest);
        }
        catch (InvalidDataException)
        {
            return Result.Failure<StoravaArchiveManifest>(ArchiveErrors.NotAnArchive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Reading the archive failed.");
            return Result.Failure<StoravaArchiveManifest>(ArchiveErrors.ReadFailed);
        }
    }

    // --- Import ------------------------------------------------------------------

    public async Task<Result<ArchiveImportResult>> ImportAsync(
        string filePath,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var inspection = await InspectAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (inspection.IsFailure)
            return Result.Failure<ArchiveImportResult>(inspection.Error);

        var manifest = inspection.Value;
        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var sessionEntry = archive.GetEntry(StoravaArchiveEntries.Scan);
            var itemsEntry = archive.GetEntry(StoravaArchiveEntries.Items);
            if (sessionEntry is null || itemsEntry is null)
                return Result.Failure<ArchiveImportResult>(ArchiveErrors.MissingEntry);

            // Verify integrity before touching the database.
            var verification = await VerifyAsync(archive, manifest, cancellationToken).ConfigureAwait(false);
            if (verification.IsFailure)
                return Result.Failure<ArchiveImportResult>(verification.Error);

            var session = manifest.SchemaVersion >= 2
                ? await ReadJsonEntryAsync<ArchiveScan>(sessionEntry, cancellationToken)
                    .ConfigureAwait(false) is { } scan ? FromArchive(scan) : null
                : await ReadJsonEntryAsync<LegacySessionDto>(sessionEntry, cancellationToken)
                    .ConfigureAwait(false) is { } legacy ? FromLegacy(legacy) : null;

            if (session is null)
                return Result.Failure<ArchiveImportResult>(ArchiveErrors.MissingEntry);

            session.Origin = ScanOrigin.Imported;
            session.ImportedAt = DateTimeOffset.Now;
            session.SourceLabel = Path.GetFileName(filePath);
            // Pending work belongs to the machine that produced it, not to this one.
            session.ResumeState = null;

            // Re-importing the same archive replaces the earlier copy instead of duplicating it.
            await _sessions.DeleteAsync(session.Id, cancellationToken).ConfigureAwait(false);
            await _sessions.SaveAsync(session, cancellationToken).ConfigureAwait(false);

            progress?.Report(new ArchiveProgress("items", 0));
            int imported = await ImportItemsAsync(
                itemsEntry, session.Id, manifest.SchemaVersion, progress, cancellationToken).ConfigureAwait(false);

            int recommendationCount = await ImportRecommendationsAsync(archive, session.Id, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Imported scan {SessionId} from an archive: {Items} items, {Recommendations} recommendations.",
                session.Id, imported, recommendationCount);

            return Result.Success(new ArchiveImportResult(
                session.Id, session.RootPath, imported, recommendationCount, session.StartedAt));
        }
        catch (InvalidDataException)
        {
            return Result.Failure<ArchiveImportResult>(ArchiveErrors.NotAnArchive);
        }
        catch (JsonException)
        {
            return Result.Failure<ArchiveImportResult>(ArchiveErrors.NotAnArchive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Importing the archive failed.");
            return Result.Failure<ArchiveImportResult>(ArchiveErrors.ReadFailed);
        }
    }

    private static async Task<Result> VerifyAsync(
        ZipArchive archive, StoravaArchiveManifest manifest, CancellationToken cancellationToken)
    {
        foreach (var (entryName, expected) in manifest.Hashes)
        {
            var entry = archive.GetEntry(entryName);
            if (entry is null)
                return Result.Failure(ArchiveErrors.MissingEntry);

            await using var entryStream = entry.Open();
            byte[] actual = await SHA256.HashDataAsync(entryStream, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(Convert.ToHexString(actual), expected, StringComparison.OrdinalIgnoreCase))
                return Result.Failure(ArchiveErrors.HashMismatch);
        }

        return Result.Success();
    }

    private async Task<int> ImportItemsAsync(
        ZipArchiveEntry entry,
        string sessionId,
        int schemaVersion,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var factory = new SqliteScanItemSinkFactory(_options);
        await using var sink = factory.Create(sessionId);

        int count = 0;
        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var item = schemaVersion >= 2
                ? JsonSerializer.Deserialize<ArchiveItem>(line, JsonOptions) is { } archived
                    ? FromArchive(archived)
                    : null
                : JsonSerializer.Deserialize<LegacyItemDto>(line, JsonOptions) is { } legacy
                    ? FromLegacy(legacy)
                    : null;

            if (item is null)
                continue;

            item.SessionId = sessionId;
            await sink.AddAsync(item, cancellationToken).ConfigureAwait(false);

            count++;
            if (count % 5000 == 0)
                progress?.Report(new ArchiveProgress("items", count));
        }

        await sink.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    private async Task<int> ImportRecommendationsAsync(
        ZipArchive archive, string sessionId, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(StoravaArchiveEntries.Recommendations);
        if (entry is null)
            return 0;

        var stored = await ReadRecommendationsAsync(entry, cancellationToken).ConfigureAwait(false);
        if (stored.Count == 0)
            return 0;

        // Rebound to the imported copy of the scan: the ids inside the archive belong to the
        // machine that wrote it.
        var rebound = stored.Select(r => new Recommendation
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            ScanItemId = r.ItemId,
            Path = r.Path,
            Title = r.Title,
            Reason = r.Reason,
            // Imported advice is still only advice.
            SuggestedAction = SuggestedAction.NoAction,
            RiskLevel = Enum.TryParse<RiskLevel>(r.Risk, ignoreCase: true, out var risk) ? risk : RiskLevel.Unknown,
            RuleId = r.RuleId,
            EstimatedSpace = r.EstimatedBytes,
            Category = Enum.TryParse<StorageCategory>(r.Category, ignoreCase: true, out var category)
                ? category
                : StorageCategory.Unknown,
            Technology = r.Technology,
            OfficialMigrationMethod = ParseMethod(r.OfficialMethod),
            FallbackMigrationMethod = ParseMethod(r.FallbackMethod),
            OfficialMigrationHint = r.MethodHint,
            Warning = r.Warning,
            CanDelete = r.CanDelete,
            CanMove = r.CanMove,
            Source = Enum.TryParse<RecommendationSource>(r.Source, ignoreCase: true, out var source)
                ? source
                : RecommendationSource.RuleEngine
        }).ToArray();

        await _recommendations.ReplaceForSessionAsync(sessionId, rebound, cancellationToken).ConfigureAwait(false);
        return rebound.Length;
    }

    /// <summary>
    /// Reads the recommendations entry, in either the shared shape or the one archives used before
    /// there was a shared shape.
    /// <para>
    /// Older archives hold the desktop's own entity, whose field names differ enough that reading
    /// them as the shared shape yields a list of blanks rather than a failure. Silently importing
    /// advice with no item and no reason attached would be worse than not importing it, so the
    /// fallback is explicit: if nothing came back bound to an item, read it the old way.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<ArchiveRecommendation>> ReadRecommendationsAsync(
        ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var shared = await ReadJsonEntryAsync<List<ArchiveRecommendation>>(entry, cancellationToken)
            .ConfigureAwait(false);

        if (shared is { Count: > 0 } && shared.Any(r => !string.IsNullOrEmpty(r.ItemId)))
            return shared;

        var legacy = await ReadJsonEntryAsync<List<Recommendation>>(entry, cancellationToken)
            .ConfigureAwait(false);

        return legacy is null or { Count: 0 }
            ? []
            : legacy.Select(ToArchiveRecommendation).ToArray();
    }

    /// <summary>
    /// Reads a migration method by name, defaulting to none rather than guessing.
    /// <para>
    /// An unrecognised name means an archive from a version that knows a mechanism this one does
    /// not. Treating it as none is the answer that cannot do anything unexpected to a folder.
    /// </para>
    /// </summary>
    private static MigrationMethod ParseMethod(string? name) =>
        Enum.TryParse<MigrationMethod>(name, ignoreCase: true, out var method)
            ? method
            : MigrationMethod.None;

    /// <summary>
    /// Narrows a stored recommendation to what another edition can use.
    /// <para>
    /// The score, the confidence and the migration mechanics stay behind. They describe a decision
    /// reached against one machine's rule catalog, and an edition reading this applies its own.
    /// </para>
    /// </summary>
    private static ArchiveRecommendation ToArchiveRecommendation(Recommendation source) => new()
    {
        Id = source.Id,
        ItemId = source.ScanItemId,
        Path = source.Path,
        Title = source.Title,
        Reason = source.Reason,
        Risk = source.RiskLevel.ToString(),
        EstimatedBytes = source.EstimatedSpace,
        RuleId = source.RuleId,
        Category = source.Category.ToString(),
        Technology = source.Technology,
        OfficialMethod = source.OfficialMigrationMethod.ToString(),
        FallbackMethod = source.FallbackMigrationMethod.ToString(),
        MethodHint = source.OfficialMigrationHint,
        Warning = source.Warning,
        Source = source.Source.ToString(),
        CanDelete = source.CanDelete,
        CanMove = source.CanMove
    };

    private static async Task<StoravaArchiveManifest?> ReadManifestAsync(
        ZipArchive archive, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(StoravaArchiveEntries.Manifest);
        return entry is null
            ? null
            : await ReadJsonEntryAsync<StoravaArchiveManifest>(entry, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJsonEntryAsync<T>(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static string AppVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    // --- Archive shapes ----------------------------------------------------------
    // Version 2 writes the interchange schema from Storava.Contracts, which every edition maps to
    // and from. Version 1 wrote these entities directly, so its readers stay below: an archive
    // outlives the release that produced it, and refusing to open one would be the format failing
    // at the only job it has.

    private static ArchiveScan ToArchive(ScanSession s) => new()
    {
        Id = s.Id,
        Root = s.RootPath,
        Label = s.Label,
        Mode = s.Mode == ScanMode.Deep ? "deep" : "quick",
        Status = s.Status.ToString().ToLowerInvariant(),
        StartedAt = s.StartedAt,
        CompletedAt = s.CompletedAt,
        TotalBytes = s.TotalSize,
        TotalFiles = s.TotalFiles,
        TotalFolders = s.TotalFolders,
        ErrorCount = s.ErrorCount
    };

    private static ScanSession FromArchive(ArchiveScan a) => new()
    {
        Id = a.Id,
        RootPath = a.Root,
        Label = a.Label,
        Mode = string.Equals(a.Mode, "deep", StringComparison.OrdinalIgnoreCase) ? ScanMode.Deep : ScanMode.Quick,
        Status = Enum.TryParse<ScanStatus>(a.Status, ignoreCase: true, out var status) ? status : ScanStatus.Completed,
        StartedAt = a.StartedAt,
        CompletedAt = a.CompletedAt,
        TotalSize = a.TotalBytes,
        TotalFiles = a.TotalFiles,
        TotalFolders = a.TotalFolders,
        ErrorCount = a.ErrorCount
    };

    private static ArchiveItem ToArchive(ScanItem i) => new()
    {
        Id = i.Id,
        ParentId = i.ParentId,
        Path = i.Path,
        Name = i.Name,
        Kind = i.ItemType == ItemType.Folder ? ArchiveItemKinds.Folder : ArchiveItemKinds.File,
        Extension = i.Extension,
        Size = i.Size,
        AllocatedSize = i.AllocatedSize,
        FileCount = i.FileCount,
        FolderCount = i.FolderCount,
        Depth = i.Depth,
        CreatedAt = i.CreationTime,
        ModifiedAt = i.LastWriteTime,
        Category = i.Category.ToString(),
        Technology = i.DetectedTechnology,
        // A list even though this edition matches one rule: the browser matches several, and a
        // superset is lossless in both directions where a single value would not be.
        RuleIds = i.KnownRuleId is { Length: > 0 } rule ? [rule] : [],
        Risk = i.RiskLevel.ToString(),
        IsProtected = i.IsProtected,
        IsReparsePoint = i.IsReparsePoint,
        CanDelete = i.CanDelete,
        CanMove = i.CanMove
    };

    private static ScanItem FromArchive(ArchiveItem a) => new()
    {
        Id = a.Id,
        ParentId = a.ParentId,
        Path = a.Path,
        Name = a.Name,
        Extension = a.Extension,
        ItemType = string.Equals(a.Kind, ArchiveItemKinds.Folder, StringComparison.OrdinalIgnoreCase)
            ? ItemType.Folder
            : ItemType.File,
        Size = a.Size,
        AllocatedSize = a.AllocatedSize ?? a.Size,
        FileCount = a.FileCount,
        FolderCount = a.FolderCount,
        Depth = a.Depth,
        CreationTime = a.CreatedAt,
        LastWriteTime = a.ModifiedAt,
        Category = Enum.TryParse<StorageCategory>(a.Category, ignoreCase: true, out var category)
            ? category
            : StorageCategory.Unknown,
        DetectedTechnology = a.Technology,
        KnownRuleId = a.RuleIds.Count > 0 ? a.RuleIds[0] : null,
        RiskLevel = Enum.TryParse<RiskLevel>(a.Risk, ignoreCase: true, out var risk) ? risk : RiskLevel.Unknown,
        IsProtected = a.IsProtected,
        IsReparsePoint = a.IsReparsePoint,
        CanDelete = a.CanDelete,
        CanMove = a.CanMove,
        // Advice from another machine is advice, not an instruction. Import already forces this on
        // recommendations; items get the same treatment.
        SuggestedAction = SuggestedAction.NoAction
    };

    // --- Version 1 readers -------------------------------------------------------
    // Only ever read. These mirror the entity shapes as the first release happened to serialize
    // them, which is exactly why version 2 stopped doing that.

    private static ScanSession FromLegacy(LegacySessionDto d) => new()
    {
        Id = d.Id,
        RootPath = d.RootPath,
        Label = d.Label,
        Mode = d.Mode,
        Status = d.Status,
        StartedAt = d.StartedAt,
        CompletedAt = d.CompletedAt,
        TotalSize = d.TotalSize,
        TotalFiles = d.TotalFiles,
        TotalFolders = d.TotalFolders,
        ErrorCount = d.ErrorCount
    };

    private static ScanItem FromLegacy(LegacyItemDto d) => new()
    {
        Id = d.Id,
        ParentId = d.ParentId,
        Path = d.Path,
        SanitizedPath = d.SanitizedPath,
        Name = d.Name,
        Extension = d.Extension,
        ItemType = d.ItemType,
        Size = d.Size,
        AllocatedSize = d.AllocatedSize,
        FileCount = d.FileCount,
        FolderCount = d.FolderCount,
        Depth = d.Depth,
        CreationTime = d.CreationTime,
        LastWriteTime = d.LastWriteTime,
        LastAccessTime = d.LastAccessTime,
        Attributes = (FileAttributes)d.Attributes,
        IsHidden = d.IsHidden,
        IsSystem = d.IsSystem,
        IsReparsePoint = d.IsReparsePoint,
        IsProtected = d.IsProtected,
        Category = d.Category,
        DetectedTechnology = d.DetectedTechnology,
        KnownRuleId = d.KnownRuleId,
        RiskLevel = d.RiskLevel,
        Confidence = d.Confidence,
        CanDelete = d.CanDelete,
        CanMove = d.CanMove,
        CanRegenerate = d.CanRegenerate,
        SuggestedAction = d.SuggestedAction,
        Reason = d.Reason
    };

    private sealed record LegacySessionDto(
        string Id, string RootPath, string? Label, ScanMode Mode, ScanStatus Status,
        DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        long TotalSize, int TotalFiles, int TotalFolders, int ErrorCount);

    private sealed record LegacyItemDto(
        string Id, string? ParentId, string Path, string? SanitizedPath, string Name, string? Extension,
        ItemType ItemType, long Size, long AllocatedSize, int FileCount, int FolderCount, int Depth,
        DateTimeOffset? CreationTime, DateTimeOffset? LastWriteTime, DateTimeOffset? LastAccessTime,
        int Attributes, bool IsHidden, bool IsSystem, bool IsReparsePoint, bool IsProtected,
        StorageCategory Category, string? DetectedTechnology, string? KnownRuleId, RiskLevel RiskLevel,
        double Confidence, bool CanDelete, bool CanMove, bool CanRegenerate,
        SuggestedAction SuggestedAction, string? Reason);
}
