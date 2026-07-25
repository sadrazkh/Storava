using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Application.Services;
using Storava.Domain.Enums;
using Storava.Domain.ValueObjects;

namespace Storava.App.Services;

/// <summary>
/// UI-facing owner of the currently running scan. Runs the coordinator on a background thread,
/// marshals progress to the UI, and exposes pause/resume/cancel. Single instance for the app.
/// </summary>
public sealed partial class ScanController : ObservableObject
{
    private readonly ScanCoordinator _coordinator;
    private readonly ILocalizationService _localization;
    private readonly ILogger<ScanController> _logger;

    private PauseTokenSource? _pauseSource;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private long _filesScanned;
    [ObservableProperty] private long _foldersScanned;
    [ObservableProperty] private string _bytesText = "0 B";
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private string? _currentSessionId;
    [ObservableProperty] private ScanResult? _lastResult;

    public ScanController(
        ScanCoordinator coordinator,
        ILocalizationService localization,
        ILogger<ScanController> logger)
    {
        _coordinator = coordinator;
        _localization = localization;
        _logger = logger;
    }

    /// <summary>Raised on the UI thread when a scan run finishes (completed, cancelled or failed).</summary>
    public event EventHandler<ScanResult>? Completed;

    public async Task StartAsync(ScanRequest request)
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        _pauseSource = new PauseTokenSource();
        IsPaused = false;
        IsRunning = true;
        CurrentPath = string.Empty;
        FilesScanned = 0;
        FoldersScanned = 0;
        BytesText = "0 B";
        ErrorCount = 0;
        ElapsedText = "00:00";
        LastResult = null;

        var progress = new Progress<ScanProgress>(OnProgress);

        try
        {
            var result = await Task
                .Run(() => _coordinator.RunAsync(request, progress, _pauseSource.Token, _cts.Token))
                .ConfigureAwait(true);

            CurrentSessionId = result.SessionId;
            LastResult = result;
            Completed?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan run threw unexpectedly.");
            var failed = new ScanResult(CurrentSessionId ?? string.Empty, ScanStatus.Failed, 0, 0, 0, ErrorCount, TimeSpan.Zero);
            LastResult = failed;
            Completed?.Invoke(this, failed);
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
            _cts?.Dispose();
            _cts = null;
            _pauseSource = null;
        }
    }

    public void Pause()
    {
        _pauseSource?.Pause();
        IsPaused = true;
    }

    public void Resume()
    {
        _pauseSource?.Resume();
        IsPaused = false;
    }

    public void Cancel() => _cts?.Cancel();

    private void OnProgress(ScanProgress p)
    {
        CurrentPath = p.CurrentPath;
        FilesScanned = p.FilesScanned;
        FoldersScanned = p.FoldersScanned;
        BytesText = new ByteSize(p.BytesProcessed).Humanize(_localization.CurrentCulture);
        ErrorCount = p.ErrorCount;
        ElapsedText = p.Elapsed.ToString(p.Elapsed.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);
    }
}
