using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.Planning;
using Storava.Domain.Entities;
using Storava.Domain.ValueObjects;

namespace Storava.App.ViewModels.Pages;

/// <summary>
/// Drives the Storage Plan page: pick recommendations, choose a permitted action for each, and
/// see the ordered result.
/// <para>
/// Nothing here executes anything. The page writes a document; every step still has to be carried
/// out deliberately in a later phase, and the banner on the page says so.
/// </para>
/// </summary>
public sealed partial class StoragePlanViewModel : ViewModelBase, IDisposable
{
    private readonly StoragePlanService _planning;
    private readonly IScanSessionRepository _sessions;
    private readonly ScanController _controller;
    private readonly ILocalizationService _localization;
    private readonly ILogger<StoragePlanViewModel> _logger;

    private StoragePlan? _plan;
    private string? _sessionId;

    [ObservableProperty] private bool _hasSession;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _stepCountText = "0";
    [ObservableProperty] private string _reclaimableText = "—";
    [ObservableProperty] private string _highestRiskText = "—";
    [ObservableProperty] private string _moveCountText = "0";
    [ObservableProperty] private string _deleteCountText = "0";
    [ObservableProperty] private double _goalGb;
    [ObservableProperty] private double _goalProgress;
    [ObservableProperty] private bool _hasGoal;
    [ObservableProperty] private bool _meetsGoal;
    [ObservableProperty] private bool _isSaved;
    [ObservableProperty] private string? _errorMessage;

    public StoragePlanViewModel(
        StoragePlanService planning,
        IScanSessionRepository sessions,
        ScanController controller,
        ILocalizationService localization,
        ILogger<StoragePlanViewModel> logger)
    {
        _planning = planning;
        _sessions = sessions;
        _controller = controller;
        _localization = localization;
        _logger = logger;

        _localization.LanguageChanged += OnLanguageChanged;
        _ = LoadAsync();
    }

    public ObservableCollection<PlanCandidateModel> Candidates { get; } = [];

    public ObservableCollection<PlanStepModel> Steps { get; } = [];

    public bool HasCandidates => Candidates.Count > 0;

    public bool HasSteps => Steps.Count > 0;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Every label on this page comes from the rule catalog in the active language.
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            _sessionId = await ResolveSessionIdAsync().ConfigureAwait(true);
            HasSession = _sessionId is not null;
            if (_sessionId is null)
                return;

            _plan = await _planning.LoadOrCreateAsync(_sessionId).ConfigureAwait(true);
            var candidates = await _planning.GetCandidatesAsync(_sessionId).ConfigureAwait(true);

            GoalGb = _plan.GoalBytes <= 0
                ? 0
                : Math.Round((double)_plan.GoalBytes / (1024 * 1024 * 1024), 1);

            BuildCandidates(candidates);
            RefreshSummary();
            IsSaved = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the storage plan failed.");
            ErrorMessage = _localization["Str.Plan.Error"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<string?> ResolveSessionIdAsync()
    {
        if (!string.IsNullOrEmpty(_controller.CurrentSessionId))
            return _controller.CurrentSessionId;

        var recent = await _sessions.GetRecentAsync(1).ConfigureAwait(true);
        return recent.Count > 0 ? recent[0].Id : null;
    }

    private void BuildCandidates(IReadOnlyList<Recommendation> recommendations)
    {
        foreach (var existing in Candidates)
            existing.PropertyChanged -= OnCandidateChanged;

        Candidates.Clear();

        var culture = _localization.CurrentCulture;
        foreach (var recommendation in recommendations)
        {
            var candidate = new PlanCandidateModel(recommendation, culture, _localization);

            // Reflect the saved plan without treating it as a fresh user choice.
            if (_plan?.FindByScanItem(recommendation.ScanItemId) is { } entry)
            {
                candidate.SuppressNotifications = true;
                candidate.IsIncluded = true;
                candidate.SelectedAction = candidate.AvailableActions
                    .FirstOrDefault(o => o.Action == entry.Action) ?? candidate.SelectedAction;
                candidate.SuppressNotifications = false;
            }

            candidate.PropertyChanged += OnCandidateChanged;
            Candidates.Add(candidate);
        }

        OnPropertyChanged(nameof(HasCandidates));
    }

    private void OnCandidateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PlanCandidateModel candidate || candidate.SuppressNotifications || _plan is null)
            return;

