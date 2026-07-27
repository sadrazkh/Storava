using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
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
    private readonly ILogger<ScanCoordinator> _logger;

    public ScanCoordinator(
        IDiskScanner scanner,
        IScanItemSinkFactory sinkFactory,
        IScanSessionRepository sessions,
        IScanQueryService query,
        IDatabaseInitializer databaseInitializer,
        ILogger<ScanCoordinator> logger)
    {
        _scanner = scanner;
        _sinkFactory = sinkFactory;
        _sessions = sessions;
        _query = query;
        _databaseInitializer = databaseInitializer;
        _logger = logger;
    }

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

        return new ScanResult(
            session.Id,
            status,
            outcome.TotalSize,
            outcome.TotalFiles,
            outcome.TotalFolders,
            outcome.ErrorCount,
            session.Duration ?? TimeSpan.Zero);
    }
}
