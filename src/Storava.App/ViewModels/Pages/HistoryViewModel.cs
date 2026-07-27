using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.History;
using Storava.Contracts.Workspace;
using Storava.Domain.ValueObjects;

namespace Storava.App.ViewModels.Pages;

/// <summary>
/// Drives the History page: past scans, how one folder's size has moved over time, the difference
/// between any two scans of it, the record of every change Storava has made to the disk, and the
/// <c>.storava</c> archives that carry a scan to another machine.
/// <para>
/// Deleting a stored scan removes the scan and its advice, never the execution log — that log is
/// the user's only account of what actually happened to their files.
/// </para>
/// </summary>
public sealed partial class HistoryViewModel : ViewModelBase, IDisposable
{
    private readonly ScanHistoryService _history;
    private readonly IWorkspaceArchiveService _archives;
    private readonly IScanSessionRepository _sessions;
    private readonly IFileSaver _fileSaver;
    private readonly IFileOpener _fileOpener;
    private readonly IDialogService _dialogs;
    private readonly ILocalizationService _localization;
    private readonly ScanController _controller;
    private readonly INavigationService _navigation;
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportSessionCommand))]
    private bool _isArchiveBusy;

    [ObservableProperty] private string? _archiveStatus;
    [ObservableProperty] private string? _archiveProgressText;

    public HistoryViewModel(
        ScanHistoryService history,
        IWorkspaceArchiveService archives,
        IScanSessionRepository sessions,
        IFileSaver fileSaver,
        IFileOpener fileOpener,
        IDialogService dialogs,
        ILocalizationService localization,
        ScanController controller,
        INavigationService navigation,
        ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _archives = archives;
        _sessions = sessions;
        _fileSaver = fileSaver;
        _fileOpener = fileOpener;
        _dialogs = dialogs;
        _localization = localization;
        _controller = controller;
        _navigation = navigation;
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

    /// <summary>
    /// Carries on a scan that stopped partway. The subtrees the earlier run finished are already
    /// stored, so only the folders it never reached are walked — nothing is measured twice.
    /// </summary>
    [RelayCommand]
    private void ResumeSession(ScanHistoryModel? session)
    {
        if (session is null || !session.CanResume || _controller.IsRunning)
            return;

        // Navigate first so the progress page is subscribed before the scan can finish.
        _navigation.NavigateTo(NavigationKeys.ScanProgress);
        _ = _controller.ResumeAsync(session.Id);
    }

    // --- .storava archives -------------------------------------------------------
    // An archive carries a scan and its advice, never settings and never the API key: the service
    // reads only the scan tables, so there is nothing for a key to travel in.

    private bool CanExportSession(ScanHistoryModel? session) => session is not null && !IsArchiveBusy;

    [RelayCommand(CanExecute = nameof(CanExportSession))]
    private async Task ExportSessionAsync(ScanHistoryModel? session)
    {
        if (session is null || IsArchiveBusy)
            return;

        string? path = _fileSaver.Save(SuggestedArchiveName(session), "storava");
        if (path is null)
            return;

        IsArchiveBusy = true;
        ErrorMessage = null;
        ArchiveStatus = null;

        try
        {
            var result = await _archives.ExportAsync(
                session.Id,
                path,
                _localization.CurrentCulture.Name,
                ReportArchiveProgress()).ConfigureAwait(true);

            if (result.IsFailure)
            {
                ErrorMessage = DescribeArchiveError(result.Error.Code);
                return;
            }

            ArchiveStatus = string.Format(
                _localization.CurrentCulture,
                _localization["Str.Archive.Exported"],
                Path.GetFileName(path),
                result.Value.ItemCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exporting a scan to an archive failed.");
            ErrorMessage = _localization["Str.Archive.Error.Write"];
        }
        finally
        {
            IsArchiveBusy = false;
            ArchiveProgressText = null;
        }
    }

    private bool CanImportArchive() => !IsArchiveBusy;

    [RelayCommand(CanExecute = nameof(CanImportArchive))]
    private async Task ImportArchiveAsync()
    {
        if (IsArchiveBusy)
            return;

        string? path = _fileOpener.Open("storava");
        if (path is null)
            return;

        IsArchiveBusy = true;
        ErrorMessage = null;
        ArchiveStatus = null;

        try
        {
            // Describe the file before touching the database, so the confirmation is about what
            // this archive actually contains rather than about archives in general.
            var inspection = await _archives.InspectAsync(path).ConfigureAwait(true);
            if (inspection.IsFailure)
            {
                ErrorMessage = DescribeArchiveError(inspection.Error.Code);
                return;
            }

            if (!await ConfirmImportAsync(inspection.Value).ConfigureAwait(true))
                return;

            var result = await _archives.ImportAsync(path, ReportArchiveProgress()).ConfigureAwait(true);
            if (result.IsFailure)
            {
                ErrorMessage = DescribeArchiveError(result.Error.Code);
                return;
            }

            ArchiveStatus = string.Format(
                _localization.CurrentCulture,
                _localization["Str.Archive.Imported"],
                result.Value.ItemCount,
                result.Value.RootPath);

            // The comparison on screen may have been built from the scan the import just replaced.
            HasComparison = false;
            _comparison = null;

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Importing an archive failed.");
            ErrorMessage = _localization["Str.Archive.Error.Read"];
        }
        finally
        {
            IsArchiveBusy = false;
            ArchiveProgressText = null;
        }
    }

    /// <summary>
    /// Import restores the archive under the scan id it was written with, so re-importing replaces
    /// the earlier copy instead of duplicating it. When that id belongs to a scan measured on this
    /// machine, the confirmation says so rather than letting a local scan be quietly overwritten.
    /// </summary>
    private async Task<bool> ConfirmImportAsync(StoravaArchiveManifest manifest)
    {
        var culture = _localization.CurrentCulture;
        var existing = await _sessions.GetAsync(manifest.SessionId).ConfigureAwait(true);

        string message = string.Format(
            culture,
            _localization["Str.Archive.Import.Message"],
            manifest.RootPath,
            manifest.ScanDate.ToString("g", culture),
            manifest.ItemCount.ToString("N0", culture),
            manifest.RecommendationCount.ToString("N0", culture));

        if (existing is not null)
        {
            message += Environment.NewLine + Environment.NewLine + _localization[
                existing.IsImported
                    ? "Str.Archive.Import.ReplacesImport"
                    : "Str.Archive.Import.ReplacesLocal"];
        }

        return await _dialogs.ConfirmAsync(
            _localization["Str.Archive.Import.Title"],
            message,
            _localization["Str.Archive.Import.Action"],
            _localization["Str.Common.Cancel"]).ConfigureAwait(true);
    }

    private Progress<ArchiveProgress> ReportArchiveProgress() => new(p =>
        ArchiveProgressText = p.ItemsProcessed > 0
            ? string.Format(
                _localization.CurrentCulture,
                _localization["Str.Archive.Progress.Items"],
                DescribeStage(p.Stage),
                p.ItemsProcessed.ToString("N0", _localization.CurrentCulture))
            : DescribeStage(p.Stage));

    private string DescribeStage(string stage) => _localization[stage switch
    {
        "scan" => "Str.Archive.Stage.Scan",
        "items" => "Str.Archive.Stage.Items",
        "categories" => "Str.Archive.Stage.Categories",
        "recommendations" => "Str.Archive.Stage.Recommendations",
        _ => "Str.Archive.Stage.Working"
    }];

    /// <summary>A name that says which folder and which run the archive holds.</summary>
    private static string SuggestedArchiveName(ScanHistoryModel session)
    {
        string leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(session.RootPath));
        if (string.IsNullOrWhiteSpace(leaf))
            leaf = session.RootPath;

        foreach (char invalid in Path.GetInvalidFileNameChars())
            leaf = leaf.Replace(invalid, '-');

        leaf = leaf.Trim('-', ' ');
        if (leaf.Length == 0)
            leaf = "scan";

        return $"storava-{leaf}-{session.Session.StartedAt.LocalDateTime:yyyyMMdd-HHmm}{StoravaArchiveEntries.Extension}";
    }

    private string DescribeArchiveError(string code) => _localization[code switch
    {
        "archive.not_found" => "Str.Archive.Error.NotFound",
        "archive.invalid" => "Str.Archive.Error.NotAnArchive",
        "archive.incomplete" => "Str.Archive.Error.Incomplete",
        "archive.tampered" => "Str.Archive.Error.Tampered",
        "archive.unsupported_version" => "Str.Archive.Error.UnsupportedVersion",
        "archive.session_missing" => "Str.History.Error.SessionMissing",
        "archive.write_failed" => "Str.Archive.Error.Write",
        _ => "Str.Archive.Error.Read"
    }];

    private string Describe(string code) => _localization[code switch
    {
        "compare.same_session" => "Str.History.Error.SameSession",
        "compare.different_roots" => "Str.History.Error.DifferentRoots",
        "compare.session_missing" => "Str.History.Error.SessionMissing",
        _ => "Str.History.Error.Compare"
    }];

    public void Dispose() => _localization.LanguageChanged -= OnLanguageChanged;
}
