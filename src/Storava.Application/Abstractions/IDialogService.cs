namespace Storava.Application.Abstractions;

/// <summary>Simple user-facing dialogs. Confirmation is required before any sensitive action.</summary>
public interface IDialogService
{
    Task ShowInfoAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);
}
