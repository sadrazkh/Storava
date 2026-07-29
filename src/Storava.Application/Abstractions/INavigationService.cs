namespace Storava.Application.Abstractions;

/// <summary>Stable keys for the top-level pages of the shell.</summary>
public static class NavigationKeys
{
    public const string Welcome = "welcome";
    public const string Dashboard = "dashboard";
    public const string NewScan = "new-scan";
    public const string ScanProgress = "scan-progress";
    public const string ScanExplorer = "scan-explorer";
    public const string Analysis = "analysis";
    /// <summary>
    /// Choosing what to clear and carrying it out. Replaces the three keys this used to take —
    /// advice, the plan document and the run — which were three pages for one intention.
    /// </summary>
    public const string Cleanup = "cleanup";
    public const string Reports = "reports";
    public const string History = "history";
    public const string Settings = "settings";
}

/// <summary>Switches the shell's active page. Implemented in the UI layer.</summary>
public interface INavigationService
{
    string? CurrentKey { get; }

    event EventHandler<string>? Navigated;

    void NavigateTo(string key);
}