        if (e.PropertyName is not (nameof(PlanCandidateModel.IsIncluded) or nameof(PlanCandidateModel.SelectedAction)))
            return;

        ErrorMessage = null;

        // Always start from a clean slate for this item, so an action change is not a second entry.
        _planning.Exclude(_plan, candidate.ScanItemId);

        if (candidate.IsIncluded && candidate.SelectedAction is { } option)
        {
            var result = _planning.Include(_plan, candidate.Source, option.Action);
            if (result.IsFailure)
            {
                ErrorMessage = DescribeError(result.Error.Code);
                _logger.LogWarning("A plan step was refused: {Code}.", result.Error.Code);

                // The domain said no, so the checkbox must not stay ticked.
                candidate.SuppressNotifications = true;
                candidate.IsIncluded = false;
                candidate.SuppressNotifications = false;
            }
        }

        IsSaved = false;
        RefreshSummary();
    }

    private string DescribeError(string code) => _localization[code switch
    {
        "plan.protected_path" => "Str.Plan.Error.Protected",
        "plan.delete_not_permitted" => "Str.Plan.Error.DeleteNotPermitted",
        "plan.move_not_permitted" => "Str.Plan.Error.MoveNotPermitted",
        "plan.already_added" => "Str.Plan.Error.AlreadyAdded",
        "plan.not_plannable" => "Str.Plan.Error.NotPlannable",
        "plan.wrong_session" => "Str.Plan.Error.WrongSession",
        _ => "Str.Plan.Error"
    }];

    private void RefreshSummary()
    {
        if (_plan is null)
            return;

        var culture = _localization.CurrentCulture;

        StepCountText = _plan.Entries.Count.ToString(culture);
        MoveCountText = _plan.MoveCount.ToString(culture);
        DeleteCountText = _plan.DeleteCount.ToString(culture);
        ReclaimableText = new ByteSize(_plan.TotalReclaimable).Humanize(culture);
        HighestRiskText = _plan.Entries.Count == 0 ? "—" : _localization[$"Str.Risk.{_plan.HighestRisk}"];

        HasGoal = _plan.GoalBytes > 0;
        GoalProgress = _plan.GoalProgress;
        MeetsGoal = _plan.MeetsGoal;

        Steps.Clear();
        foreach (var entry in _plan.Entries.OrderBy(e => e.Order))
            Steps.Add(new PlanStepModel(entry, culture, _localization));

        OnPropertyChanged(nameof(HasSteps));
    }

    partial void OnGoalGbChanged(double value)
    {
        if (_plan is null)
            return;

        _plan.GoalBytes = value <= 0 ? 0 : (long)(value * 1024 * 1024 * 1024);
        IsSaved = false;
        RefreshSummary();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_plan is null)
            return;

        try
        {
            await _planning.SaveAsync(_plan).ConfigureAwait(true);
            IsSaved = true;
            RefreshSummary();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving the storage plan failed.");
            ErrorMessage = _localization["Str.Plan.Error"];
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (_plan is null || _sessionId is null)
            return;

        _plan.Clear();

        foreach (var candidate in Candidates)
        {
            candidate.SuppressNotifications = true;
            candidate.IsIncluded = false;
            candidate.SuppressNotifications = false;
        }

        await _planning.DiscardAsync(_sessionId).ConfigureAwait(true);
        IsSaved = false;
        ErrorMessage = null;
        RefreshSummary();
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        foreach (var candidate in Candidates)
            candidate.PropertyChanged -= OnCandidateChanged;
    }
}
