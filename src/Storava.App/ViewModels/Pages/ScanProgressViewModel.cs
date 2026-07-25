using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Domain.Enums;

namespace Storava.App.ViewModels.Pages;

public sealed partial class ScanProgressViewModel : ViewModelBase, IDisposable
{
    private readonly ScanController _controller;
    private readonly INavigationService _navigation;

    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private bool _hasErrors;
    [ObservableProperty] private ScanStatus _resultStatus;

    public ScanProgressViewModel(ScanController controller, INavigationService navigation)
    {
        _controller = controller;
        _navigation = navigation;
        _controller.Completed += OnCompleted;

        // If a scan already finished before this page opened, reflect that immediately.
        if (!_controller.IsRunning && _controller.LastResult is { } result)
            ApplyResult(result);
    }

    public ScanController Controller => _controller;

    private void OnCompleted(object? sender, ScanResult e) => ApplyResult(e);

    private void ApplyResult(ScanResult result)
    {
        ResultStatus = result.Status;
        HasErrors = result.ErrorCount > 0;
        IsComplete = true;
    }

    [RelayCommand]
    private void Pause() => _controller.Pause();

    [RelayCommand]
    private void Resume() => _controller.Resume();

    [RelayCommand]
    private void Cancel() => _controller.Cancel();

    [RelayCommand]
    private void ViewResults() => _navigation.NavigateTo(NavigationKeys.ScanExplorer);

    public void Dispose() => _controller.Completed -= OnCompleted;
}
