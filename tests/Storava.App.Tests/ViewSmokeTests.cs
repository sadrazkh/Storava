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
            // The Application has to exist before the dictionaries are built: constructing it is
            // what registers the "pack" scheme, and a pack URI made before that fails to resolve.
            var application = System.Windows.Application.Current ?? new System.Windows.Application();
            var resources = BuildApplicationResources(isDark: true);
            application.Resources = resources;

            Assert.All(BuildEveryPage(), Assert.NotNull);

            // Then again on the light palette, swapped exactly as ThemeService swaps it, to catch
            // anything the swap itself breaks. Key parity between the two palettes is NOT what
            // this proves — a DynamicResource whose key is missing resolves to nothing rather
            // than throwing, so every page would still load. BothPalettesDefineTheSameKeys is the
            // test that covers that, because nothing here can.
            SwapSemanticPalette(resources, isDark: false);

            Assert.All(BuildEveryPage(), Assert.NotNull);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The two semantic palettes are separate files holding the same keys, and everything binds
    /// them with <c>DynamicResource</c> so a theme switch takes effect without rebuilding a view.
    /// <para>
    /// That binding is also why this test has to exist. A <c>DynamicResource</c> naming a key that
    /// is not there does not throw — it silently resolves to nothing, so a brush added to one
    /// palette and forgotten in the other produces an invisible tag rather than an error, on the
    /// theme the author happened not to be using. Nothing else catches it.
    /// </para>
    /// </summary>
    [Fact]
    public void BothPalettesDefineTheSameKeys()
    {
        var error = RunOnStaThread(() =>
        {
            // One Application per AppDomain, and the sibling test in this class may already have
            // made it. Same class, so xUnit runs them in sequence and there is nothing to race.
            _ = System.Windows.Application.Current ?? new System.Windows.Application();

            var dark = new ResourceDictionary { Source = PaletteUri(isDark: true) };
            var light = new ResourceDictionary { Source = PaletteUri(isDark: false) };

            var darkKeys = dark.Keys.Cast<object>().Select(key => key.ToString()!).OrderBy(key => key);
            var lightKeys = light.Keys.Cast<object>().Select(key => key.ToString()!).OrderBy(key => key);

            Assert.Equal(darkKeys, lightKeys);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// Constructing a control runs its BAML, which resolves every resource it references.
    /// </summary>
    private static UserControl[] BuildEveryPage() =>
    [
        new WelcomeView(),
        new DashboardView(),
        new SettingsView(),
        new ComingSoonView(),
        new NewScanView(),
        new ScanProgressView(),
        new ScanExplorerView(),
        new AnalysisView(),
        new ReportsView(),
        new CleanupView(),
        new HistoryView()
    ];

    /// <summary>Replaces the palette in place, the way ThemeService does.</summary>
    private static void SwapSemanticPalette(ResourceDictionary resources, bool isDark)
    {
        var replacement = new ResourceDictionary { Source = PaletteUri(isDark) };

        var dictionaries = resources.MergedDictionaries;
        var current = dictionaries.First(dictionary =>
            dictionary.Source is { } source &&
            source.OriginalString.Contains("Theme/Palette.", StringComparison.OrdinalIgnoreCase));

        dictionaries[dictionaries.IndexOf(current)] = replacement;
    }

    private static Uri PaletteUri(bool isDark) => new(
        isDark
            ? "pack://application:,,,/Storava;component/Resources/Theme/Palette.Dark.xaml"
            : "pack://application:,,,/Storava;component/Resources/Theme/Palette.Light.xaml",
        UriKind.Absolute);

    /// <summary>
    /// Mirrors the merge order in App.xaml; tokens must come before their consumers.
    /// </summary>
    /// <param name="isDark">Which semantic palette to start on.</param>
    private static ResourceDictionary BuildApplicationResources(bool isDark)
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
            PaletteUri(isDark).OriginalString,
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
