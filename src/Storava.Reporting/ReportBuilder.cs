using Storava.Application.Abstractions;
using Storava.Domain.Enums;
using Storava.Reporting.Model;

namespace Storava.Reporting;

/// <summary>
/// Assembles a <see cref="StorageReport"/> from what is already stored locally. Labels are
/// resolved through a caller-supplied lookup so the report matches the selected language.
/// </summary>
public sealed class ReportBuilder
{
    private const int LargestItemCount = 25;

    private readonly IScanSessionRepository _sessions;
    private readonly IScanQueryService _query;
    private readonly IRecommendationRepository _recommendations;
    private readonly IStorageInfoService _storage;

    public ReportBuilder(
        IScanSessionRepository sessions,
        IScanQueryService query,
        IRecommendationRepository recommendations,
        IStorageInfoService storage)
    {
        _sessions = sessions;
        _query = query;
        _recommendations = recommendations;
        _storage = storage;
    }

    /// <param name="categoryLabel">Localized name for a category.</param>
    /// <param name="riskLabel">Localized name for a risk level.</param>
    public async Task<StorageReport> BuildAsync(
        string sessionId,
        string language,
        Func<StorageCategory, string> categoryLabel,
        Func<RiskLevel, string> riskLabel,
        ReportAiSection? ai = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(categoryLabel);
        ArgumentNullException.ThrowIfNull(riskLabel);

        var session = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Scan session '{sessionId}' was not found.");

        var usage = await _query.GetCategoryUsageAsync(sessionId, cancellationToken).ConfigureAwait(false);
        long usageTotal = usage.Sum(u => u.TotalSize);

        var largest = await _query
            .GetLargestAsync(sessionId, LargestItemCount, foldersOnly: true, cancellationToken)
            .ConfigureAwait(false);

        var stored = await _recommendations.GetBySessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var drive = ResolveDrive(session.RootPath);

        return new StorageReport
        {
            SessionId = session.Id,
            RootPath = session.RootPath,
            Language = language,
            ScanStartedAt = session.StartedAt.ToLocalTime(),
            ScanDuration = session.Duration ?? TimeSpan.Zero,
            TotalSize = session.TotalSize,
            FileCount = session.TotalFiles,
            FolderCount = session.TotalFolders,
            ErrorCount = session.ErrorCount,
            DriveCapacity = drive?.TotalSize.Bytes ?? 0,
            DriveFreeSpace = drive?.FreeSpace.Bytes ?? 0,
            Categories = usage
                .Where(u => u.TotalSize > 0)
                .Select(u => new ReportCategory(
                    u.Category,
                    categoryLabel(u.Category),
                    u.TotalSize,
                    usageTotal == 0 ? 0 : (double)u.TotalSize / usageTotal))
                .ToArray(),
            LargestItems = largest
                .Where(i => i.Depth > 0)
                .Select(i => new ReportItem(
                    i.Path, i.Name, i.Size, i.Category, categoryLabel(i.Category), i.IsProtected))
                .ToArray(),
            Recommendations = stored
                .Select(r => new ReportRecommendation(
                    r.Title,
                    r.Path,
                    r.Reason,
                    r.EstimatedSpace,
                    r.RiskLevel,
                    riskLabel(r.RiskLevel),
                    r.Category,
                    categoryLabel(r.Category),
                    r.Technology,
                    r.OfficialMigrationHint,
                    r.Warning,
                    r.Confidence,
                    r.CanDelete,
                    r.CanMove,
                    r.CanRegenerate,
                    r.Source))
                .ToArray(),
            Ai = ai
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
}
