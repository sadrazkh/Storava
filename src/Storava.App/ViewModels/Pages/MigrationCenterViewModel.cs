using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.Migration;
using Storava.Application.Planning;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Domain.ValueObjects;
using Storava.Migrations;
using Storava.Migrations.Preflight;

namespace Storava.App.ViewModels.Pages;

/// <summary>
/// Drives the Migration Center: dry-run the saved plan, then carry it out one confirmed step at a
/// time.
/// <para>
/// This is the only page that can change the user's disk, and it is built so that no single click
/// ever does. A step runs when three separate things are true: it passed a dry run, the user chose
/// its destination, and the user typed the folder's own name. Typing that name is what produces the
/// approval, so changing the destination afterwards silently invalidates it — see
/// <see cref="OnDestinationParentChanged"/>.
/// </para>
/// </summary>
public sealed partial class MigrationCenterViewModel : ViewModelBase, IDisposable
{
    private readonly StoragePlanService _planning;
    private readonly PlanExecutionService _executor;
    private readonly IPlanExecutionRepository _executions;
    private readonly IScanSessionRepository _sessions;
    private readonly ScanController _controller;
    private readonly IFolderPicker _folderPicker;
    private readonly ILocalizationService _localization;
    private readonly ILogger<MigrationCenterViewModel> _logger;

    private StoragePlan? _plan;
    private PreflightReport? _preflight;
    private PlanExecution? _execution;
    private PlanExecutionStep? _step;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Captured the moment the typed name became correct. Execution is refused unless it still
    /// matches the step, which is what stops an approval outliving what it approved.
    /// </summary>
    private string? _approvedFingerprint;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private bool _isPreflighting;
    [ObservableProperty] private bool _hasPreflight;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _recoveryNotice;

    // Dry-run summary
    [ObservableProperty] private string _runnableCountText = "0";
    [ObservableProperty] private string _blockedCountText = "0";
    [ObservableProperty] private string _reclaimableText = "—";

