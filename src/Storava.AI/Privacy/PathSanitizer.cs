using System.Text;

namespace Storava.AI.Privacy;

/// <summary>
/// Replaces personal parts of a path with stable placeholders before anything leaves the
/// machine, e.g. <c>C:\Users\Ali\Documents\ClientA\Secret</c> becomes
/// <c>&lt;UserProfile&gt;\Documents\&lt;PrivateFolder-1&gt;\&lt;PrivateFolder-2&gt;</c>.
/// <para>
/// Well-known segments (Documents, Downloads, node_modules, .nuget…) are preserved because they
/// carry no personal information and are exactly what makes advice useful. Everything else is
/// numbered per session, so the same folder always maps to the same placeholder within one
/// payload while revealing nothing outside it. The mapping never leaves this process.
/// </para>
/// </summary>
public sealed class PathSanitizer
{
    private static readonly HashSet<string> WellKnownSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        // User shell folders
        "Documents", "Downloads", "Desktop", "Pictures", "Videos", "Music", "Favorites",
        "AppData", "Local", "LocalLow", "Roaming", "Temp", "Public",
        // System roots
        "Windows", "System32", "SysWOW64", "WinSxS", "Program Files", "Program Files (x86)",
        "ProgramData", "Users", "Boot", "Drivers", "$Recycle.Bin",
        // Developer and tooling folders that rules key off
        "bin", "obj", ".vs", ".git", ".idea", "node_modules", ".nuget", "packages", ".gradle",
        ".m2", "repository", ".android", "avd", "Sdk", ".cache", "huggingface", ".ollama",
        "models", "pip", "Cache", "npm-cache", "pnpm-store", "yarn", "Yarn", "steamapps",
        "Library", "DerivedDataCache", "Docker", "DockerDesktopWSL", "logs", "Logs",
        "CrashDumps", "Minidump", "SoftwareDistribution", "Download", "src", "tests", "docs"
    };

    private readonly Dictionary<string, string> _folderAliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _driveAliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _userProfile;
    private readonly string _userName;

    public PathSanitizer(string? userProfile = null, string? userName = null)
    {
        _userProfile = (userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            .TrimEnd('\\', '/');
        _userName = userName ?? Environment.UserName;
    }

    /// <summary>Number of distinct private folder names replaced so far.</summary>
    public int AliasCount => _folderAliases.Count;

    /// <summary>
    /// Sanitizes a full path. The result contains no user name, no drive label and no
    /// personal folder or file names.
    /// </summary>
    public string Sanitize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string working = path.Replace('/', '\\').TrimEnd('\\');
        var builder = new StringBuilder();

        // Collapse the whole user profile prefix into a single placeholder.
        if (_userProfile.Length > 0 &&
            working.StartsWith(_userProfile, StringComparison.OrdinalIgnoreCase) &&
            (working.Length == _userProfile.Length || working[_userProfile.Length] == '\\'))
        {
            builder.Append("<UserProfile>");
            working = working[_userProfile.Length..].TrimStart('\\');
        }

        foreach (var segment in working.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (builder.Length > 0)
                builder.Append('\\');
            builder.Append(SanitizeSegment(segment));
        }

        return builder.Length == 0 ? "<Root>" : builder.ToString();
    }

    private string SanitizeSegment(string segment)
    {
        // Drive roots become <Drive-C> so free-space advice still makes sense.
        if (segment.Length == 2 && segment[1] == ':')
        {
            if (!_driveAliases.TryGetValue(segment, out var driveAlias))
            {
                driveAlias = $"<Drive-{char.ToUpperInvariant(segment[0])}>";
                _driveAliases[segment] = driveAlias;
            }

            return driveAlias;
        }

        if (WellKnownSegments.Contains(segment))
            return segment;

        // The account name must never be sent, even outside the profile path.
        if (string.Equals(segment, _userName, StringComparison.OrdinalIgnoreCase))
            return "<User>";

        if (!_folderAliases.TryGetValue(segment, out var alias))
        {
            alias = $"<PrivateFolder-{_folderAliases.Count + 1}>";
            _folderAliases[segment] = alias;
        }

        return alias;
    }

    /// <summary>
    /// True when the text still contains something that must not be transmitted. Used as a
    /// final assertion before a payload is shown to the user or sent.
    /// </summary>
    public bool ContainsPersonalData(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        if (_userName.Length > 1 && text.Contains(_userName, StringComparison.OrdinalIgnoreCase))
            return true;

        return _userProfile.Length > 0 && text.Contains(_userProfile, StringComparison.OrdinalIgnoreCase);
    }
}
