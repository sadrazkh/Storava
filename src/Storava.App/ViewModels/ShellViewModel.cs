using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.Common;
using WpfFlowDirection = System.Windows.FlowDirection;

namespace Storava.App.ViewModels;

/// <summary>Root ViewModel: owns the navigation rail, current page, theme and language chrome.</summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;
    private bool _suppressNavigation;

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private NavigationItem? _selectedNavItem;
    [ObservableProperty] private WpfFlowDirection _flowDirection;

    public ShellViewModel(
        NavigationService navigation,
        ILocalizationService localization,
        IThemeService theme,
        ISettingsService settings,
        PathActions paths)
    {
        _navigation = navigation;
        _localization = localization;
        _theme = theme;
        _settings = settings;
        Paths = paths;

        NavItems = BuildNavItems();
        NavItemsView = CollectionViewSource.GetDefaultView(NavItems);
        NavItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavigationItem.Group)));

        _flowDirection = _localization.IsRightToLeft ? WpfFlowDirection.RightToLeft : WpfFlowDirection.LeftToRight;

        _navigation.CurrentChanged += OnCurrentChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Copying a path and opening it in Explorer, for every page at once.
    /// <para>
    /// Here rather than on each page because it is the same two commands everywhere and the shell
    /// is the one ancestor every page has. A row binds through the window and gets them.
    /// </para>
    /// </summary>
    public PathActions Paths { get; }

    public ObservableCollection<NavigationItem> NavItems { get; }

    public ICollectionView NavItemsView { get; }

    /// <summary>Raised so the view can refresh the grouped rail after a language change.</summary>
    public event EventHandler? NavRefreshRequested;

    public void Start()
    {
        var startKey = _settings.Current.OnboardingCompleted
            ? NavigationKeys.Dashboard
            : NavigationKeys.Welcome;
        _navigation.NavigateTo(startKey);
    }

    partial void OnSelectedNavItemChanged(NavigationItem? value)
    {
        if (_suppressNavigation || value is null)
            return;
        _navigation.NavigateTo(value.Key);
    }

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        CurrentPage = _navigation.CurrentViewModel;

        var match = NavItems.FirstOrDefault(i => i.Key == _navigation.CurrentKey);
        _suppressNavigation = true;
        SelectedNavItem = match;
        _suppressNavigation = false;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        FlowDirection = _localization.IsRightToLeft ? WpfFlowDirection.RightToLeft : WpfFlowDirection.LeftToRight;
        NavRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        var next = _theme.CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _theme.ApplyTheme(next);

        var updated = _settings.Current.Clone();
        updated.Theme = next;
        await _settings.SaveAsync(updated).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleLanguageAsync()
    {
        var next = _localization.CurrentLanguage == AppLanguage.Persian
            ? AppLanguage.English
            : AppLanguage.Persian;
        _localization.SetLanguage(next);

        var updated = _settings.Current.Clone();
        updated.Language = next;
        await _settings.SaveAsync(updated).ConfigureAwait(true);
    }

    private static ObservableCollection<NavigationItem> BuildNavItems() =>
    [
        new(NavigationKeys.Dashboard, "Str.Nav.Dashboard", PackIconKind.ViewDashboard, "Str.Shell.Group.Main"),
        new(NavigationKeys.NewScan, "Str.Nav.NewScan", PackIconKind.Magnify, "Str.Shell.Group.Analyze"),
        new(NavigationKeys.ScanExplorer, "Str.Nav.ScanExplorer", PackIconKind.Folder, "Str.Shell.Group.Analyze"),
        new(NavigationKeys.Analysis, "Str.Nav.Analysis", PackIconKind.ChartBoxOutline, "Str.Shell.Group.Analyze"),
        new(NavigationKeys.Cleanup, "Str.Nav.Cleanup", PackIconKind.Broom, "Str.Shell.Group.Act"),
        new(NavigationKeys.Reports, "Str.Nav.Reports", PackIconKind.ChartBar, "Str.Shell.Group.System"),
        new(NavigationKeys.History, "Str.Nav.History", PackIconKind.History, "Str.Shell.Group.System"),
        new(NavigationKeys.Settings, "Str.Nav.Settings", PackIconKind.Cog, "Str.Shell.Group.System")
    ];
}
