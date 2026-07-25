using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Application.Services;

/// <summary>
/// Owns the lifecycle of a scan run: creates the session, drives the scanner into a sink,
/// and records final status/totals. The scanner itself never touches session state.
/// </summary>
public sealed class ScanCoordinator
{
    private readonly IDiskScanner _scanner;
    private readonly IScanItemSinkFactory _sinkFactory;
    private readonly IScanSessionRepository _sessions;
    private readonly IDatabaseInitializer _databaseInitializer;
    private readonly ILogger<ScanCoordinator> _logger;

    public ScanCoordinator(
        IDiskScanner scanner,
        IScanItemSinkFactory sinkFactory,
        IScanSessionRepository sessions,
        IDatabaseInitializer databaseInitializer,
        ILogger<ScanCoordinator> logger)
    {
        _scanner = scanner;
        _sinkFactory = sinkFactory;
        _sessions = sessions;
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

        var sink = _sinkFactory.Create(session.Id);
        var status = ScanStatus.Completed;
        ScanOutcome outcome = default;

        try
        {
            outcome = await _scanner
                .ScanAsync(request, session.Id, sink, progress, pauseToken, cancellationToken)
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

        session.Status = status;
        session.CompletedAt = DateTimeOffset.UtcNow;
        session.TotalSize = outcome.TotalSize;
        session.TotalFiles = (int)outcome.TotalFiles;
        session.TotalFolders = (int)outcome.TotalFolders;
        session.ErrorCount = outcome.ErrorCount;
        await _sessions.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);

        _logger.LogInformation(
            "Scan {SessionId} finished: {Status}, {Files} files, {Folders} folders, {Errors} errors.",
            session.Id, status, outcome.TotalFiles, outcome.TotalFolders, outcome.ErrorCount);

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
