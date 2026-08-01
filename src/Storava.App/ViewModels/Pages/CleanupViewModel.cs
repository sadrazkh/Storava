using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.Migration;
using Storava.Application.Planning;
using Storava.Application.Scanning;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Domain.ValueObjects;
using Storava.Migrations;
using Storava.Migrations.Preflight;

namespace Storava.App.ViewModels.Pages;

/// <summary>
/// Choosing what to clear and carrying it out, on one page.
/// <para>
/// This replaces three: advice, the plan document, and the run. Splitting them was defensible on
/// paper — deciding and acting are different things — but in use it meant seven pages between
/// finishing a scan and freeing a byte, with a Save in the middle that looked like the end. People
/// stopped at the plan and never found the page that could act on it.
/// </para>
/// <para>
/// The safety did not come from the separation and is all still here: a dry run before anything,
/// then per step a fresh measurement, the folder's own name typed by hand, and removal only ever
/// to the Recycle Bin. What is gone is the ceremony, not the guards.
/// </para>
/// </summary>
public sealed partial class CleanupViewModel : ViewModelBase, IDisposable
{
    /// <summary>How many of the largest items to offer beyond what the catalog recognised.</summary>
    private const int BrowseLimit = 300;

    /// <summary>How far back to look for a scan taken on this machine before giving up.</summary>
    private const int RecentLookback = 20;

    private readonly StoragePlanService _planning;
    private readonly PlanExecutionService _executor;
    private readonly IPlanExecutionRepository _executions;
    private readonly IScanQueryService _query;
    private readonly IScanSessionRepository _sessions;
    private readonly ScanController _controller;
    private readonly IFolderPicker _folderPicker;
    private readonly ILocalizationService _localization;
    private readonly ILogger<CleanupViewModel> _logger;

    private string? _sessionId;
    private StoragePlan? _plan;
    private PreflightReport? _preflight;
    private PlanExecution? _execution;
    private PlanExecutionStep? _step;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Captured the moment the typed name became correct, and checked again at execution. This is
    /// what stops an approval outliving the thing it approved — change the destination and the
    /// fingerprint no longer matches.
    /// </summary>
    private string? _approvedFingerprint;

    /// <summary>
    /// Set while the scan picker is being refilled. Choosing an item programmatically is not the
    /// user choosing one, and without this the change handler would reload the page from inside
    /// the load that is already running.
    /// </summary>
    private bool _isRebuildingScans;

