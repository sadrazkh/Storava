using Microsoft.Win32;

namespace Storava.App.Services;

/// <summary>Asks the user where to write an exported file. UI concern kept out of ViewModels.</summary>
public interface IFileSaver
{
    /// <summary>Returns the chosen path, or null when the user cancels.</summary>
    string? Save(string suggestedFileName, string extension);
}

public sealed class FileSaver : IFileSaver
{
    public string? Save(string suggestedFileName, string extension)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Storava",
            FileName = suggestedFileName,
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true,
            Filter = FileDialogFilters.For(extension)
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

/// <summary>File types Storava reads or writes, shared by the save and open dialogs.</summary>
internal static class FileDialogFilters
{
    private static readonly Dictionary<string, string> Filters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["html"] = "HTML report (*.html)|*.html",
        ["json"] = "JSON report (*.json)|*.json",
        ["csv"] = "CSV report (*.csv)|*.csv",
        ["storava"] = "Storava archive (*.storava)|*.storava"
    };

    /// <summary>The dialog filter for an extension, falling back to "all files".</summary>
    public static string For(string extension) =>
        Filters.TryGetValue(extension, out var filter) ? filter : "All files (*.*)|*.*";
}
