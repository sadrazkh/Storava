using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Domain.ValueObjects;

namespace Storava.App.ViewModels;

/// <summary>
/// A lazily-loaded node in the scan tree. Children are fetched from the database only when the
/// node is first expanded, so arbitrarily large trees never load fully into memory.
/// </summary>
public sealed partial class ScanNodeViewModel : ObservableObject
{
    private readonly IScanQueryService? _query;
    private readonly string _sessionId;
    private readonly CultureInfo _culture;
    private bool _loaded;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public ScanNodeViewModel(ScanItemView item, IScanQueryService query, string sessionId, CultureInfo culture)
    {
        Item = item;
        _query = query;
        _sessionId = sessionId;
        _culture = culture;

        if (item.HasChildren)
            Children.Add(CreatePlaceholder());
    }

    private ScanNodeViewModel()
    {
        Item = null!;
        _sessionId = string.Empty;
        _culture = CultureInfo.InvariantCulture;
        IsPlaceholder = true;
    }

    public ScanItemView Item { get; }

    public bool IsPlaceholder { get; }

    public ObservableCollection<ScanNodeViewModel> Children { get; } = [];

    public string SizeText => IsPlaceholder ? string.Empty : new ByteSize(Item.Size).Humanize(_culture);

    public string CountText => !IsPlaceholder && Item.IsFolder
        ? string.Format(_culture, "{0:N0} · {1:N0}", Item.FileCount, Item.FolderCount)
        : string.Empty;

    public string DisplayName => IsPlaceholder ? string.Empty : Item.Name;

    private static ScanNodeViewModel CreatePlaceholder() => new();

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded)
            _ = LoadChildrenAsync();
    }

    private async Task LoadChildrenAsync()
    {
        if (_loaded || _query is null)
            return;
        _loaded = true;

        var children = await _query.GetChildrenAsync(_sessionId, Item.Id).ConfigureAwait(true);
        Children.Clear();
        foreach (var child in children)
            Children.Add(new ScanNodeViewModel(child, _query, _sessionId, _culture));
    }
}
