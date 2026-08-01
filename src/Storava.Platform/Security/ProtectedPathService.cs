using System.Runtime.InteropServices;

namespace Storava.Platform.Security;

using Storava.Application.Abstractions;

/// <summary>
/// Marks Windows-critical locations as protected. These can never be targeted for
/// deletion or migration, regardless of user selection or AI recommendation.
/// </summary>
public sealed class ProtectedPathService : IProtectedPathService
{
    private readonly List<string> _roots;

    public ProtectedPathService()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var systemDrive = Path.GetPathRoot(windows) ?? "C:\\";

        var candidates = new[]
        {
            windows,
            programFiles,
            programFilesX86,
            Path.Combine(windows, "System32"),
            Path.Combine(windows, "SysWOW64"),
            Path.Combine(windows, "WinSxS"),
            Path.Combine(windows, "Boot"),
            Path.Combine(windows, "Fonts"),
            Path.Combine(systemDrive, "System Volume Information"),
            Path.Combine(systemDrive, "$Recycle.Bin"),
            Path.Combine(systemDrive, "Recovery"),
            Path.Combine(systemDrive, "PerfLogs")
        };

        _roots = candidates
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> ProtectedRoots => _roots;

    public bool IsProtected(string path) => MatchingRoot(path) is not null;

    public string? MatchingRoot(string path)
    {
        // Fail safe: an unknown or empty path is treated as protected. It has no root to name, so
        // it reports the empty string rather than null — refused, with nothing to point at.
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string normalized = Normalize(path);

        foreach (var root in _roots)
        {
            if (normalized.Equals(root, StringComparison.OrdinalIgnoreCase))
                return root;

            if (normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return root;
        }

        return null;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    // Reserved for future elevation checks; kept here to centralize platform concerns.
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern bool IsUserAnAdmin();

    public static bool IsRunningAsAdministrator()
    {
        try
        {
            return IsUserAnAdmin();
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
