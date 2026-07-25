using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Domain.Enums;

namespace Storava.App.ViewModels.Pages;

public sealed partial class NewScanViewModel : ViewModelBase
{
    private readonly IStorageInfoService _storage;
    private readonly IFolderPicker _folderPicker;
    private readonly ScanController _controller;
    private readonly INavigationService _navigation;

    [ObservableProperty] private string _selectedPath = string.Empty;
    [ObservableProperty] private bool _isDeep;
    [ObservableProperty] private string _excludedExtensionsText = string.Empty;

    [NotifyPropertyChangedFor(nameof(HasError))]
    [ObservableProperty] private string? _errorMessage;

    public NewScanViewModel(
        IStorageInfoService storage,
        IFolderPicker folderPicker,
        ScanController controller,
        INavigationService navigation,
        ILocalizationService localization)
    {
        _storage = storage;
        _folderPicker = folderPicker;
        _controller = controller;
        _navigation = navigation;

        var culture = localization.CurrentCulture;
        foreach (var drive in _storage.GetDrives().Where(d => d.IsReady))
            Drives.Add(new DriveCardModel(drive, culture));

        SelectedPath = Drives.Count > 0 ? Drives[0].Root : string.Empty;
    }

    public ObservableCollection<DriveCardModel> Drives { get; } = [];

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    // Highlight whichever drive card matches the current target, including when the target
    // was typed or browsed to rather than clicked.
    partial void OnSelectedPathChanged(string value)
    {
        foreach (var drive in Drives)
            drive.IsSelected = string.Equals(drive.Root, value, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void SelectDrive(string root)
    {
        SelectedPath = root;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void Browse()
    {
        var picked = _folderPicker.Pick(Directory.Exists(SelectedPath) ? SelectedPath : null);
        if (picked is not null)
        {
            SelectedPath = picked;
            ErrorMessage = null;
        }
    }

    [RelayCommand]
    private async Task StartScanAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedPath) || !Directory.Exists(SelectedPath))
        {
            ErrorMessage = "invalid-path";
            return;
        }

        if (_controller.IsRunning)
            return;

        var request = new ScanRequest
        {
            RootPath = SelectedPath,
            Mode = IsDeep ? ScanMode.Deep : ScanMode.Quick,
            ExcludedExtensions = ParseExtensions(ExcludedExtensionsText)
        };

        // Navigate first so the progress page is subscribed before the scan can finish.
        _navigation.NavigateTo(NavigationKeys.ScanProgress);
        _ = _controller.StartAsync(request);
    }

    private static IReadOnlyCollection<string> ParseExtensions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
