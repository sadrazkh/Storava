using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Domain.Common;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Application.History;

/// <summary>
/// Reads back what has been scanned and what was done about it: the list of past scans, how one
/// root's size has moved over time, the difference between any two scans of it, and the record of
/// every change Storava made to the disk.
/// <para>
/// Like the planning service, this one only reads and prunes its own database — it has no
/// reference to <c>IFileSystemActions</c> and therefore cannot touch a user file.
/// </para>
/// </summary>
public sealed class ScanHistoryService
{
    /// <summary>
    /// How deep the comparison looks. Deeper than this and a rebuilt package cache produces
    /// thousands of rows that all say the same thing as their parent.
    /// </summary>
    public const int ComparisonDepth = 4;

    private const int ComparisonRowLimit = 4000;

    private readonly IScanSessionRepository _sessions;
    private readonly IScanQueryService _query;
    private readonly IStoragePlanRepository _plans;
    private readonly IPlanExecutionRepository _executions;
    private readonly ILogger<ScanHistoryService> _logger;

    public ScanHistoryService(
        IScanSessionRepository sessions,
        IScanQueryService query,
        IStoragePlanRepository plans,
        IPlanExecutionRepository executions,
        ILogger<ScanHistoryService> logger)
    {
        _sessions = sessions;
        _query = query;
        _plans = plans;
        _executions = executions;
        _logger = logger;
    }

    public Task<IReadOnlyList<ScanSession>> GetSessionsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        _sessions.GetRecentAsync(limit, cancellationToken);

    /// <summary>
    /// The size of one root over time, oldest first so it reads left to right as a trend.
    /// Only completed scans count: a cancelled run stopped partway and its total would show up as
    /// a cliff that never happened.
    /// </summary>
    public async Task<IReadOnlyList<ScanSession>> GetTrendAsync(
        string rootPath,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var sessions = await _sessions.GetRecentAsync(200, cancellationToken).ConfigureAwait(false);

        return sessions
            .Where(s => s.Status == ScanStatus.Completed
                        && string.Equals(s.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.StartedAt)
            .Take(limit)
            .OrderBy(s => s.StartedAt)
            .ToList();
    }

    /// <summary>
    /// Compares two scans of the same root. Comparing different roots is refused rather than
    /// producing a diff where every folder looks added or removed.
    /// </summary>
    public async Task<Result<ScanComparison>> CompareAsync(
        string baselineSessionId,
        string currentSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);

        if (string.Equals(baselineSessionId, currentSessionId, StringComparison.Ordinal))
            return Result.Failure<ScanComparison>(ComparisonErrors.SameSession);

        var baseline = await _sessions.GetAsync(baselineSessionId, cancellationToken).ConfigureAwait(false);
        var current = await _sessions.GetAsync(currentSessionId, cancellationToken).ConfigureAwait(false);

        if (baseline is null || current is null)
            return Result.Failure<ScanComparison>(ComparisonErrors.SessionMissing);

        if (!string.Equals(baseline.RootPath, current.RootPath, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<ScanComparison>(ComparisonErrors.DifferentRoots);

        // Whichever ran first is the baseline, regardless of which the user picked first.
        if (baseline.StartedAt > current.StartedAt)
            (baseline, current) = (current, baseline);

        var baselineFolders = await _query
            .GetFolderSizesAsync(baseline.Id, ComparisonDepth, ComparisonRowLimit, cancellationToken)
            .ConfigureAwait(false);

        var currentFolders = await _query
            .GetFolderSizesAsync(current.Id, ComparisonDepth, ComparisonRowLimit, cancellationToken)
            .ConfigureAwait(false);

        var baselineCategories = await GetCategorySnapshotAsync(baseline.Id, cancellationToken).ConfigureAwait(false);
        var currentCategories = await GetCategorySnapshotAsync(current.Id, cancellationToken).ConfigureAwait(false);

        var comparison = ScanComparer.Compare(
            baseline, current, baselineFolders, currentFolders, baselineCategories, currentCategories);

        _logger.LogInformation("Compared two scans of {Root}: {Count} folders moved.",
            baseline.RootPath, comparison.Changes.Count);

        return Result.Success(comparison);
    }

    private async Task<IReadOnlyList<CategoryUsageSnapshot>> GetCategorySnapshotAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var usage = await _query.GetCategoryUsageAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return usage.Select(u => new CategoryUsageSnapshot(u.Category, u.TotalSize)).ToList();
    }

    /// <summary>Every run of a plan, newest first — the record of what was actually done to the disk.</summary>
    public Task<IReadOnlyList<PlanExecution>> GetExecutionsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default) =>
        _executions.GetRecentAsync(limit, cancellationToken);

    /// <summary>
    /// Removes a stored scan and everything derived from it.
    /// <para>
    /// The execution log is deliberately left behind. It records real changes to the user's files,
    /// which outlive the scan that suggested them — dropping it because a scan was tidied away
    /// would erase the only account of what Storava did.
    /// </para>
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await _plans.DeleteForSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await _sessions.DeleteAsync(sessionId, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("A stored scan and its derived data were deleted.");
    }
}
