using System.IO;
using Microsoft.Win32;

namespace Storava.App.Services;

/// <summary>Picks a folder from the file system. UI concern kept out of ViewModels.</summary>
public interface IFolderPicker
{
    string? Pick(string? initialDirectory);
}

public sealed class FolderPicker : IFolderPicker
{
    public string? Pick(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Storava",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
