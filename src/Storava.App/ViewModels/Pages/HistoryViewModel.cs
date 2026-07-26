using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.History;
using Storava.Domain.ValueObjects;

namespace Storava.App.ViewModels.Pages;

/// <summary>
/// Drives the History page: past scans, how one folder's size has moved over time, the difference
/// between any two scans of it, and the record of every change Storava has made to the disk.
/// <para>
/// Deleting a stored scan removes the scan and its advice, never the execution log — that log is
/// the user's only account of what actually happened to their files.
/// </para>
/// </summary>
public sealed partial class HistoryViewModel : ViewModelBase, IDisposable
{
    private readonly ScanHistoryService _history;
    private readonly IDialogService _dialogs;
    private readonly ILocalizationService _localization;
    private readonly ILogger<HistoryViewModel> _logger;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasSessions;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private ScanHistoryModel? _baselineSelection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private ScanHistoryModel? _currentSelection;

    [ObservableProperty] private bool _hasComparison;
    [ObservableProperty] private string _comparisonDeltaText = "—";
    [ObservableProperty] private bool _comparisonGrew;
    [ObservableProperty] private string _comparisonBeforeText = "—";
    [ObservableProperty] private string _comparisonAfterText = "—";
    [ObservableProperty] private string _comparisonSpanText = string.Empty;
    [ObservableProperty] private bool _showNestedChanges;

    [ObservableProperty] private bool _hasTrend;
    [ObservableProperty] private string _trendRootPath = string.Empty;
    [ObservableProperty] private string _trendDeltaText = "—";

    public HistoryViewModel(
        ScanHistoryService history,
        IDialogService dialogs,
        ILocalizationService localization,
        ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _dialogs = dialogs;
        _localization = localization;
        _logger = logger;

        _localization.LanguageChanged += OnLanguageChanged;
        _ = LoadAsync();
    }

    public ObservableCollection<ScanHistoryModel> Sessions { get; } = [];

    public ObservableCollection<ScanHistoryModel> TrendPoints { get; } = [];

    public ObservableCollection<FolderChangeModel> Changes { get; } = [];

    public ObservableCollection<CategoryChangeModel> CategoryChanges { get; } = [];

    public ObservableCollection<ExecutionHistoryModel> Executions { get; } = [];

    public bool HasExecutions => Executions.Count > 0;

    public bool HasChanges => Changes.Count > 0;

    private ScanComparison? _comparison;

    private void OnLanguageChanged(object? sender, EventArgs e) => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var culture = _localization.CurrentCulture;

            var sessions = await _history.GetSessionsAsync().ConfigureAwait(true);
            Sessions.Clear();
            foreach (var session in sessions)
                Sessions.Add(new ScanHistoryModel(session, culture, _localization));

            HasSessions = Sessions.Count > 0;

            var executions = await _history.GetExecutionsAsync().ConfigureAwait(true);
            Executions.Clear();
            foreach (var execution in executions)
                Executions.Add(new ExecutionHistoryModel(execution, culture, _localization));

            OnPropertyChanged(nameof(HasExecutions));

