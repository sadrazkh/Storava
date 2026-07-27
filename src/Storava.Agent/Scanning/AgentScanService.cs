using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Application.Services;
using Storava.Contracts.Agent;
using Storava.Domain.Enums;

namespace Storava.Agent.Scanning;

/// <summary>
/// Runs walks of this machine on behalf of the page, using the same scanner, the same rule
/// catalog and the same storage the desktop application uses. None of that was rewritten for the
/// Agent — it is the same code, given a different caller.
/// <para>
/// One walk at a time. A second request while one is running is refused rather than queued: two
/// concurrent walks of the same disk are slower than one, and a page that could start them without
/// limit could be made to thrash the machine.
/// </para>
/// </summary>
public sealed class AgentScanService(
    ScanCoordinator coordinator,
    IScanQueryService query,
    ILogger<AgentScanService> logger)
{
    /// <summary>Bounded so a page cannot ask for a million rows in one response.</summary>
    public const int MaximumItems = 500;

    private readonly ConcurrentDictionary<string, ScanRun> _runs = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _startGate = new(1, 1);

    private ScanRun? _current;

    public sealed record StartResult(AgentScanProgress? Progress, AgentProblem? Problem)
    {
        public static StartResult Refused(string reason, string message) =>
            new(null, new AgentProblem(reason, message));
    }

    public async Task<StartResult> StartAsync(AgentScanRequest request)
    {
        if (!TryResolveRoot(request.RootPath, out string rootPath, out var problem))
            return StartResult.Refused(problem!.Reason, problem.Message);

        await _startGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_current is { IsFinished: false })
            {
                return StartResult.Refused(
                    "already_scanning",
                    "This agent is already walking a folder. Wait for it to finish, or cancel it.");
            }

            var run = new ScanRun(rootPath, ParseMode(request.Mode));
            _current = run;
            _runs[run.Id] = run;

            // Deliberately not awaited: the walk outlives the request that asked for it, and the
            // page follows it by polling.
            run.Task = Task.Run(() => ExecuteAsync(run));

            logger.LogInformation("Started a scan of {Root}.", rootPath);
            return new StartResult(run.Snapshot(), null);
        }
        finally
        {
            _startGate.Release();
        }
    }

    public AgentScanProgress? Get(string scanId) =>
        _runs.TryGetValue(scanId, out var run) ? run.Snapshot() : null;

    public bool Cancel(string scanId)
    {
        if (!_runs.TryGetValue(scanId, out var run) || run.IsFinished)
            return false;

        run.Cancellation.Cancel();
        return true;
    }

    /// <summary>
    /// The largest items the walk stored, with their real paths. Only readable once the walk has
    /// finished: a partial tree has folder rows that have not been totalled yet, and reporting
    /// those as sizes would be wrong rather than merely incomplete.
    /// </summary>
    public async Task<AgentScanItems?> ItemsAsync(
        string scanId,
        int limit,
        bool foldersOnly,
        CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(scanId, out var run) || run.State != AgentScanState.Completed)
            return null;

        // Queried by the session the coordinator created, not by the id the page holds. The two
        // are different on purpose: the page's id is stable from the moment the walk starts, while
        // the session only exists once the coordinator has made it.
        if (run.SessionId is not { Length: > 0 } sessionId)
            return null;

        var items = await query
            .GetLargestAsync(sessionId, Math.Clamp(limit, 1, MaximumItems), foldersOnly, cancellationToken)
            .ConfigureAwait(false);

        return new AgentScanItems(scanId, items.Select(item => new AgentScanItem(
            item.Id,
            item.Path,
            item.Name,
            item.IsFolder,
            item.Size,
            item.FileCount,
            item.FolderCount,
            item.Category.ToString(),
            item.DetectedTechnology,
            item.KnownRuleId,
            item.RiskLevel.ToString(),
            item.IsProtected,
            item.IsReparsePoint)).ToList());
    }

    private async Task ExecuteAsync(ScanRun run)
    {
        try
        {
            var result = await coordinator.RunAsync(
                new ScanRequest { RootPath = run.RootPath, Mode = run.Mode },
                new Progress<ScanProgress>(run.Report),
                new PauseTokenSource().Token,
                run.Cancellation.Token).ConfigureAwait(false);

            run.Finish(
                result.Status switch
                {
                    ScanStatus.Completed => AgentScanState.Completed,
                    ScanStatus.Cancelled => AgentScanState.Cancelled,
                    _ => AgentScanState.Failed
                },
                result.SessionId);

            logger.LogInformation(
                "Scan of {Root} finished: {State}, {Files} files, {Folders} folders.",
                run.RootPath, run.State, run.Files, run.Folders);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "A scan of {Root} failed.", run.RootPath);
            run.Fail(exception.Message);
        }
    }

    /// <summary>
    /// The one place a path from outside this process is accepted. It has to exist and be
    /// absolute; everything else — reparse loops, unreadable folders, protected locations — is
    /// already the scanner's business and unchanged by the caller being a page.
    /// </summary>
    private static bool TryResolveRoot(string? candidate, out string rootPath, out AgentProblem? problem)
    {
        rootPath = string.Empty;
        problem = null;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            problem = new AgentProblem("no_path", "No folder was given.");
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(candidate.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problem = new AgentProblem("bad_path", "That is not a usable path on this computer.");
            return false;
        }

        if (!Path.IsPathFullyQualified(full))
        {
            problem = new AgentProblem("bad_path", "The folder must be given as a full path.");
            return false;
        }

        if (!Directory.Exists(full))
        {
            problem = new AgentProblem("not_found", "There is no folder at that path on this computer.");
            return false;
        }

        rootPath = full;
        return true;
    }

    private static ScanMode ParseMode(string? mode) =>
        string.Equals(mode, "deep", StringComparison.OrdinalIgnoreCase) ? ScanMode.Deep : ScanMode.Quick;

    /// <summary>One walk, and everything the page can learn about it while it runs.</summary>
    private sealed class ScanRun(string rootPath, ScanMode mode)
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly Lock _gate = new();

        private string _currentPath = rootPath;
        private long _files;
        private long _folders;
        private long _bytes;
        private int _errors;
        private string? _error;

        /// <summary>Stable from the moment the walk starts; this is what the page polls by.</summary>
        public string Id { get; } = Guid.NewGuid().ToString("N");

        /// <summary>The stored scan, known only once the coordinator has finished making it.</summary>
        public string? SessionId { get; private set; }

        public string RootPath { get; } = rootPath;

        public ScanMode Mode { get; } = mode;

        public CancellationTokenSource Cancellation { get; } = new();

        public Task? Task { get; set; }

        public AgentScanState State { get; private set; } = AgentScanState.Running;

        public bool IsFinished => State != AgentScanState.Running;

        public long Files { get { lock (_gate) return _files; } }

        public long Folders { get { lock (_gate) return _folders; } }

        public void Report(ScanProgress progress)
        {
            lock (_gate)
            {
                _currentPath = progress.CurrentPath;
                _files = progress.FilesScanned;
                _folders = progress.FoldersScanned;
                _bytes = progress.BytesProcessed;
                _errors = progress.ErrorCount;
            }
        }

        public void Finish(AgentScanState state, string sessionId)
        {
            lock (_gate)
            {
                State = state;
                SessionId = sessionId;
                _stopwatch.Stop();
            }
        }

        public void Fail(string message)
        {
            lock (_gate)
            {
                State = AgentScanState.Failed;
                _error = message;
                _stopwatch.Stop();
            }
        }

        public AgentScanProgress Snapshot()
        {
            lock (_gate)
            {
                return new AgentScanProgress(
                    Id,
                    State,
                    RootPath,
                    _currentPath,
                    _files,
                    _folders,
                    _bytes,
                    _errors,
                    Math.Round(_stopwatch.Elapsed.TotalSeconds, 1),
                    _error);
            }
        }
    }
}
