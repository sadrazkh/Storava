using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storava.App.Controls;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Domain.ValueObjects;

namespace Storava.App.ViewModels.Pages;

public sealed partial class AnalysisViewModel : ViewModelBase, IDisposable
{
    private const int TreemapTileLimit = 220;
    private const int TopConsumerLimit = 12;

    private readonly IScanQueryService _query;
    private readonly IScanSessionRepository _sessions;
    private readonly ScanController _controller;
    private readonly ILocalizationService _localization;

    /// <summary>Drill-down stack of (item id, label); empty means the scan root.</summary>
    private readonly Stack<(string? Id, string Label)> _breadcrumb = new();

    private string? _sessionId;

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _colorByRisk;
    [ObservableProperty] private string _currentLabel = string.Empty;
    [ObservableProperty] private string _identifiedText = string.Empty;

    [NotifyCanExecuteChangedFor(nameof(NavigateUpCommand))]
    [ObservableProperty] private bool _canNavigateUp;

    public AnalysisViewModel(
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

    public ObservableCollection<TreemapItem> Tiles { get; } = [];
    public ObservableCollection<DonutSlice> CategorySlices { get; } = [];
    public ObservableCollection<CategoryUsageRow> Categories { get; } = [];
    public ObservableCollection<ScanItemView> TopConsumers { get; } = [];

    private void OnLanguageChanged(object? sender, EventArgs e) => _ = LoadAsync();

    private async Task LoadAsync()
    {
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
        _breadcrumb.Clear();

        await LoadCategoriesAsync().ConfigureAwait(true);
        await LoadTopConsumersAsync().ConfigureAwait(true);
        await LoadTilesAsync(null, _localization["Str.Analysis.Root"]).ConfigureAwait(true);

        HasData = Tiles.Count > 0 || Categories.Count > 0;
    }

    private async Task LoadCategoriesAsync()
    {
        if (_sessionId is null)
            return;

        var culture = _localization.CurrentCulture;
        var usage = await _query.GetCategoryUsageAsync(_sessionId).ConfigureAwait(true);
        long total = usage.Sum(u => u.TotalSize);

        Categories.Clear();
        CategorySlices.Clear();

        foreach (var entry in usage.Where(u => u.TotalSize > 0))
        {
            string label = _localization[$"Str.Category.{entry.Category}"];
            double share = total == 0 ? 0 : (double)entry.TotalSize / total;

            Categories.Add(new CategoryUsageRow(
                label,
                new ByteSize(entry.TotalSize).Humanize(culture),
                share,
                entry.ItemCount,
                CategoryPalette.ForCategory(entry.Category)));

            CategorySlices.Add(new DonutSlice
            {
                Label = label,
                Value = entry.TotalSize,
                Color = CategoryPalette.ForCategory(entry.Category)
            });
        }

        // How much of the scanned data the rule engine could actually name.
        long identified = usage
            .Where(u => u.Category != Domain.Enums.StorageCategory.Unknown)
            .Sum(u => u.TotalSize);
        IdentifiedText = total == 0
            ? string.Empty
            : string.Format(culture, "{0:P0}", (double)identified / total);
    }

    private async Task LoadTopConsumersAsync()
    {
        if (_sessionId is null)
            return;

        var largest = await _query.GetLargestAsync(_sessionId, TopConsumerLimit, foldersOnly: true).ConfigureAwait(true);
        TopConsumers.Clear();
        foreach (var item in largest.Where(i => i.Depth > 0))
            TopConsumers.Add(item);
    }

    private async Task LoadTilesAsync(string? parentId, string label)
    {
        if (_sessionId is null)
            return;

        var culture = _localization.CurrentCulture;
        var children = await _query
            .GetTreemapChildrenAsync(_sessionId, parentId, TreemapTileLimit)
            .ConfigureAwait(true);

        // Drilling into the single scan root is pointless, so step straight through it.
        if (parentId is null && children.Count == 1 && children[0].IsFolder)
        {
            var root = children[0];
            var rootChildren = await _query
                .GetTreemapChildrenAsync(_sessionId, root.Id, TreemapTileLimit)
                .ConfigureAwait(true);
            if (rootChildren.Count > 0)
            {
                _breadcrumb.Push((null, label));
                await ShowTilesAsync(rootChildren, root.Id, root.Name, culture).ConfigureAwait(true);
                return;
            }
        }

        await ShowTilesAsync(children, parentId, label, culture).ConfigureAwait(true);
    }

    private Task ShowTilesAsync(
        IReadOnlyList<ScanItemView> children, string? parentId, string label, CultureInfo culture)
    {
        Tiles.Clear();
        foreach (var child in children)
        {
            Tiles.Add(new TreemapItem
            {
                Id = child.Id,
                Label = child.Name,
                Value = child.Size,
                Color = ColorByRisk
                    ? CategoryPalette.ForRisk(child.RiskLevel)
                    : CategoryPalette.ForCategory(child.Category),
                CanDrillDown = child.IsFolder && child.FolderCount + child.FileCount > 0,
                Detail = new ByteSize(child.Size).Humanize(culture)
            });
        }

        CurrentLabel = label;
        CanNavigateUp = _breadcrumb.Count > 0;
        _currentParentId = parentId;
        return Task.CompletedTask;
    }

    private string? _currentParentId;

    partial void OnColorByRiskChanged(bool value)
    {
        // Recolour in place; no need to re-query.
        var culture = _localization.CurrentCulture;
        _ = ReloadCurrentTilesAsync(culture);
    }

    private async Task ReloadCurrentTilesAsync(CultureInfo culture)
    {
        if (_sessionId is null)
            return;

        var children = await _query
            .GetTreemapChildrenAsync(_sessionId, _currentParentId, TreemapTileLimit)
            .ConfigureAwait(true);
        await ShowTilesAsync(children, _currentParentId, CurrentLabel, culture).ConfigureAwait(true);
    }

    /// <summary>Zooms into a folder tile.</summary>
    public async Task DrillDownAsync(TreemapItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_sessionId is null || !item.CanDrillDown)
            return;

        _breadcrumb.Push((_currentParentId, CurrentLabel));
        await LoadTilesAsync(item.Id, item.Label).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    private async Task NavigateUpAsync()
    {
        if (_breadcrumb.Count == 0)
            return;

        var (id, label) = _breadcrumb.Pop();
        var culture = _localization.CurrentCulture;
        var children = await _query.GetTreemapChildrenAsync(_sessionId!, id, TreemapTileLimit).ConfigureAwait(true);
        await ShowTilesAsync(children, id, label, culture).ConfigureAwait(true);
    }

    public void Dispose() => _localization.LanguageChanged -= OnLanguageChanged;
}

/// <summary>A row in the category breakdown list.</summary>
public sealed record CategoryUsageRow(
    string Label,
    string SizeText,
    double Share,
    int ItemCount,
    System.Windows.Media.Color Color)
{
    public string SharePercentText => Share.ToString("P0", CultureInfo.CurrentCulture);
}
