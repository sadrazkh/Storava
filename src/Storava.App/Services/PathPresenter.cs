using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;

namespace Storava.App.Services;

/// <inheritdoc />
public sealed class PathPresenter : IPathPresenter
{
    private readonly ILogger<PathPresenter> _logger;

    public PathPresenter(ILogger<PathPresenter> logger) => _logger = logger;

    public bool Copy(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            // Copy rather than SetText: the clipboard belongs to whatever the user has open, and
            // another application holding it briefly is common enough that it is not an error worth
            // showing. Failing quietly and returning false lets the caller say nothing happened.
            Clipboard.SetDataObject(path, copy: true);
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "The clipboard would not take the path.");
            return false;
        }
    }

    public bool CanReveal(string? path) =>
        !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));

    public bool Reveal(string? path)
    {
        if (!CanReveal(path))
            return false;

        try
        {
            // /select, puts the item itself under the cursor rather than opening it. For a folder
            // that means showing it highlighted in its parent, which is what "where is this" means;
            // opening it would hide the thing being asked about.
            //
            // The path is quoted because these are user paths and contain spaces, and passed as a
            // single argument string rather than through a shell, so there is nothing to escape
            // into.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Explorer would not open {Path}.", path);
            return false;
        }
    }
}