    // The step awaiting a decision
    [ObservableProperty] private bool _hasCurrentStep;
    [ObservableProperty] private string _currentTitle = string.Empty;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private string _currentActionText = string.Empty;
    [ObservableProperty] private string _currentSizeText = "—";
    [ObservableProperty] private string _currentRiskText = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunStepCommand))]
    private bool _currentIsMove;

    [ObservableProperty] private string _currentPositionText = string.Empty;
    [ObservableProperty] private string _requiredConfirmationName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunStepCommand))]
    private string _confirmationText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunStepCommand))]
    private string? _destinationParent;

    [ObservableProperty] private string? _destinationPreview;

    // Live copy progress
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunStepCommand))]
    private bool _isExecuting;

    [ObservableProperty] private double _copyFraction;
    [ObservableProperty] private string _copyStatusText = string.Empty;

    // Outcome
    [ObservableProperty] private string _completedCountText = "0";
    [ObservableProperty] private string _failedCountText = "0";
    [ObservableProperty] private string _skippedCountText = "0";
    [ObservableProperty] private string _freedText = "—";

    public MigrationCenterViewModel(
        StoragePlanService planning,
        PlanExecutionService executor,
        IPlanExecutionRepository executions,
        IScanSessionRepository sessions,
        ScanController controller,
        IFolderPicker folderPicker,
        ILocalizationService localization,
        ILogger<MigrationCenterViewModel> logger)
    {
        _planning = planning;
        _executor = executor;
        _executions = executions;
        _sessions = sessions;
        _controller = controller;
        _folderPicker = folderPicker;
        _localization = localization;
        _logger = logger;

        _localization.LanguageChanged += OnLanguageChanged;
        _ = LoadAsync();
    }

    public ObservableCollection<MigrationPreflightModel> PreflightSteps { get; } = [];

    public ObservableCollection<MigrationLogModel> Log { get; } = [];

    public bool HasLog => Log.Count > 0;

    /// <summary>True once the user has typed the folder's name exactly.</summary>
    public bool IsNameConfirmed => !string.IsNullOrWhiteSpace(RequiredConfirmationName)
                                   && string.Equals(
                                       ConfirmationText?.Trim(),
                                       RequiredConfirmationName,
                                       StringComparison.OrdinalIgnoreCase);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Every label here comes from the rule catalog or the error map, both language-dependent.
        RefreshCurrentStepText();
        RebuildLog();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var sessionId = await ResolveSessionIdAsync().ConfigureAwait(true);
            if (sessionId is null)
                return;

            var stored = await _planning.LoadOrCreateAsync(sessionId).ConfigureAwait(true);
            _plan = stored.Entries.Count > 0 ? stored : null;
            HasPlan = _plan is not null;

            await CheckForInterruptedRunAsync(sessionId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the migration center failed.");
            ErrorMessage = _localization["Str.Migration.Error.Load"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// A step still marked as running means the app died mid-operation. It is settled from the disk
    /// before anything new is offered, so the user is never asked to act on top of a half-done move.
    /// </summary>
    private async Task CheckForInterruptedRunAsync(string sessionId)
    {
        var previous = await _executions.GetLatestForSessionAsync(sessionId).ConfigureAwait(true);
        if (previous?.StepNeedingRecovery is not { } interrupted)
            return;

        _logger.LogWarning("An interrupted plan step was found and is being settled.");
        await _executor.RecoverAsync(previous, interrupted).ConfigureAwait(true);

        RecoveryNotice = _localization[interrupted.Status switch
        {
            ExecutionStatus.Completed => "Str.Migration.Recovered.Completed",
            ExecutionStatus.RolledBack => "Str.Migration.Recovered.RolledBack",
            _ => "Str.Migration.Recovered.Failed"
        }];
    }

    /// <summary>
    /// The scan this page may act on. An imported scan is skipped on purpose: its paths were
    /// measured on another machine, and a path that happens to exist here too would name a folder
    /// this scan never looked at. Only a scan taken on this machine can drive a real move.
    /// </summary>
    private async Task<string?> ResolveSessionIdAsync()
    {
        if (!string.IsNullOrEmpty(_controller.CurrentSessionId))
            return _controller.CurrentSessionId;

        var recent = await _sessions.GetRecentAsync(RecentLookback).ConfigureAwait(true);
        return recent.FirstOrDefault(session => !session.IsImported)?.Id;
    }

    /// <summary>How far back to look for a scan measured here before giving up.</summary>
    private const int RecentLookback = 20;

    [RelayCommand]
    private async Task PreflightAsync()
    {
        if (_plan is null || IsPreflighting)
            return;

        IsPreflighting = true;
        ErrorMessage = null;

        try
        {
            // Walks every folder in the plan, so it belongs off the UI thread.
            var report = await Task.Run(() => _executor.PreflightAsync(_plan)).ConfigureAwait(true);
            _preflight = report;

            var culture = _localization.CurrentCulture;
            PreflightSteps.Clear();
            foreach (var result in report.Steps)
                PreflightSteps.Add(new MigrationPreflightModel(result, culture, _localization, Describe));

            RunnableCountText = report.RunnableCount.ToString(culture);
            BlockedCountText = report.BlockedCount.ToString(culture);
            ReclaimableText = new ByteSize(report.ReclaimableBytes).Humanize(culture);
            HasPreflight = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The dry run failed.");
            ErrorMessage = _localization["Str.Migration.Error.Preflight"];
        }
        finally
        {
            IsPreflighting = false;
        }
    }

    private bool CanStart => _plan is not null && _preflight is { HasAnythingToDo: true } && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_plan is null || _preflight is null)
            return;

        try
        {
            _execution = await _executor.CreateExecutionAsync(_plan, _preflight).ConfigureAwait(true);
            IsRunning = true;
            IsFinished = false;
            Log.Clear();
            OnPropertyChanged(nameof(HasLog));

            // Steps the dry run refused are already recorded as skipped; show them from the start.
            foreach (var skipped in _execution.Steps.Where(s => s.Status == ExecutionStatus.Skipped))
                AppendLog(skipped);

            Advance();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Starting the run failed.");
            ErrorMessage = _localization["Str.Migration.Error.Start"];
        }
    }

    [RelayCommand]
    private void PickDestination()
    {
        if (_step is null)
            return;

        var picked = _folderPicker.Pick(DestinationParent);
        if (picked is not null)
            DestinationParent = picked;
    }

    partial void OnDestinationParentChanged(string? value)
    {
        if (_step is null)
            return;

        // The folder keeps its own name at the new location, so the user picks a parent, not a
        // full path — picking the parent is what people expect and it cannot produce a nested mess.
        _step.DestinationPath = string.IsNullOrWhiteSpace(value)
            ? null
            : Path.Combine(value, ExecutionGuard.GetLeafName(_step.SourcePath));

        DestinationPreview = _step.DestinationPath;

        // Any approval was given for the old destination, so it no longer applies.
        ConfirmationText = string.Empty;
    }

    partial void OnConfirmationTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsNameConfirmed));

        // The approval is minted here, bound to the step exactly as it stands at this instant.
        _approvedFingerprint = IsNameConfirmed && _step is not null
            ? StepConfirmation.Compute(_step)
            : null;
    }

    private bool CanRunStep => _step is not null
                               && !IsExecuting
                               && IsNameConfirmed
                               && (!CurrentIsMove || !string.IsNullOrWhiteSpace(DestinationParent));

    [RelayCommand(CanExecute = nameof(CanRunStep))]
    private async Task RunStepAsync()
    {
        if (_execution is null || _step is null || _approvedFingerprint is null)
            return;

        var step = _step;
        var confirmation = new StepConfirmation
        {
            StepId = step.Id,
            Fingerprint = _approvedFingerprint,
            TypedName = ConfirmationText.Trim()
        };

        IsExecuting = true;
        ErrorMessage = null;
        CopyFraction = 0;
        CopyStatusText = _localization["Str.Migration.Working"];

        _cts = new CancellationTokenSource();
        var progress = new Progress<CopyProgress>(OnCopyProgress);

        try
        {
            var result = await Task
                .Run(() => _executor.ExecuteStepAsync(_execution, step, confirmation, progress, _cts.Token))
                .ConfigureAwait(true);

            if (result.IsFailure && !step.IsFinished)
            {
                // Refused before anything ran: the step stays pending so it can be corrected.
                ErrorMessage = Describe(result.Error.Code);
                return;
            }

            if (result.IsFailure)
                ErrorMessage = Describe(result.Error.Code);

            AppendLog(step);
            Advance();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Running a plan step failed unexpectedly.");
            ErrorMessage = _localization["Str.Migration.Error.Step"];
        }
        finally
        {
            IsExecuting = false;
            CopyFraction = 0;
            CopyStatusText = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnCopyProgress(CopyProgress progress)
    {
        CopyFraction = progress.Fraction;

        var culture = _localization.CurrentCulture;
        CopyStatusText = string.Format(
            culture,
            _localization["Str.Migration.Copying"],
            new ByteSize(progress.BytesCopied).Humanize(culture),
            new ByteSize(progress.TotalBytes).Humanize(culture));
    }

    /// <summary>Stops the copy in flight. The half-written copy is cleaned up by the executor.</summary>
    [RelayCommand]
    private void CancelStep() => _cts?.Cancel();

    [RelayCommand]
    private async Task SkipStepAsync()
    {
        if (_execution is null || _step is null || IsExecuting)
            return;

        var step = _step;
        await _executor.SkipAsync(_execution, step).ConfigureAwait(true);
        AppendLog(step);
        Advance();
    }

    /// <summary>Moves to the next pending step, or closes the run out when there is none.</summary>
    private void Advance()
    {
        if (_execution is null)
            return;

        _step = _execution.NextPending;
        _approvedFingerprint = null;
        ConfirmationText = string.Empty;
        DestinationParent = null;
        DestinationPreview = null;

        HasCurrentStep = _step is not null;
        RefreshCurrentStepText();
        RefreshTotals();

        if (_step is null)
        {
            IsRunning = false;
            IsFinished = true;
        }
    }

    private void RefreshCurrentStepText()
    {
        if (_step is null || _execution is null)
            return;

        var culture = _localization.CurrentCulture;

        CurrentTitle = _step.Title;
        CurrentPath = _step.SourcePath;
        CurrentActionText = _localization[$"Str.Plan.Action.{_step.Action}"];
        CurrentSizeText = new ByteSize(_step.MeasuredBytes).Humanize(culture);
        CurrentRiskText = _localization[$"Str.Migration.Method.{_step.Method}"];
        CurrentIsMove = _step.Action == SuggestedAction.Move;
        RequiredConfirmationName = ExecutionGuard.GetLeafName(_step.SourcePath);

        int done = _execution.Steps.Count(s => s.IsFinished) + 1;
        CurrentPositionText = string.Format(
            culture, _localization["Str.Migration.StepPosition"], done, _execution.Steps.Count);

        OnPropertyChanged(nameof(IsNameConfirmed));
    }

    private void RefreshTotals()
    {
        if (_execution is null)
            return;

        var culture = _localization.CurrentCulture;
        CompletedCountText = _execution.CompletedCount.ToString(culture);
        FailedCountText = _execution.FailedCount.ToString(culture);
        SkippedCountText = _execution.SkippedCount.ToString(culture);
        FreedText = new ByteSize(_execution.TotalBytesFreed).Humanize(culture);
    }

    private void AppendLog(PlanExecutionStep step)
    {
        Log.Add(new MigrationLogModel(step, _localization.CurrentCulture, _localization, Describe));
        OnPropertyChanged(nameof(HasLog));
    }

    private void RebuildLog()
    {
        if (_execution is null)
            return;

        var finished = _execution.Steps.Where(s => s.IsFinished).OrderBy(s => s.Order).ToList();
        Log.Clear();
        foreach (var step in finished)
            AppendLog(step);
    }

    /// <summary>Starts over from the dry run, so a plan can be re-run after fixing what blocked it.</summary>
    [RelayCommand]
    private async Task ResetAsync()
    {
        _execution = null;
        _step = null;
        _preflight = null;
        HasPreflight = false;
        IsRunning = false;
        IsFinished = false;
        HasCurrentStep = false;
        ErrorMessage = null;
        PreflightSteps.Clear();
        Log.Clear();
        OnPropertyChanged(nameof(HasLog));

        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Maps a stable error or warning code to the user's language. Codes are never shown raw: the
    /// user is told what happened, not which constant fired.
    /// </summary>
    private string Describe(string code) => _localization[code switch
    {
        "exec.protected_path" => "Str.Migration.Error.Protected",
        "exec.source_missing" => "Str.Migration.Error.SourceMissing",
        "exec.source_is_link" => "Str.Migration.Error.SourceIsLink",
        "exec.destination_required" => "Str.Migration.Error.DestinationRequired",
        "exec.destination_invalid" => "Str.Migration.Error.DestinationInvalid",
        "exec.destination_not_empty" => "Str.Migration.Error.DestinationNotEmpty",
        "exec.destination_inside_source" => "Str.Migration.Error.DestinationInsideSource",
        "exec.destination_same_volume" => "Str.Migration.Error.DestinationSameVolume",
        "exec.not_enough_space" => "Str.Migration.Error.NotEnoughSpace",
        "exec.action_not_permitted" => "Str.Migration.Error.NotPermitted",
        "exec.copy_failed" => "Str.Migration.Error.CopyFailed",
        "exec.verification_failed" => "Str.Migration.Error.VerificationFailed",
        "exec.recycle_failed" => "Str.Migration.Error.RecycleFailed",
        "exec.link_failed" => "Str.Migration.Error.LinkFailed",
        "exec.not_confirmed" => "Str.Migration.Error.NotConfirmed",
        "exec.confirmation_stale" => "Str.Migration.Error.Stale",
        "exec.another_running" => "Str.Migration.Error.AnotherRunning",
        "exec.not_pending" => "Str.Migration.Error.NotPending",
        "exec.cancelled" => "Str.Migration.Error.Cancelled",
        "preflight.grew" => "Str.Migration.Warn.Grew",
        "preflight.shrank" => "Str.Migration.Warn.Shrank",
        "preflight.no_official_method" => "Str.Migration.Warn.NoOfficialMethod",
        "preflight.high_risk" => "Str.Migration.Warn.HighRisk",
        "preflight.covered" => "Str.Migration.Warn.Covered",
        _ => "Str.Migration.Error.Unexpected"
    }];

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