    /// <summary>
    /// Which phase is on screen.
    /// <para>
    /// Everything used to be visible at once, which meant a page of controls with no obvious place
    /// to start and no obvious place to click next. One thing at a time, with a single primary
    /// action anchored in the same spot, is the whole point of this.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosing))]
    [NotifyPropertyChangedFor(nameof(IsPickingDestination))]
    [NotifyPropertyChangedFor(nameof(IsActing))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private CleanupPhase _phase = CleanupPhase.Choose;

    partial void OnPhaseChanged(CleanupPhase value) => RebuildPhases();

    [ObservableProperty] private bool _hasSession;
    [ObservableProperty] private string _rootPathText = string.Empty;

    /// <summary>Which scan is being worked on. More than one is usually worth keeping around.</summary>
    [ObservableProperty] private CleanupScanOption? _selectedScan;

    [ObservableProperty] private bool _hasSeveralScans;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _recoveryNotice;

    // Choosing
    [ObservableProperty] private bool _suggestedOnly = true;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCountText = "0";
    [ObservableProperty] private string _selectedSizeText = "—";
    [ObservableProperty] private string _highestRiskText = "—";
    [ObservableProperty] private bool _hasSelection;

    // Where moves go
    [ObservableProperty] private string? _destinationRoot;

    [ObservableProperty] private bool _hasMoves;

    partial void OnHasMovesChanged(bool value) => RebuildPhases();

    // The run
    [ObservableProperty] private bool _isPreparing;
    [ObservableProperty] private bool _hasPreflight;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _runnableCountText = "0";
    [ObservableProperty] private string _blockedCountText = "0";
    [ObservableProperty] private string _reclaimableText = "—";

    [ObservableProperty] private bool _hasCurrentStep;
    [ObservableProperty] private CleanupStepModel? _currentStep;
    [ObservableProperty] private string? _destinationPreview;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunStepCommand))]
    private string _confirmationText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunStepCommand))]
    private string? _stepDestination;

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

    public CleanupViewModel(
        StoragePlanService planning,
        PlanExecutionService executor,
        IPlanExecutionRepository executions,
        IScanQueryService query,
        IScanSessionRepository sessions,
        ScanController controller,
        IFolderPicker folderPicker,
        ILocalizationService localization,
        ILogger<CleanupViewModel> logger)
    {
        _planning = planning;
        _executor = executor;
        _executions = executions;
        _query = query;
        _sessions = sessions;
        _controller = controller;
        _folderPicker = folderPicker;
        _localization = localization;
        _logger = logger;

        _localization.LanguageChanged += OnLanguageChanged;

        RebuildPhases();
        _ = LoadAsync();
    }

    public bool IsChoosing => Phase == CleanupPhase.Choose;

    public bool IsPickingDestination => Phase == CleanupPhase.Destination;

    public bool IsActing => Phase == CleanupPhase.Run;

    private void RebuildPhases()
    {
        var culture = _localization.CurrentCulture;
        int number = 1;

        Phases.Clear();
        Phases.Add(new CleanupPhaseChip(
            number++.ToString(culture), _localization["Str.Cleanup.Step.Choose"], IsChoosing));

        // Only when there is something to move. A phase that would be skipped is not shown, and
        // the numbering below it closes up rather than leaving a gap.
        if (HasMoves)
        {
            Phases.Add(new CleanupPhaseChip(
                number++.ToString(culture), _localization["Str.Cleanup.Step.Destination"], IsPickingDestination));
        }

        Phases.Add(new CleanupPhaseChip(
            number.ToString(culture), _localization["Str.Cleanup.Step.Run"], IsActing));
    }

    /// <summary>Back is offered until the first step has actually changed the disk.</summary>
    public bool CanGoBack => Phase != CleanupPhase.Choose && !IsRunning && !IsFinished;

    /// <summary>
    /// What the one primary button says. Named for the step it leads to rather than the step it is
    /// on, so the button always describes what is about to happen.
    /// </summary>
    public string PrimaryActionText => _localization[CleanupPhases.PrimaryKey(Phase)];

    /// <summary>
    /// True when the check ran and refused every step. Worth saying outright: the numbers alone
    /// leave the user looking at a button that will not respond and no reason why.
    /// </summary>
    public bool NothingCanRun => HasPreflight && !IsPreparing && _preflight is { HasAnythingToDo: false };

    /// <summary>
    /// The step strip. Rebuilt whenever the phase or the selection changes, because the middle
    /// phase only exists when something is being moved and the numbering has to follow.
    /// </summary>
    public ObservableCollection<CleanupPhaseChip> Phases { get; } = [];

    /// <summary>
    /// The risk levels present in this scan, as filters. Only levels that actually occur are
    /// offered — a filter that can only ever return nothing is not worth the room.
    /// </summary>
    public ObservableCollection<CleanupTagFilter> RiskFilters { get; } = [];

    /// <summary>Every scan of this machine that is still on record.</summary>
    public ObservableCollection<CleanupScanOption> Scans { get; } = [];

    /// <summary>Everything found, both proposed and merely measured.</summary>
    public ObservableCollection<CleanupItemModel> AllItems { get; } = [];

    /// <summary>What the filters currently let through. This is what the list binds to.</summary>
    public ObservableCollection<CleanupItemModel> VisibleItems { get; } = [];

    public ObservableCollection<MigrationPreflightModel> Blockers { get; } = [];

    public ObservableCollection<MigrationLogModel> Log { get; } = [];

    public bool HasLog => Log.Count > 0;

    public bool HasItems => VisibleItems.Count > 0;

    /// <summary>True once the approval word has been typed. See <see cref="ExecutionGuard.ApprovalWord"/>.</summary>
    public bool IsNameConfirmed => CurrentStep is { RequiredName.Length: > 0 } step
                                   && string.Equals(
                                       ConfirmationText?.Trim(),
                                       step.RequiredName,
                                       StringComparison.OrdinalIgnoreCase);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PrimaryActionText));
        RebuildPhases();
        _ = LoadAsync();
    }

    // --- choosing -------------------------------------------------------------------

    private async Task LoadAsync()
    {
        if (IsLoading)
            return;

        using var loading = BeginLoading("Str.Common.Loading.Scan");
        try
        {
            await BuildScanListAsync().ConfigureAwait(true);

            _sessionId = SelectedScan?.SessionId;
            HasSession = _sessionId is not null;
            if (_sessionId is null)
                return;

            var session = await _sessions.GetAsync(_sessionId).ConfigureAwait(true);
            RootPathText = session?.RootPath ?? string.Empty;

            _plan = await _planning.LoadOrCreateAsync(_sessionId).ConfigureAwait(true);
            _plan.Clear();

            await BuildItemsAsync().ConfigureAwait(true);
            await CheckForInterruptedRunAsync(_sessionId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the cleanup page failed.");
            ErrorMessage = _localization["Str.Cleanup.Error.Load"];
        }
    }

    /// <summary>
    /// Fills the scan picker and settles which one is being worked on.
    /// <para>
    /// Preserves the current choice across a reload, so changing the language or finishing a run
    /// does not silently move the user back to the newest scan and lose what they had selected.
    /// </para>
    /// </summary>
    private async Task BuildScanListAsync()
    {
        var culture = _localization.CurrentCulture;
        var recent = await _sessions.GetRecentAsync(RecentLookback).ConfigureAwait(true);

        string? wanted = SelectedScan?.SessionId
                         ?? (!string.IsNullOrEmpty(_controller.CurrentSessionId)
                             ? _controller.CurrentSessionId
                             : null);

        Scans.Clear();
        foreach (var session in recent.Where(session => !session.IsImported))
        {
            // The size is left out when the record has none rather than printed as "0 B". A scan
            // that was interrupted never wrote a total, and telling the user a drive holds nothing
            // is worse than telling them nothing about it.
            string when = session.StartedAt.ToLocalTime().ToString("g", culture);
            string label = session.TotalSize > 0
                ? string.Format(culture, "{0} — {1} — {2}",
                    session.RootPath, new ByteSize(session.TotalSize).Humanize(culture), when)
                : string.Format(culture, "{0} — {1}", session.RootPath, when);

            Scans.Add(new CleanupScanOption(session.Id, label));
        }

        HasSeveralScans = Scans.Count > 1;

        var chosen = Scans.FirstOrDefault(scan => string.Equals(scan.SessionId, wanted, StringComparison.Ordinal))
                     ?? Scans.FirstOrDefault();

        _isRebuildingScans = true;
        try
        {
            SelectedScan = chosen;
        }
        finally
        {
            _isRebuildingScans = false;
        }
    }

    partial void OnSelectedScanChanged(CleanupScanOption? value)
    {
        if (_isRebuildingScans || value is null)
            return;

        if (string.Equals(value.SessionId, _sessionId, StringComparison.Ordinal))
            return;

        // A different scan means a different plan, so nothing prepared for the old one survives.
        ResetRun();
        IsRunning = false;
        IsFinished = false;
        Log.Clear();
        OnPropertyChanged(nameof(HasLog));

        _ = LoadAsync();
    }

    /// <summary>
    /// Builds one list from both sources: what the catalog proposed, then the largest of everything
    /// else it did not recognise.
    /// </summary>
    private async Task BuildItemsAsync()
    {
        if (_sessionId is null)
            return;

        foreach (var existing in AllItems)
            existing.PropertyChanged -= OnItemChanged;

        AllItems.Clear();

        var culture = _localization.CurrentCulture;
        var advice = await _planning.GetCandidatesAsync(_sessionId).ConfigureAwait(true);

        // The catalog's advice and the AI's are kept apart on the way in: an item the AI also
        // commented on should gain a note, not a second row saying the same thing twice.
        var fromRules = advice.Where(item => item.Source != RecommendationSource.Ai).ToList();
        var fromAi = advice
            .Where(item => item.Source == RecommendationSource.Ai)
            .ToDictionary(item => item.ScanItemId, StringComparer.Ordinal);

        var byScanItem = new Dictionary<string, CleanupItemModel>(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var recommendation in fromRules)
        {
            var row = CleanupItemModel.FromAdvice(recommendation, culture, _localization);
            AllItems.Add(row);
            byScanItem[recommendation.ScanItemId] = row;
            covered.Add(recommendation.ScanItemId);
        }

        // Anything the AI raised that the catalog did not is a row in its own right.
        foreach (var (scanItemId, recommendation) in fromAi)
        {
            if (covered.Contains(scanItemId))
                continue;

            var row = CleanupItemModel.FromAdvice(recommendation, culture, _localization);
            AllItems.Add(row);
            byScanItem[scanItemId] = row;
            covered.Add(scanItemId);
        }

        var largest = await _query
            .GetLargestAsync(_sessionId, BrowseLimit, foldersOnly: false)
            .ConfigureAwait(true);

        foreach (var item in largest)
        {
            // Skip what advice already covers, so the same folder is never offered twice under
            // two different descriptions.
            if (covered.Contains(item.Id) || item.IsProtected || item.IsReparsePoint)
                continue;

            AllItems.Add(CleanupItemModel.FromItem(item, culture, _localization));
        }

        // The AI's opinion lands on whichever row the item ended up as, however it got there.
        foreach (var (scanItemId, recommendation) in fromAi)
        {
            if (byScanItem.TryGetValue(scanItemId, out var row))
                row.AttachAiNote(recommendation.Reason);
        }

        foreach (var model in AllItems)
            model.PropertyChanged += OnItemChanged;

        BuildRiskFilters();

        // Start on everything when the catalog recognised nothing here.
        //
        // Defaulting to suggestions-only and letting the page come up empty is the exact failure
        // this rewrite exists to end: a scan of a drive the catalog has no rules for would show a
        // blank page, and "there is nothing to clean" is not what that means. The filter is still
        // there and still defaults on wherever there is advice to filter to.
        if (!AllItems.Any(item => item.IsSuggested))
            SuggestedOnly = false;

        ApplyFilter();

        // Worth a line in the log: "the page is empty" has several causes — no scan, no items in
        // the scan, or a filter hiding them — and they are indistinguishable from a screenshot.
        _logger.LogInformation(
            "Cleanup loaded {Total} item(s) ({Suggested} suggested); {Visible} shown with suggestedOnly={Filter}.",
            AllItems.Count,
            AllItems.Count(item => item.IsSuggested),
            VisibleItems.Count,
            SuggestedOnly);
    }

    /// <summary>
    /// Builds one filter per risk level that occurs, ordered gentlest first so the safest things
    /// to clear are the easiest to reach.
    /// </summary>
    private void BuildRiskFilters()
    {
        foreach (var existing in RiskFilters)
            existing.PropertyChanged -= OnFilterChanged;

        RiskFilters.Clear();

        var order = new[] { RiskLevel.Low, RiskLevel.Medium, RiskLevel.Unknown, RiskLevel.High };

        foreach (var risk in order)
        {
            int count = AllItems.Count(item => item.Risk == risk);
            if (count == 0)
                continue;

            var filter = new CleanupTagFilter(risk, _localization[$"Str.Risk.{risk}"], count);
            filter.PropertyChanged += OnFilterChanged;
            RiskFilters.Add(filter);
        }

        OnPropertyChanged(nameof(HasRiskFilters));
    }

    public bool HasRiskFilters => RiskFilters.Count > 0;

    private void OnFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupTagFilter.IsSelected))
            ApplyFilter();
    }

    [RelayCommand]
    private void ClearRiskFilters()
    {
        foreach (var filter in RiskFilters)
            filter.IsSelected = false;
    }

    partial void OnSuggestedOnlyChanged(bool value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        // No risk chosen means no opinion, which shows everything. Narrowing to nothing at all
        // would be a filter nobody asked for.
        var risks = RiskFilters
            .Where(filter => filter.IsSelected)
            .Select(filter => filter.Risk)
            .ToHashSet();

        VisibleItems.Clear();
        foreach (var item in CleanupFilter.Apply(AllItems, SuggestedOnly, SearchText, risks))
            VisibleItems.Add(item);

        OnPropertyChanged(nameof(HasItems));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CleanupItemModel item || item.SuppressNotifications)
            return;

        if (e.PropertyName is not (nameof(CleanupItemModel.IsSelected) or nameof(CleanupItemModel.SelectedAction)))
            return;

        ErrorMessage = null;
        RebuildPlan();
    }

    /// <summary>
    /// Rewrites the plan from the current selection.
    /// <para>
    /// Rebuilt whole rather than patched: the plan de-duplicates nested steps and orders them
    /// safest-first, and both depend on the entire set. Patching one entry would leave the rest
    /// describing an arrangement that no longer exists.
    /// </para>
    /// </summary>
    private void RebuildPlan()
    {
        if (_plan is null || _sessionId is null)
            return;

        _plan.Clear();

        foreach (var model in AllItems.Where(item => item.IsSelected))
        {
            var result = model.Advice is { } advice
                ? _planning.Include(_plan, advice, model.Action, model.Method)
                : _planning.Include(_plan, model.Item!, _sessionId, model.Action, model.Method);

            if (result.IsFailure)
            {
                ErrorMessage = DescribePlanError(result.Error.Code);
                _logger.LogWarning("A cleanup step was refused: {Code}.", result.Error.Code);

                // The domain said no, so the tick must not stay.
                model.SuppressNotifications = true;
                model.IsSelected = false;
                model.SuppressNotifications = false;
            }
        }

        RefreshSelectionSummary();

        // Anything already prepared described the previous selection.
        if (HasPreflight && !IsRunning)
            ResetRun();
    }

    private void RefreshSelectionSummary()
    {
        if (_plan is null)
            return;

        var culture = _localization.CurrentCulture;

        SelectedCountText = _plan.Entries.Count.ToString(culture);
        SelectedSizeText = new ByteSize(_plan.TotalReclaimable).Humanize(culture);
        HighestRiskText = _plan.Entries.Count == 0
            ? "—"
            : _localization[$"Str.Risk.{_plan.HighestRisk}"];

        HasSelection = _plan.Entries.Count > 0;
        HasMoves = _plan.MoveCount > 0;

        NextCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Whether the primary button is on screen at all.
    /// <para>
    /// It steps aside once the run is under way, because from then on the decisions are per item
    /// and belong to the confirmation card. Hiding it for the whole of the last phase — which is
    /// what the first version of this layout did — left the run with no way to be started.
    /// </para>
    /// </summary>
    public bool IsPrimaryVisible => !IsRunning && !IsFinished;

    private bool CanGoNext => CleanupPhases.CanAdvance(
        Phase,
        HasSelection,
        HasMoves,
        !string.IsNullOrWhiteSpace(DestinationRoot),
        canRun: _preflight is { HasAnythingToDo: true } && !IsPreparing);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        // The last phase's primary action is to begin, not to advance.
        if (Phase == CleanupPhase.Run)
        {
            await StartAsync().ConfigureAwait(true);
            return;
        }

        Phase = CleanupPhases.Next(Phase, HasMoves);

        if (Phase == CleanupPhase.Run)
            await PrepareAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void Back()
    {
        if (IsRunning || IsFinished)
            return;

        Phase = CleanupPhases.Back(Phase, HasMoves);

        // Anything checked was checked against the selection as it was; going back is how a user
        // says they want to change it.
        ResetRun();
    }

    [RelayCommand]
    private void SelectSuggested()
    {
        foreach (var item in AllItems)
        {
            item.SuppressNotifications = true;
            item.IsSelected = item.IsSuggested;
            item.SuppressNotifications = false;
        }

        RebuildPlan();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in AllItems)
        {
            item.SuppressNotifications = true;
            item.IsSelected = false;
            item.SuppressNotifications = false;
        }

        RebuildPlan();
    }

    // --- where moves go -------------------------------------------------------------

    /// <summary>
    /// One folder for everything being moved. Each item keeps its own name underneath it, and
    /// <see cref="MoveDestinationPlanner"/> settles the collisions that produces — two folders both
    /// called <c>node_modules</c> cannot land in the same place.
    /// </summary>
    [RelayCommand]
    private void PickDestinationRoot()
    {
        var picked = _folderPicker.Pick(DestinationRoot);
        if (picked is not null)
            DestinationRoot = picked;
    }

    partial void OnDestinationRootChanged(string? value)
    {
        NextCommand.NotifyCanExecuteChanged();

        // A destination chosen for the whole run does not survive into a run already under way.
        if (HasPreflight && !IsRunning)
            ResetRun();
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsPrimaryVisible));
    }

    partial void OnIsFinishedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsPrimaryVisible));
    }

    partial void OnIsPreparingChanged(bool value)
    {
        OnPropertyChanged(nameof(NothingCanRun));
        NextCommand.NotifyCanExecuteChanged();
    }

    // --- the run --------------------------------------------------------------------

    private bool CanPrepare => HasSelection && !IsPreparing && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanPrepare))]
    private async Task PrepareAsync()
    {
        if (_plan is null || _sessionId is null)
            return;

        IsPreparing = true;
        ErrorMessage = null;

        try
        {
            // Saved before the run, not by a button. The plan is what the executor reads and what
            // an interrupted run is recovered against, so it has to be on disk — but asking the
            // user to press Save for that was exposing bookkeeping as a step.
            await _planning.SaveAsync(_plan).ConfigureAwait(true);

            // Walks every folder in the plan, so it belongs off the UI thread.
            var report = await Task.Run(() => _executor.PreflightAsync(_plan)).ConfigureAwait(true);
            _preflight = report;

            var culture = _localization.CurrentCulture;
            Blockers.Clear();
            foreach (var result in report.Steps.Where(step => !step.CanRun))
                Blockers.Add(new MigrationPreflightModel(result, culture, _localization, Describe));

            RunnableCountText = report.RunnableCount.ToString(culture);
            BlockedCountText = report.BlockedCount.ToString(culture);
            ReclaimableText = new ByteSize(report.ReclaimableBytes).Humanize(culture);
            HasPreflight = true;

            OnPropertyChanged(nameof(NothingCanRun));
            NextCommand.NotifyCanExecuteChanged();
            StartCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preparing the cleanup run failed.");
            ErrorMessage = _localization["Str.Cleanup.Error.Prepare"];
        }
        finally
        {
            IsPreparing = false;
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
            foreach (var skipped in _execution.Steps.Where(step => step.Status == ExecutionStatus.Skipped))
                AppendLog(skipped);

            Advance();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Starting the cleanup run failed.");
            ErrorMessage = _localization["Str.Cleanup.Error.Start"];
        }
    }

    /// <summary>Moves to the next pending step, or closes the run out when there is none.</summary>
    private void Advance()
    {
        if (_execution is null)
            return;

        _step = _execution.NextPending;
        _approvedFingerprint = null;
        ConfirmationText = string.Empty;

        HasCurrentStep = _step is not null;
        RefreshCurrentStep();
        RefreshTotals();

        if (_step is null)
        {
            IsRunning = false;
            IsFinished = true;
        }
    }

    private void RefreshCurrentStep()
    {
        if (_step is null || _execution is null)
        {
            CurrentStep = null;
            StepDestination = null;
            DestinationPreview = null;
            return;
        }

        var culture = _localization.CurrentCulture;
        int position = _execution.Steps.Count(step => step.IsFinished) + 1;

        CurrentStep = new CleanupStepModel(
            _step.Title,
            _step.SourcePath,
            _localization[$"Str.Plan.Action.{_step.Action}"],
            new ByteSize(_step.MeasuredBytes).Humanize(culture),
            DescribeMechanism(_step),
            _step.Action == SuggestedAction.Move,
            _step.HasNoRule,
            string.Format(culture, _localization["Str.Migration.StepPosition"], position, _execution.Steps.Count),
            ExecutionGuard.ApprovalWord);

        // A move inherits the destination chosen for the whole run; the user can still override it
        // for this one step before confirming.
        StepDestination = _step.Action == SuggestedAction.Move
            ? ResolveDestinationFor(_step)
            : null;

        OnPropertyChanged(nameof(IsNameConfirmed));
    }

    /// <summary>
    /// What will happen to the old path, in the user's words.
    /// <para>
    /// Keyed on the action as well as the mechanism. The mechanism alone is ambiguous: a move that
    /// leaves nothing behind and a delete both carry <see cref="MigrationMethod.None"/>, and the
    /// text for one is badly wrong for the other.
    /// </para>
    /// </summary>
    private string DescribeMechanism(PlanExecutionStep step) => _localization[
        step.Action != SuggestedAction.Move
            ? "Str.Migration.Method.None"
            : step.Method switch
            {
                MigrationMethod.Junction => "Str.Cleanup.Method.Junction",
                MigrationMethod.SymbolicLink => "Str.Cleanup.Method.SymbolicLink",
                MigrationMethod.OfficialSetting => "Str.Migration.Method.OfficialSetting",
                _ => "Str.Cleanup.Method.Plain"
            }];

    /// <summary>
    /// Where this step lands under the run's destination folder, avoiding anywhere an earlier step
    /// in the same run already claimed.
    /// </summary>
    private string? ResolveDestinationFor(PlanExecutionStep step)
    {
        if (string.IsNullOrWhiteSpace(DestinationRoot) || _execution is null)
            return null;

        var taken = new HashSet<string>(
            _execution.Steps
                .Where(other => !ReferenceEquals(other, step) && other.DestinationPath is { Length: > 0 })
                .Select(other => other.DestinationPath!),
            StringComparer.OrdinalIgnoreCase);

        return MoveDestinationPlanner.Resolve(DestinationRoot, step.SourcePath, taken);
    }

    [RelayCommand]
    private void PickStepDestination()
    {
        if (_step is null)
            return;

        var picked = _folderPicker.Pick(Path.GetDirectoryName(StepDestination) ?? DestinationRoot);
        if (picked is not null)
            StepDestination = Path.Combine(picked, ExecutionGuard.GetLeafName(_step.SourcePath));
    }

    partial void OnStepDestinationChanged(string? value)
    {
        if (_step is null)
            return;

        _step.DestinationPath = string.IsNullOrWhiteSpace(value) ? null : value;
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
                               && (CurrentStep?.IsMove != true || !string.IsNullOrWhiteSpace(StepDestination));

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
            _logger.LogError(ex, "Running a cleanup step failed unexpectedly.");
            ErrorMessage = _localization["Str.Cleanup.Error.Step"];
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

    private async Task CheckForInterruptedRunAsync(string sessionId)
    {
        var previous = await _executions.GetLatestForSessionAsync(sessionId).ConfigureAwait(true);
        if (previous?.StepNeedingRecovery is not { } interrupted)
            return;

        _logger.LogWarning("An interrupted cleanup step was found and is being settled.");
        await _executor.RecoverAsync(previous, interrupted).ConfigureAwait(true);

        RecoveryNotice = _localization[interrupted.Status switch
        {
            ExecutionStatus.Completed => "Str.Migration.Recovered.Completed",
            ExecutionStatus.RolledBack => "Str.Migration.Recovered.RolledBack",
            _ => "Str.Migration.Recovered.Failed"
        }];
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

    /// <summary>Clears anything prepared, so a changed selection is never run against a stale plan.</summary>
    private void ResetRun()
    {
        _preflight = null;
        _execution = null;
        _step = null;
        HasPreflight = false;
        HasCurrentStep = false;
        CurrentStep = null;
        Blockers.Clear();

        OnPropertyChanged(nameof(NothingCanRun));
        NextCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task StartOverAsync()
    {
        Phase = CleanupPhase.Choose;
        ResetRun();
        IsRunning = false;
        IsFinished = false;
        ErrorMessage = null;
        Log.Clear();
        OnPropertyChanged(nameof(HasLog));

        await LoadAsync().ConfigureAwait(true);
    }

    private string DescribePlanError(string code) => _localization[code switch
    {
        "plan.protected_path" => "Str.Plan.Error.Protected",
        "plan.delete_not_permitted" => "Str.Plan.Error.DeleteNotPermitted",
        "plan.move_not_permitted" => "Str.Plan.Error.MoveNotPermitted",
        "plan.already_added" => "Str.Plan.Error.AlreadyAdded",
        "plan.not_plannable" => "Str.Plan.Error.NotPlannable",
        "plan.wrong_session" => "Str.Plan.Error.WrongSession",
        "plan.is_link" => "Str.Plan.Error.IsLink",
        _ => "Str.Plan.Error"
    }];

    /// <summary>
    /// Maps a stable execution code to the user's language. Codes are never shown raw: the user is
    /// told what happened, not which constant fired.
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
        "exec.recycle_in_use" => "Str.Migration.Error.RecycleInUse",
        "exec.recycle_access_denied" => "Str.Migration.Error.RecycleAccessDenied",
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

        foreach (var item in AllItems)
            item.PropertyChanged -= OnItemChanged;

        foreach (var filter in RiskFilters)
            filter.PropertyChanged -= OnFilterChanged;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
