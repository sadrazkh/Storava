using Storava.AI.Privacy;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Application.Settings;
using Storava.Contracts.Ai;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.AI;

/// <summary>
/// Builds the sanitized summary that may be sent to the AI. The full file tree is never
/// included: only aggregates, a bounded number of candidates, and paths that have been run
/// through <see cref="PathSanitizer"/>.
/// </summary>
public sealed class AiPayloadBuilder
{
    private const int MaxCandidates = 25;
    private const int MaxUnknownItems = 15;
    private const long OneGigabyte = 1024L * 1024 * 1024;

    private readonly IScanQueryService _query;
    private readonly IScanSessionRepository _sessions;
    private readonly IStorageInfoService _storage;

    public AiPayloadBuilder(
        IScanQueryService query,
        IScanSessionRepository sessions,
        IStorageInfoService storage)
    {
        _query = query;
        _sessions = sessions;
        _storage = storage;
    }

    /// <summary>
    /// Produces the payload together with the id → real path map, which stays on this machine
    /// and is used to resolve validated recommendations back to actual items.
    /// </summary>
    public async Task<AiPayloadResult> BuildAsync(
        string sessionId,
        AiSettings settings,
        string language,
        double targetFreeSpaceGb,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(settings);

        var session = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scan session '{sessionId}' was not found.");

        var sanitizer = new PathSanitizer();
        var localPaths = new Dictionary<string, string>(StringComparer.Ordinal);

        var candidates = await BuildCandidatesAsync(sessionId, sanitizer, localPaths, cancellationToken)
            .ConfigureAwait(false);

        var unknown = settings.AllowUnknownFolderAnalysis
            ? await BuildUnknownItemsAsync(sessionId, sanitizer, localPaths, cancellationToken).ConfigureAwait(false)
            : [];

        var categories = await BuildCategoriesAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var drive = ResolveDrive(session.RootPath);

        var payload = new AiRequestPayload
        {
            System = new AiSystemInfo
            {
                Os = GetOsDescription(),
                SelectedLanguage = language,
                Drive = drive is null ? "<Drive-?>" : $"<Drive-{drive.Name.TrimEnd('\\', ':')}>",
                CapacityGb = ToGb(drive?.TotalSize.Bytes ?? 0),
                FreeGb = ToGb(drive?.FreeSpace.Bytes ?? 0),
                ScannedGb = ToGb(session.TotalSize),
                FileCount = session.TotalFiles,
                FolderCount = session.TotalFolders
            },
            Categories = categories,
            TopCandidates = candidates,
            UnknownItems = unknown,
            UserGoal = new AiUserGoal
            {
                TargetFreeSpaceGb = Math.Round(targetFreeSpaceGb, 1),
                // Phase 4 is advice-only: the AI is told plainly that nothing may be executed.
                AllowDelete = false,
                AllowMove = true
            }
        };

        return new AiPayloadResult(payload, localPaths, sanitizer);
    }

    private async Task<IReadOnlyList<AiCandidate>> BuildCandidatesAsync(
        string sessionId,
        PathSanitizer sanitizer,
        Dictionary<string, string> localPaths,
        CancellationToken cancellationToken)
    {
        var items = await _query
            .GetRecommendationCandidatesAsync(sessionId, 50L * 1024 * 1024, MaxCandidates, cancellationToken)
            .ConfigureAwait(false);

        return items.Select(item => ToCandidate(item, sanitizer, localPaths)).ToArray();
    }

    private async Task<IReadOnlyList<AiCandidate>> BuildUnknownItemsAsync(
        string sessionId,
        PathSanitizer sanitizer,
        Dictionary<string, string> localPaths,
        CancellationToken cancellationToken)
    {
        var largest = await _query
            .GetLargestAsync(sessionId, MaxUnknownItems * 4, foldersOnly: true, cancellationToken)
            .ConfigureAwait(false);

        return largest
            .Where(i => !i.IsIdentified && !i.IsProtected && i.Depth > 0)
            .Take(MaxUnknownItems)
            .Select(item => ToCandidate(item, sanitizer, localPaths))
            .ToArray();
    }

    private async Task<IReadOnlyList<AiCategoryUsage>> BuildCategoriesAsync(
        string sessionId, CancellationToken cancellationToken)
    {
        var usage = await _query.GetCategoryUsageAsync(sessionId, cancellationToken).ConfigureAwait(false);
        long total = usage.Sum(u => u.TotalSize);

        return usage
            .Where(u => u.TotalSize > 0)
            .Select(u => new AiCategoryUsage
            {
                Category = u.Category.ToString(),
                SizeGb = ToGb(u.TotalSize),
                Share = total == 0 ? 0 : Math.Round((double)u.TotalSize / total, 3)
            })
            .ToArray();
    }

    private static AiCandidate ToCandidate(
        ScanItemView item, PathSanitizer sanitizer, Dictionary<string, string> localPaths)
    {
        // Keep the real path locally so a validated recommendation can be resolved later.
        localPaths[item.Id] = item.Path;

        int? idleDays = item.LastWriteTime is { } written
            ? (int)Math.Max(0, (DateTimeOffset.UtcNow - written).TotalDays)
            : null;

        return new AiCandidate
        {
            ScanItemId = item.Id,
            SanitizedPath = sanitizer.Sanitize(item.Path),
            SizeGb = ToGb(item.Size),
            Category = item.Category.ToString(),
            Technology = item.DetectedTechnology,
            RiskLevel = item.RiskLevel.ToString(),
            CanDelete = item.CanDelete,
            CanMove = item.CanMove,
            CanRegenerate = item.CanRegenerate,
            HasOfficialMigration = item.CanMove,
            DaysSinceLastWrite = idleDays
        };
    }

    private Application.Common.DriveSnapshot? ResolveDrive(string rootPath)
    {
        try
        {
            string? root = Path.GetPathRoot(rootPath);
            if (string.IsNullOrEmpty(root))
                return null;

            return _storage.GetDrives()
                .FirstOrDefault(d => string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string GetOsDescription() =>
        System.Runtime.InteropServices.RuntimeInformation.OSDescription.Contains("11")
            ? "Windows 11"
            : "Windows";

    private static double ToGb(long bytes) => Math.Round((double)bytes / OneGigabyte, 2);
}

/// <summary>
/// The payload plus the local-only id → real path map. <see cref="LocalPaths"/> must never be
/// serialized into a request, an export or a log.
/// </summary>
public sealed record AiPayloadResult(
    AiRequestPayload Payload,
    IReadOnlyDictionary<string, string> LocalPaths,
    PathSanitizer Sanitizer);