            // Two scans of the same root are the pair worth comparing, so preselect the newest two.
            PreselectComparablePair();
            await RefreshTrendAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading history failed.");
            ErrorMessage = _localization["Str.History.Error.Load"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void PreselectComparablePair()
    {
        var pair = Sessions
            .GroupBy(s => s.RootPath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Max(s => s.Session.StartedAt))
            .FirstOrDefault();

        if (pair is null)
        {
            BaselineSelection = null;
            CurrentSelection = null;
            return;
        }

        var ordered = pair.OrderByDescending(s => s.Session.StartedAt).ToList();
        CurrentSelection = ordered[0];
        BaselineSelection = ordered[1];
    }

    private async Task RefreshTrendAsync()
    {
        TrendPoints.Clear();
        HasTrend = false;

        var root = CurrentSelection?.RootPath ?? Sessions.FirstOrDefault()?.RootPath;
        if (string.IsNullOrWhiteSpace(root))
            return;

        var culture = _localization.CurrentCulture;
        var trend = await _history.GetTrendAsync(root).ConfigureAwait(true);

        foreach (var session in trend)
            TrendPoints.Add(new ScanHistoryModel(session, culture, _localization));

        TrendRootPath = root;
        HasTrend = TrendPoints.Count >= 2;

        if (!HasTrend)
            return;

        // Bars are scaled against the largest point, so the shape of the series is readable even
        // when every scan is within a few percent of the others.
        long peak = trend.Max(s => s.TotalSize);
        foreach (var point in TrendPoints)
            point.TrendFraction = peak <= 0 ? 0 : (double)point.Session.TotalSize / peak;

        long delta = trend[^1].TotalSize - trend[0].TotalSize;
        TrendDeltaText = (delta >= 0 ? "+" : "−") + new ByteSize(Math.Abs(delta)).Humanize(culture);
    }

    private bool CanCompare => BaselineSelection is not null
                               && CurrentSelection is not null
                               && !string.Equals(BaselineSelection.Id, CurrentSelection.Id, StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        if (BaselineSelection is null || CurrentSelection is null)
            return;

        ErrorMessage = null;

        try
        {
            var result = await _history
                .CompareAsync(BaselineSelection.Id, CurrentSelection.Id)
                .ConfigureAwait(true);

            if (result.IsFailure)
            {
                ErrorMessage = Describe(result.Error.Code);
                HasComparison = false;
                return;
            }

            _comparison = result.Value;
            BuildComparison();
            await RefreshTrendAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Comparing two scans failed.");
            ErrorMessage = _localization["Str.History.Error.Compare"];
        }
    }

    private void BuildComparison()
    {
        if (_comparison is null)
            return;

        var culture = _localization.CurrentCulture;

        ComparisonBeforeText = new ByteSize(_comparison.BaselineBytes).Humanize(culture);
        ComparisonAfterText = new ByteSize(_comparison.CurrentBytes).Humanize(culture);
        ComparisonGrew = _comparison.Delta > 0;
        ComparisonDeltaText = (_comparison.Delta >= 0 ? "+" : "−")
                              + new ByteSize(Math.Abs(_comparison.Delta)).Humanize(culture);

        int days = Math.Max(0, (int)_comparison.Elapsed.TotalDays);
        ComparisonSpanText = string.Format(culture, _localization["Str.History.Span"], days);

        RefreshChangeList();

        CategoryChanges.Clear();
        foreach (var change in _comparison.CategoryChanges.Take(12))
            CategoryChanges.Add(new CategoryChangeModel(change, culture, _localization));

        HasComparison = true;
    }

    private void RefreshChangeList()
    {
        if (_comparison is null)
            return;

        var culture = _localization.CurrentCulture;

        // Nested rows repeat their ancestor's movement, so they are hidden unless asked for.
        var source = ShowNestedChanges ? _comparison.Changes : _comparison.Changes.Where(c => !c.HasChangedAncestor);

        Changes.Clear();
        foreach (var change in source.Take(60))
            Changes.Add(new FolderChangeModel(change, culture, _localization));

        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnShowNestedChangesChanged(bool value) => RefreshChangeList();

    partial void OnCurrentSelectionChanged(ScanHistoryModel? value) => _ = RefreshTrendAsync();

    [RelayCommand]
    private async Task DeleteSessionAsync(ScanHistoryModel? session)
    {
        if (session is null)
            return;

        bool confirmed = await _dialogs.ConfirmAsync(
            _localization["Str.History.Delete.Title"],
            string.Format(_localization.CurrentCulture, _localization["Str.History.Delete.Message"], session.Label),
            _localization["Str.Common.Delete"],
            _localization["Str.Common.Cancel"]).ConfigureAwait(true);

        if (!confirmed)
            return;

        try
        {
            await _history.DeleteSessionAsync(session.Id).ConfigureAwait(true);

            // The open comparison may have been built from the scan that just went away.
            HasComparison = false;
            _comparison = null;

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting a stored scan failed.");
            ErrorMessage = _localization["Str.History.Error.Delete"];
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private string Describe(string code) => _localization[code switch
    {
        "compare.same_session" => "Str.History.Error.SameSession",
        "compare.different_roots" => "Str.History.Error.DifferentRoots",
        "compare.session_missing" => "Str.History.Error.SessionMissing",
        _ => "Str.History.Error.Compare"
    }];

    public void Dispose() => _localization.LanguageChanged -= OnLanguageChanged;
}
