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
    // Report formats Storava can write. Anything else falls back to "all files".
    private static readonly Dictionary<string, string> Filters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["html"] = "HTML report (*.html)|*.html",
        ["json"] = "JSON report (*.json)|*.json",
        ["csv"] = "CSV report (*.csv)|*.csv"
    };

    public string? Save(string suggestedFileName, string extension)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Storava",
            FileName = suggestedFileName,
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true,
            Filter = Filters.TryGetValue(extension, out var filter) ? filter : "All files (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
