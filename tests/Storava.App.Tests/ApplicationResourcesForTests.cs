using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;

namespace Storava.App.Tests;

/// <summary>
/// The application's resource dictionaries, built the way App.xaml builds them.
/// <para>
/// Parsed from one XAML document rather than assembled by adding dictionaries to
/// <see cref="ResourceDictionary.MergedDictionaries"/> one at a time. The difference is not
/// cosmetic: a <c>StaticResource</c> inside a style resolves against the document it was loaded
/// in, so a dictionary loaded on its own cannot see a sibling merged beside it afterwards. Built
/// that way, the card style's padding came out unset and the page threw the moment it was
/// measured — a fault of the harness, not of the application, and one that would have been read
/// as a real bug.
/// </para>
/// </summary>
internal static class ApplicationResourcesForTests
{
    private static readonly object ApplicationGate = new();

    /// <summary>
    /// The one Application this AppDomain may have, with the real resources on it.
    /// <para>
    /// There can only ever be one, and more than one suite now wants it. Checking
    /// <see cref="System.Windows.Application.Current"/> and constructing if it is null is a race
    /// the moment two suites do it, so the check and the construction happen together.
    /// </para>
    /// </summary>
    public static System.Windows.Application EnsureApplication(bool isDark = true)
    {
        lock (ApplicationGate)
        {
            var application = System.Windows.Application.Current ?? new System.Windows.Application();
            application.Resources = Build(isDark);
            return application;
        }
    }

    public static ResourceDictionary Build(bool isDark = true)
    {
        // Mirrors App.xaml, including the order: tokens before their consumers.
        var xaml = new StringBuilder()
            .AppendLine("<ResourceDictionary")
            .AppendLine("    xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"")
            .AppendLine("    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"")
            // The CLR form, not the http namespace: XamlReader has no compiled context to map the
            // friendly URI to an assembly, so the pretty one fails to resolve outside a build.
            .AppendLine("    xmlns:materialDesign=\"clr-namespace:MaterialDesignThemes.Wpf;assembly=MaterialDesignThemes.Wpf\">")
            .AppendLine("  <ResourceDictionary.MergedDictionaries>")
            .AppendLine("    <materialDesign:BundledTheme BaseTheme=\"Dark\" PrimaryColor=\"Teal\" SecondaryColor=\"Cyan\" />")
            .AppendLine("    <ResourceDictionary Source=\"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml\" />")
            .AppendLine($"    <ResourceDictionary Source=\"{PaletteUri(isDark).OriginalString}\" />")
            .AppendLine("    <ResourceDictionary Source=\"pack://application:,,,/Storava;component/Resources/Theme/Spacing.xaml\" />")
            .AppendLine("    <ResourceDictionary Source=\"pack://application:,,,/Storava;component/Resources/Theme/Typography.xaml\" />")
            .AppendLine("    <ResourceDictionary Source=\"pack://application:,,,/Storava;component/Resources/Theme/Controls.xaml\" />")
            .AppendLine("    <ResourceDictionary Source=\"pack://application:,,,/Storava;component/Resources/Localization/Strings.fa.xaml\" />")
            .AppendLine("    <ResourceDictionary Source=\"pack://application:,,,/Storava;component/Resources/ViewMappings.xaml\" />")
            .AppendLine("  </ResourceDictionary.MergedDictionaries>")
            .AppendLine("</ResourceDictionary>")
            .ToString();

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    public static Uri PaletteUri(bool isDark) => new(
        isDark
            ? "pack://application:,,,/Storava;component/Resources/Theme/Palette.Dark.xaml"
            : "pack://application:,,,/Storava;component/Resources/Theme/Palette.Light.xaml",
        UriKind.Absolute);
}
