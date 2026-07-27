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

    public WorkspaceArchiveService(
        StoravaDbOptions options,
        IDatabaseInitializer initializer,
        IScanSessionRepository sessions,
        IScanQueryService query,
        IRecommendationRepository recommendations,
        ILogger<WorkspaceArchiveService> logger)
    {
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
                    archive, StoravaArchiveEntries.Scan, ToDto(session), cancellationToken).ConfigureAwait(false);

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
                hashes[StoravaArchiveEntries.Recommendations] = await WriteJsonEntryAsync(
                    archive, StoravaArchiveEntries.Recommendations, stored, cancellationToken).ConfigureAwait(false);

                manifest = new StoravaArchiveManifest
                {
                    AppVersion = AppVersion(),
                    CreatedAt = DateTimeOffset.Now,
                    ScanDate = session.StartedAt,
                    Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    Culture = culture,
                    SessionId = session.Id,
                    RootPath = session.RootPath,
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
            string line = JsonSerializer.Serialize(ToDto(item), JsonOptions);
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

            if (manifest.SchemaVersion > StoravaArchiveManifest.CurrentSchemaVersion)
                return Result.Failure<StoravaArchiveManifest>(ArchiveErrors.UnsupportedVersion);

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

            var sessionDto = await ReadJsonEntryAsync<SessionDto>(sessionEntry, cancellationToken).ConfigureAwait(false);
            if (sessionDto is null)
                return Result.Failure<ArchiveImportResult>(ArchiveErrors.MissingEntry);

            var session = FromDto(sessionDto);
            session.Origin = ScanOrigin.Imported;
            session.ImportedAt = DateTimeOffset.Now;
            session.SourceLabel = Path.GetFileName(filePath);
            // Pending work belongs to the machine that produced it, not to this one.
            session.ResumeState = null;

            // Re-importing the same archive replaces the earlier copy instead of duplicating it.
            await _sessions.DeleteAsync(session.Id, cancellationToken).ConfigureAwait(false);
            await _sessions.SaveAsync(session, cancellationToken).ConfigureAwait(false);

            progress?.Report(new ArchiveProgress("items", 0));
            int imported = await ImportItemsAsync(itemsEntry, session.Id, progress, cancellationToken)
                .ConfigureAwait(false);

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

            var dto = JsonSerializer.Deserialize<ItemDto>(line, JsonOptions);
            if (dto is null)
                continue;

            var item = FromDto(dto);
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

        var stored = await ReadJsonEntryAsync<List<Recommendation>>(entry, cancellationToken).ConfigureAwait(false);
        if (stored is null || stored.Count == 0)
            return 0;

        // Recommendations reference the session they belong to; rebind them to the imported copy.
        var rebound = stored.Select(r => new Recommendation
        {
            Id = r.Id,
            SessionId = sessionId,
            ScanItemId = r.ScanItemId,
            Path = r.Path,
            Title = r.Title,
            Reason = r.Reason,
            // Imported advice is still only advice.
            SuggestedAction = SuggestedAction.NoAction,
            RiskLevel = r.RiskLevel,
            Category = r.Category,
            Technology = r.Technology,
            RuleId = r.RuleId,
            EstimatedSpace = r.EstimatedSpace,
            Confidence = r.Confidence,
            Score = r.Score,
            CanDelete = r.CanDelete,
            CanMove = r.CanMove,
            CanRegenerate = r.CanRegenerate,
            OfficialMigrationMethod = r.OfficialMigrationMethod,
            FallbackMigrationMethod = r.FallbackMigrationMethod,
            OfficialMigrationHint = r.OfficialMigrationHint,
            Warning = r.Warning,
            Source = r.Source
        }).ToArray();

        await _recommendations.ReplaceForSessionAsync(sessionId, rebound, cancellationToken).ConfigureAwait(false);
        return rebound.Length;
    }

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
    // Explicit DTOs so the archive format is decoupled from the entities: renaming a
    // property later cannot silently change what old files are expected to contain.

    private static SessionDto ToDto(ScanSession s) => new(
        s.Id, s.RootPath, s.Label, s.Mode, s.Status, s.StartedAt, s.CompletedAt,
        s.TotalSize, s.TotalFiles, s.TotalFolders, s.ErrorCount);

    private static ScanSession FromDto(SessionDto d) => new()
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

    private static ItemDto ToDto(ScanItem i) => new(
        i.Id, i.ParentId, i.Path, i.SanitizedPath, i.Name, i.Extension, i.ItemType, i.Size,
        i.AllocatedSize, i.FileCount, i.FolderCount, i.Depth, i.CreationTime, i.LastWriteTime,
        i.LastAccessTime, (int)i.Attributes, i.IsHidden, i.IsSystem, i.IsReparsePoint, i.IsProtected,
        i.Category, i.DetectedTechnology, i.KnownRuleId, i.RiskLevel, i.Confidence,
        i.CanDelete, i.CanMove, i.CanRegenerate, i.SuggestedAction, i.Reason);

    private static ScanItem FromDto(ItemDto d) => new()
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

    private sealed record SessionDto(
        string Id, string RootPath, string? Label, ScanMode Mode, ScanStatus Status,
        DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        long TotalSize, int TotalFiles, int TotalFolders, int ErrorCount);

    private sealed record ItemDto(
        string Id, string? ParentId, string Path, string? SanitizedPath, string Name, string? Extension,
        ItemType ItemType, long Size, long AllocatedSize, int FileCount, int FolderCount, int Depth,
        DateTimeOffset? CreationTime, DateTimeOffset? LastWriteTime, DateTimeOffset? LastAccessTime,
        int Attributes, bool IsHidden, bool IsSystem, bool IsReparsePoint, bool IsProtected,
        StorageCategory Category, string? DetectedTechnology, string? KnownRuleId, RiskLevel RiskLevel,
        double Confidence, bool CanDelete, bool CanMove, bool CanRegenerate,
        SuggestedAction SuggestedAction, string? Reason);
}
