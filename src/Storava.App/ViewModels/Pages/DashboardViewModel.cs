using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storava.App.Models;
using Storava.Application.Abstractions;
using Storava.Domain.ValueObjects;

namespace Storava.App.ViewModels.Pages;

public sealed partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IStorageInfoService _storage;
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;

    [ObservableProperty] private string _totalText = "—";
    [ObservableProperty] private string _freeText = "—";
    [ObservableProperty] private string _usedText = "—";
    [ObservableProperty] private int _driveCount;
    [ObservableProperty] private double _usedFraction;
    [ObservableProperty] private bool _aiEnabled;

    public DashboardViewModel(
        IStorageInfoService storage,
        ISettingsService settings,
        INavigationService navigation,
        ILocalizationService localization)
    {
        _storage = storage;
        _settings = settings;
        _navigation = navigation;
        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;

        Load();
    }

    public ObservableCollection<DriveCardModel> Drives { get; } = [];

    /// <summary>No scan has been run yet in Phase 1, so scan-derived areas show empty states.</summary>
    public bool HasScan => false;

    private void OnLanguageChanged(object? sender, EventArgs e) => Load();

    private void Load()
    {
        var culture = CultureInfo.CurrentCulture;
        var drives = _storage.GetDrives();

        Drives.Clear();
        var total = ByteSize.Zero;
        var free = ByteSize.Zero;
        foreach (var drive in drives)
        {
            Drives.Add(new DriveCardModel(drive, culture));
            if (!drive.IsReady)
                continue;
            total += drive.TotalSize;
            free += drive.FreeSpace;
        }

        var used = new ByteSize(Math.Max(0, total.Bytes - free.Bytes));
        TotalText = total.Humanize(culture);
        FreeText = free.Humanize(culture);
        UsedText = used.Humanize(culture);
        DriveCount = drives.Count(d => d.IsReady);
        UsedFraction = total.Bytes == 0 ? 0 : (double)used.Bytes / total.Bytes;
        AiEnabled = _settings.Current.Ai.Enabled;
    }

    [RelayCommand]
    private void StartScan() => _navigation.NavigateTo(NavigationKeys.NewScan);

    public void Dispose() => _localization.LanguageChanged -= OnLanguageChanged;
}
