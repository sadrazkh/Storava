using System.Windows;
using Storava.Application.Abstractions;

namespace Storava.App.Services;

/// <summary>
/// Minimal dialog surface for Phase 1. Confirmation dialogs will move to an in-shell
/// MaterialDesign DialogHost in the migration phase where richer previews are needed.
/// </summary>
public sealed class DialogService : IDialogService
{
    public Task ShowInfoAsync(string title, string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        var result = System.Windows.Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question));
        return Task.FromResult(result == MessageBoxResult.OK);
    }
}
