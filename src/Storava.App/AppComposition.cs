using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Storava.AI;
using Storava.App.Services;
using Storava.App.ViewModels;
using Storava.App.ViewModels.Pages;
using Storava.App.Views;
using Storava.Application.Abstractions;
using Storava.Infrastructure;
using Storava.Infrastructure.Persistence;
using Storava.Migrations;
using Storava.Platform;
using Storava.Reporting;
using Storava.Rules;

namespace Storava.App;

/// <summary>
/// The application's object graph, in one place and separate from <see cref="App"/>.
/// <para>
/// It lives here rather than inside the WPF startup path so a test can build the same graph
/// against a throwaway folder. A page reachable from the navigation rail but never registered
/// fails only when the user clicks it; <c>DependencyInjectionTests</c> resolves every one of them
/// instead.
/// </para>
/// </summary>
public static class AppComposition
{
    /// <param name="appDataDirectory">
    /// Where the database and the DPAPI secret store live. Kept a parameter so a test never writes
    /// into the real <c>%LOCALAPPDATA%\Storava</c>.
    /// </param>
    public static IServiceCollection AddStoravaApp(this IServiceCollection services, string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);

        services.AddStoravaInfrastructure(Path.Combine(appDataDirectory, "storava.db"));
        // Secrets live beside the database but never inside it, so no export can carry them.
        services.AddStoravaPlatform(Path.Combine(appDataDirectory, "secrets"));
        // Rules decorate the persistence sink so items are classified as the scan streams them.
        services.AddStoravaRules<SqliteScanItemSinkFactory>();
        services.AddStoravaReporting();
        services.AddStoravaAi();
        // Execution decides whether a step may run; the platform layer above provides the only
        // implementation of IFileSystemActions that can actually carry it out.
        services.AddStoravaMigrations();

        // UI services
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();

        // Copying a path and opening it where it lives. Every page names files; none of them could
        // hand one over until now.
        services.AddSingleton<IPathPresenter, PathPresenter>();
        services.AddSingleton<PathActions>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
        services.AddSingleton<ScanController>();
        services.AddSingleton<IFolderPicker, FolderPicker>();
        services.AddSingleton<IFileSaver, FileSaver>();
        services.AddSingleton<IFileOpener, FileOpener>();

        // ViewModels
        services.AddSingleton<ShellViewModel>();
        foreach (var pageType in PageViewModelTypes)
            services.AddTransient(pageType);

        // Windows
        services.AddSingleton<ShellWindow>();

        return services;
    }

    /// <summary>
    /// Every page ViewModel the shell can show. <see cref="NavigationService"/> resolves these by
    /// type, so this list and its page map have to agree — which is what the guard test checks.
    /// </summary>
    public static IReadOnlyList<Type> PageViewModelTypes { get; } =
    [
        typeof(WelcomeViewModel),
        typeof(DashboardViewModel),
        typeof(SettingsViewModel),
        typeof(ComingSoonViewModel),
        typeof(NewScanViewModel),
        typeof(ScanProgressViewModel),
        typeof(ScanExplorerViewModel),
        typeof(AnalysisViewModel),
        typeof(ReportsViewModel),
        typeof(CleanupViewModel),
        typeof(HistoryViewModel)
    ];
}
