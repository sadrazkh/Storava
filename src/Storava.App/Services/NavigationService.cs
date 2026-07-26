using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storava.App.ViewModels.Pages;
using Storava.Application.Abstractions;

namespace Storava.App.Services;

/// <summary>
/// Resolves the ViewModel for a navigation key from the DI container and exposes it as the
/// shell's current page. Views are matched to ViewModels via DataTemplates.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private static readonly Dictionary<string, Type> PageMap = new(StringComparer.Ordinal)
    {
        [NavigationKeys.Welcome] = typeof(WelcomeViewModel),
        [NavigationKeys.Dashboard] = typeof(DashboardViewModel),
        [NavigationKeys.Settings] = typeof(SettingsViewModel),
        [NavigationKeys.NewScan] = typeof(NewScanViewModel),
        [NavigationKeys.ScanProgress] = typeof(ScanProgressViewModel),
        [NavigationKeys.ScanExplorer] = typeof(ScanExplorerViewModel),
        [NavigationKeys.Analysis] = typeof(AnalysisViewModel),
        [NavigationKeys.Recommendations] = typeof(RecommendationsViewModel),
        [NavigationKeys.StoragePlan] = typeof(StoragePlanViewModel),
        [NavigationKeys.MigrationCenter] = typeof(ComingSoonViewModel),
        [NavigationKeys.Reports] = typeof(ReportsViewModel),
        [NavigationKeys.History] = typeof(ComingSoonViewModel)
    };

    // Localized header key shown on the placeholder ("coming soon") pages.
    private static readonly Dictionary<string, string> PlaceholderTitleKeys = new(StringComparer.Ordinal)
    {
        [NavigationKeys.MigrationCenter] = "Str.Nav.MigrationCenter",
        [NavigationKeys.History] = "Str.Nav.History"
    };

    private readonly IServiceProvider _services;
    private readonly ILogger<NavigationService> _logger;

    public NavigationService(IServiceProvider services, ILogger<NavigationService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public string? CurrentKey { get; private set; }

    public object? CurrentViewModel { get; private set; }

    public event EventHandler<string>? Navigated;

    /// <summary>Raised after <see cref="CurrentViewModel"/> changes.</summary>
    public event EventHandler? CurrentChanged;

    public void NavigateTo(string key)
    {
        if (!PageMap.TryGetValue(key, out var vmType))
        {
            _logger.LogWarning("Navigation requested for unknown key '{Key}'.", key);
            return;
        }

        var viewModel = _services.GetRequiredService(vmType);

        if (viewModel is ComingSoonViewModel placeholder &&
            PlaceholderTitleKeys.TryGetValue(key, out var titleKey))
        {
            placeholder.Configure(titleKey);
        }

        // Page ViewModels are transient and subscribe to singleton services (localization,
        // settings, the scan controller). Without this the old instance keeps reacting to those
        // events forever, so every visit to a page would add another live listener.
        var outgoing = CurrentViewModel as IDisposable;

        CurrentKey = key;
        CurrentViewModel = viewModel;
        _logger.LogDebug("Navigated to {Key}.", key);

        CurrentChanged?.Invoke(this, EventArgs.Empty);
        Navigated?.Invoke(this, key);

        // Released only after the shell has swapped in the new page.
        outgoing?.Dispose();
    }
}
