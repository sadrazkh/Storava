using Microsoft.Win32;

namespace Storava.App.Services;

/// <summary>Asks the user which existing file to read. UI concern kept out of ViewModels.</summary>
public interface IFileOpener
{
    /// <summary>Returns the chosen path, or null when the user cancels.</summary>
    string? Open(string extension);
}

public sealed class FileOpener : IFileOpener
{
    public string? Open(string extension)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Storava",
            DefaultExt = extension,
            Multiselect = false,
            // The file has to exist: everything downstream reads it rather than creating it.
            CheckFileExists = true,
            Filter = FileDialogFilters.For(extension)
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
