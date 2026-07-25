using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Domain.ValueObjects;

namespace Storava.App.ViewModels.Pages;

public sealed partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private const int TopFindingCount = 5;

    private readonly IStorageInfoService _storage;
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly IScanSessionRepository _sessions;
    private readonly IScanQueryService _query;
    private readonly ScanController _controller;

    [ObservableProperty] private string _totalText = "—";
    [ObservableProperty] private string _freeText = "—";
    [ObservableProperty] private string _usedText = "—";
    [ObservableProperty] private int _driveCount;
    [ObservableProperty] private double _usedFraction;
    [ObservableProperty] private bool _aiEnabled;

    [ObservableProperty] private bool _hasScan;
    [ObservableProperty] private string _lastScanPath = string.Empty;
    [ObservableProperty] private string _lastScanWhen = string.Empty;
    [ObservableProperty] private string _lastScanSummary = string.Empty;

    public DashboardViewModel(
        IStorageInfoService storage,
        ISettingsService settings,
        INavigationService navigation,
        ILocalizationService localization,
        IScanSessionRepository sessions,
        IScanQueryService query,
        ScanController controller)
    {
        _storage = storage;
        _settings = settings;
        _navigation = navigation;
        _localization = localization;
        _sessions = sessions;
        _query = query;
        _controller = controller;
        _localization.LanguageChanged += OnLanguageChanged;
        _controller.Completed += OnScanCompleted;

        Load();
        _ = LoadLastScanAsync();
    }

    private void OnScanCompleted(object? sender, ScanResult e)
    {
        Load();
        _ = LoadLastScanAsync();
    }

    public ObservableCollection<DriveCardModel> Drives { get; } = [];

    /// <summary>Largest items from the most recent scan, shown as the headline findings.</summary>
    public ObservableCollection<ScanItemView> TopFindings { get; } = [];

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Load();
        _ = LoadLastScanAsync();
    }

    private void Load()
    {
        var culture = _localization.CurrentCulture;
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

    /// <summary>
    /// Surfaces the most recent persisted scan, so results survive an app restart rather than
    /// only living in the current session.
    /// </summary>
    private async Task LoadLastScanAsync()
    {
        var recent = await _sessions.GetRecentAsync(1).ConfigureAwait(true);
        if (recent.Count == 0)
        {
            HasScan = false;
            TopFindings.Clear();
            return;
        }

        var culture = _localization.CurrentCulture;
        var session = recent[0];

        LastScanPath = session.RootPath;
        LastScanWhen = session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", culture);
        LastScanSummary = string.Format(
            culture,
            "{0:N0} · {1:N0} · {2}",
            session.TotalFiles,
            session.TotalFolders,
            new ByteSize(session.TotalSize).Humanize(culture));

        var largest = await _query.GetLargestAsync(session.Id, TopFindingCount, foldersOnly: true).ConfigureAwait(true);
        TopFindings.Clear();
        foreach (var item in largest)
            TopFindings.Add(item);

        HasScan = true;
    }

    [RelayCommand]
    private void StartScan() => _navigation.NavigateTo(NavigationKeys.NewScan);

    [RelayCommand]
    private void OpenExplorer() => _navigation.NavigateTo(NavigationKeys.ScanExplorer);

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _controller.Completed -= OnScanCompleted;
    }
}
