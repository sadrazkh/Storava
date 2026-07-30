using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;

namespace Storava.Application.History;

/// <summary>
/// Keeps only the most recent scans, so the local database stops growing without limit.
/// <para>
/// A scan of a whole drive is millions of rows. Nothing ever removed them, so six scans had grown
/// the database past six gigabytes — which is most of what "the application feels heavy" meant. A
/// scan is also the one kind of data here that can simply be taken again, which is what makes
/// discarding the old ones reasonable at all. The record of changes actually made to the user's
/// files is not a scan and is never touched by this.
/// </para>
/// </summary>
public sealed class ScanRetentionService
{
    /// <summary>Kept unless the user says otherwise: enough to compare against, few enough to stay small.</summary>
    public const int DefaultKeep = 3;

    /// <summary>How far back to look. Beyond this a scan is old enough that nothing wants it.</summary>
    private const int Lookback = 500;

    private readonly IScanSessionRepository _sessions;
    private readonly IPlanExecutionRepository _executions;
    private readonly ILogger<ScanRetentionService> _logger;

    public ScanRetentionService(
        IScanSessionRepository sessions,
        IPlanExecutionRepository executions,
        ILogger<ScanRetentionService> logger)
    {
        _sessions = sessions;
        _executions = executions;
        _logger = logger;
    }

    /// <summary>
    /// Removes scans beyond the most recent <paramref name="keep"/>.
    /// <para>
    /// Deleting is all this does. It does not compact the file afterwards, though it used to: SQLite
    /// rewrites the whole database under an exclusive lock to do that, and measuring it showed every
    /// page load issued meanwhile waiting for the entire rewrite — around half a minute on a
    /// database of the size this was written to deal with, arriving the moment a scan finishes and
    /// somebody goes to look at the results. The pages the deletes free are reused, so the file
    /// stops growing either way; giving them back to the operating system is
    /// <see cref="IDatabaseMaintenance.CompactAsync"/>, which is something a person asks for.
    /// </para>
    /// </summary>
    /// <param name="keep">How many to keep. Below one is treated as one: never delete everything.</param>
    /// <param name="protectedSessionId">
    /// A scan that must survive whatever its age — the one being viewed or just finished. Deleting
    /// the session a page is reading would empty that page underneath the user.
    /// </param>
    /// <returns>Which scans were discarded, so a caller can say so rather than guess.</returns>
    public async Task<RetentionResult> ApplyAsync(
        int keep,
        string? protectedSessionId = null,
        CancellationToken cancellationToken = default)
    {
        keep = Math.Max(1, keep);

        var recent = await _sessions.GetRecentAsync(Lookback, cancellationToken).ConfigureAwait(false);
        if (recent.Count <= keep)
            return RetentionResult.Empty;

        var removed = new List<string>();

        foreach (var session in recent.Skip(keep))
        {
            if (string.Equals(session.Id, protectedSessionId, StringComparison.Ordinal))
                continue;

            if (!await CanRemoveAsync(session.Id, cancellationToken).ConfigureAwait(false))
                continue;

            await _sessions.DeleteAsync(session.Id, cancellationToken).ConfigureAwait(false);
            removed.Add(session.Id);
        }

        if (removed.Count == 0)
            return RetentionResult.Empty;

        _logger.LogInformation(
            "Removed {Count} old scan(s), keeping the most recent {Keep}.", removed.Count, keep);

        return new RetentionResult(removed);
    }

    /// <summary>
    /// A scan whose run against it never finished has to stay. The steps of a run are settled
    /// against the scan that produced them, so removing the scan would leave a half-done move —
    /// a file already copied but not yet linked — with nothing left to reconcile it against.
    /// </summary>
    private async Task<bool> CanRemoveAsync(string sessionId, CancellationToken cancellationToken)
    {
        var execution = await _executions
            .GetLatestForSessionAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (execution is null || execution.Steps.Count == 0)
            return true;

        if (execution.IsFinished && execution.StepNeedingRecovery is null)
            return true;

        _logger.LogInformation(
            "Kept an old scan: a run against it was interrupted and has not been settled yet.");
        return false;
    }
}

/// <summary>What retention did, so a caller can tell the user rather than guess.</summary>
/// <param name="RemovedSessionIds">The scans that were discarded, oldest last.</param>
public sealed record RetentionResult(IReadOnlyList<string> RemovedSessionIds)
{
    public static readonly RetentionResult Empty = new([]);

    public int RemovedCount => RemovedSessionIds.Count;

    public bool RemovedAnything => RemovedSessionIds.Count > 0;
}
