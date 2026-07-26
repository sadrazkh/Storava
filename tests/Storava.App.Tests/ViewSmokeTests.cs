using System.Windows;
using System.Windows.Controls;
using Storava.App.Views.Pages;

namespace Storava.App.Tests;

/// <summary>
/// Loads every page with the real application resource dictionaries. A misspelled
/// <c>StaticResource</c> style, a missing converter or a broken template only fails when that page
/// is first opened — this constructs all of them in one go so the failure lands in CI instead.
/// </summary>
public class ViewSmokeTests
{
    [Fact]
    public void EveryPageLoadsWithTheApplicationResources()
    {
        var error = RunOnStaThread(() =>
        {
            _ = new System.Windows.Application { Resources = BuildApplicationResources() };

            // Constructing the control runs its BAML, which resolves every resource it references.
            UserControl[] pages =
            [
                new WelcomeView(),
                new DashboardView(),
                new SettingsView(),
                new ComingSoonView(),
                new NewScanView(),
                new ScanProgressView(),
                new ScanExplorerView(),
                new AnalysisView(),
                new RecommendationsView(),
                new ReportsView(),
                new StoragePlanView(),
                new MigrationCenterView(),
                new HistoryView()
            ];

            Assert.All(pages, Assert.NotNull);
        });

        Assert.Null(error);
    }

    /// <summary>Mirrors the merge order in App.xaml; tokens must come before their consumers.</summary>
    private static ResourceDictionary BuildApplicationResources()
    {
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
        {
            BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark,
            PrimaryColor = MaterialDesignColors.PrimaryColor.Teal,
            SecondaryColor = MaterialDesignColors.SecondaryColor.Cyan
        });

        foreach (string source in (string[])
        [
            "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml",
            "pack://application:,,,/Storava;component/Resources/Theme/Palette.xaml",
            "pack://application:,,,/Storava;component/Resources/Theme/Spacing.xaml",
            "pack://application:,,,/Storava;component/Resources/Theme/Typography.xaml",
            "pack://application:,,,/Storava;component/Resources/Theme/Controls.xaml",
            "pack://application:,,,/Storava;component/Resources/Localization/Strings.fa.xaml",
            "pack://application:,,,/Storava;component/Resources/ViewMappings.xaml"
        ])
        {
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) });
        }

        return resources;
    }

    private static Exception? RunOnStaThread(Action action)
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return captured;
    }
}
