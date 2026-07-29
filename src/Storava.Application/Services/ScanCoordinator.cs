using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.History;
using Storava.Application.Scanning;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Application.Services;

/// <summary>
/// Owns the lifecycle of a scan run: creates the session, drives the scanner into a sink,
/// and records final status/totals. The scanner itself never touches session state.
/// <para>
/// A run that stops early leaves its unfinished folders on the session as resume state, and
/// <see cref="ResumeAsync"/> picks the same session up again rather than starting a second one.
/// </para>
/// </summary>
public sealed class ScanCoordinator
{
    private readonly IDiskScanner _scanner;
    private readonly IScanItemSinkFactory _sinkFactory;
    private readonly IScanSessionRepository _sessions;
    private readonly IScanQueryService _query;
    private readonly IDatabaseInitializer _databaseInitializer;
    private readonly ScanRetentionService _retention;
    private readonly ISettingsService _settings;
    private readonly ILogger<ScanCoordinator> _logger;

    public ScanCoordinator(
        IDiskScanner scanner,
        IScanItemSinkFactory sinkFactory,
        IScanSessionRepository sessions,
        IScanQueryService query,
        IDatabaseInitializer databaseInitializer,
        ScanRetentionService retention,
        ISettingsService settings,
        ILogger<ScanCoordinator> logger)
    {
        _scanner = scanner;
        _sinkFactory = sinkFactory;
        _sessions = sessions;
        _query = query;
        _databaseInitializer = databaseInitializer;
        _retention = retention;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// The housekeeping pass left running by the most recent scan; already completed when none is.
    /// <para>
    /// Exposed so that shutdown and tests can wait for it deliberately. Nothing in the scan itself
    /// waits: it exists to make a detached task observable rather than to gate anything.
    /// </para>
    /// </summary>
    public Task RetentionInProgress { get; private set; } = Task.CompletedTask;

    public async Task<ScanResult> RunAsync(
        ScanRequest request,
        IProgress<ScanProgress> progress,
        PauseToken pauseToken,
        CancellationToken cancellationToken)
    {
        // Setup runs uncancellable: schema DDL and the session row must not be torn down
        // half-way. Cancellation is honored by the scan itself and reported as Cancelled.
        await _databaseInitializer.EnsureCreatedAsync(CancellationToken.None).ConfigureAwait(false);

        var session = new ScanSession
        {
            Id = Guid.NewGuid().ToString("N"),
            RootPath = request.RootPath,
            Label = request.Label,
            Mode = request.Mode,
            Status = ScanStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        await _sessions.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation("Scan {SessionId} started for {Root} ({Mode}).", session.Id, request.RootPath, request.Mode);

        return await ExecuteAsync(session, request, resume: null, progress, pauseToken, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Carries on a scan that stopped early, into the session it already has. Returns null when
    /// there is nothing to carry on — the scan finished, was never resumable, or its stored state
    /// cannot be read by this build.
    /// </summary>
    public async Task<ScanResult?> ResumeAsync(
        string sessionId,
        IProgress<ScanProgress> progress,
        PauseToken pauseToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await _databaseInitializer.EnsureCreatedAsync(CancellationToken.None).ConfigureAwait(false);

        var session = await _sessions.GetAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
        if (session is null || !session.CanResume)
            return null;

        var resume = ScanResumeState.Deserialize(session.ResumeState);
        if (resume is null || !resume.HasWork)
        {
            // The state is unusable, so the scan is not resumable however it was labelled.
            session.ResumeState = null;
            await _sessions.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            return null;
        }

        await LoadStoredChildrenAsync(session.Id, resume, CancellationToken.None).ConfigureAwait(false);

        var request = new ScanRequest
        {
            RootPath = session.RootPath,
            Mode = session.Mode,
            Label = session.Label,
            ExcludedPaths = resume.ExcludedPaths,
            ExcludedExtensions = resume.ExcludedExtensions
        };

        session.Status = ScanStatus.Running;
        session.CompletedAt = null;
        await _sessions.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);

        _logger.LogInformation(
            "Scan {SessionId} resuming with {Pending} unfinished folder(s).", session.Id, resume.Pending.Count);

        return await ExecuteAsync(session, request, resume, progress, pauseToken, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Tells each unfinished folder which of its children are already in the database, so the
    /// resumed walk skips them instead of measuring them a second time.
    /// <para>
    /// The pending folders are a single path from the root down, so this is one query per level of
    /// depth — a handful — rather than one per folder in the tree.
    /// </para>
    /// </summary>
    private async Task LoadStoredChildrenAsync(
        string sessionId, ScanResumeState resume, CancellationToken cancellationToken)
    {
        foreach (var folder in resume.Pending)
        {
            var stored = await _query.GetChildrenAsync(sessionId, folder.Id, cancellationToken).ConfigureAwait(false);
            foreach (var child in stored)
                folder.CompletedChildren.Add(child.Name);
        }
    }

    private async Task<ScanResult> ExecuteAsync(
        ScanSession session,
        ScanRequest request,
        ScanResumeState? resume,
        IProgress<ScanProgress> progress,
        PauseToken pauseToken,
        CancellationToken cancellationToken)
    {
        var sink = _sinkFactory.Create(session.Id);
        var resumePoint = new ScanResumePoint { Resume = resume };
        var status = ScanStatus.Completed;
        ScanOutcome outcome = default;

        try
        {
            outcome = await _scanner
                .ScanAsync(request, session.Id, sink, progress, pauseToken, resumePoint, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            status = ScanStatus.Cancelled;
            _logger.LogInformation("Scan {SessionId} cancelled.", session.Id);
        }
        catch (Exception ex)
        {
            status = ScanStatus.Failed;
            _logger.LogError(ex, "Scan {SessionId} failed.", session.Id);
        }
        finally
        {
            try
            {
                await sink.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush scan sink for {SessionId}.", session.Id);
            }

            await sink.DisposeAsync().ConfigureAwait(false);
        }

        // The scanner reports its running totals through the resume point even when it left by
        // throwing, so an interrupted run records what it did measure rather than zeroes.
        var pending = resumePoint.Pending;
        if (status != ScanStatus.Completed && pending is not null)
        {
            outcome = new ScanOutcome(
                pending.BytesScanned, pending.FilesScanned, pending.FoldersScanned, pending.ErrorCount);
        }

        session.Status = status;
        session.CompletedAt = DateTimeOffset.UtcNow;
        session.TotalSize = outcome.TotalSize;
        session.TotalFiles = (int)outcome.TotalFiles;
        session.TotalFolders = (int)outcome.TotalFolders;
        session.ErrorCount = outcome.ErrorCount;
        // Kept only while there is genuinely something left to walk; a finished scan carries none,
        // so nothing can offer to resume a scan that is already complete.
        session.ResumeState = status == ScanStatus.Completed || pending is null ? null : pending.Serialize();
        await _sessions.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);

        _logger.LogInformation(
            "Scan {SessionId} finished: {Status}, {Files} files, {Folders} folders, {Errors} errors, resumable: {Resumable}.",
            session.Id, status, outcome.TotalFiles, outcome.TotalFolders, outcome.ErrorCount,
            session.ResumeState is not null);

        if (status == ScanStatus.Completed)
            StartRetention(session.Id);

        return new ScanResult(
            session.Id,
            status,
            outcome.TotalSize,
            outcome.TotalFiles,
            outcome.TotalFolders,
            outcome.ErrorCount,
            session.Duration ?? TimeSpan.Zero);
    }

    /// <summary>
    /// Discards the scans that fell outside the keep count, now that a newer one exists.
    /// <para>
    /// Started rather than awaited, on purpose. Measured on a database of realistic size, removing
    /// one old scan takes about seven seconds and compacting the file about ten; a scan that had
    /// to wait for that would report itself finished and then sit there, which is the behaviour
    /// this release is trying to get rid of. Nothing the user is waiting for depends on it.
    /// </para>
    /// <para>
    /// Uncancellable and unable to throw, for the same reason: the scan is already saved, and
    /// housekeeping afterwards must not be able to turn a finished scan into a failure. An
    /// interrupted pass is safe to leave — each scan is removed in its own statement, and SQLite's
    /// compaction is a transaction that either lands or does not.
    /// </para>
    /// </summary>
    private void StartRetention(string justFinishedSessionId)
    {
        RetentionInProgress = Task.Run(async () =>
        {
            try
            {
                await _retention
                    .ApplyAsync(_settings.Current.KeepRecentScans, justFinishedSessionId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not apply scan retention after {SessionId}.", justFinishedSessionId);
            }
        });
    }
}
