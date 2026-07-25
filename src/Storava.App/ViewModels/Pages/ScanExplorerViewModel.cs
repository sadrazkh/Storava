using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;

namespace Storava.App.ViewModels.Pages;

public sealed partial class ScanExplorerViewModel : ViewModelBase, IDisposable
{
    private const int TableLimit = 200;

    private readonly IScanQueryService _query;
    private readonly IScanSessionRepository _sessions;
    private readonly ScanController _controller;
    private readonly ILocalizationService _localization;

    /// <summary>The session currently displayed (live scan, or the latest persisted one).</summary>
    private string? _sessionId;

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _searchTerm = string.Empty;
    [ObservableProperty] private string _rootPathText = string.Empty;
    [ObservableProperty] private ScanItemView? _selectedItem;
    [ObservableProperty] private int _selectedTabIndex;

    public ScanExplorerViewModel(
        IScanQueryService query,
        IScanSessionRepository sessions,
        ScanController controller,
        ILocalizationService localization)
    {
        _query = query;
        _sessions = sessions;
        _controller = controller;
        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;

        _ = LoadAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => _ = LoadAsync();

    public void Dispose() => _localization.LanguageChanged -= OnLanguageChanged;

    public ObservableCollection<ScanNodeViewModel> TreeRoots { get; } = [];
    public ObservableCollection<ScanItemView> LargestItems { get; } = [];
    public ObservableCollection<ScanItemView> SearchResults { get; } = [];

    private async Task LoadAsync()
    {
        // Prefer the scan from this session; otherwise fall back to the most recent persisted
        // one so results remain browsable after a restart.
        var sessionId = _controller.CurrentSessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            var recent = await _sessions.GetRecentAsync(1).ConfigureAwait(true);
            if (recent.Count == 0)
            {
                HasData = false;
                return;
            }

            sessionId = recent[0].Id;
        }

        _sessionId = sessionId;
        var session = await _sessions.GetAsync(sessionId).ConfigureAwait(true);
        RootPathText = session?.RootPath ?? string.Empty;

        var culture = _localization.CurrentCulture;
        var roots = await _query.GetRootsAsync(sessionId).ConfigureAwait(true);
        TreeRoots.Clear();
        foreach (var root in roots)
        {
            var node = new ScanNodeViewModel(root, _query, sessionId, culture) { IsExpanded = true };
            TreeRoots.Add(node);
        }

        var largest = await _query.GetLargestAsync(sessionId, TableLimit, foldersOnly: false).ConfigureAwait(true);
        LargestItems.Clear();
        foreach (var item in largest)
            LargestItems.Add(item);

        HasData = roots.Count > 0;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrEmpty(_sessionId) || string.IsNullOrWhiteSpace(SearchTerm))
        {
            IsSearching = false;
            SearchResults.Clear();
            return;
        }

        var results = await _query.SearchAsync(_sessionId, SearchTerm.Trim(), TableLimit).ConfigureAwait(true);
        SearchResults.Clear();
        foreach (var item in results)
            SearchResults.Add(item);
        IsSearching = true;
        SelectedTabIndex = 2;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchTerm = string.Empty;
        IsSearching = false;
        SearchResults.Clear();
        SelectedTabIndex = 0;
    }
}
