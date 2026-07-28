using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Storava.Application.Abstractions;
using Storava.Application.Common;

namespace Storava.App.Services;

/// <summary>Applies base theme and accent color at runtime via MaterialDesign's PaletteHelper.</summary>
public sealed class ThemeService : IThemeService
{
    private readonly PaletteHelper _paletteHelper = new();
    private readonly ILogger<ThemeService> _logger;

    public ThemeService(ILogger<ThemeService> logger) => _logger = logger;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public string AccentColor { get; private set; } = "#0FB5AE";

    public event EventHandler? ThemeChanged;

    public void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        Apply();
    }

    public void ApplyAccent(string hexColor)
    {
        if (!string.IsNullOrWhiteSpace(hexColor))
            AccentColor = hexColor;
        Apply();
    }

    private void Apply()
    {
        var theme = _paletteHelper.GetTheme();

        bool isDark = ResolveIsDark(CurrentTheme);
        theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);

        if (TryParseColor(AccentColor, out var color))
        {
            theme.SetPrimaryColor(color);
            theme.SetSecondaryColor(color);
        }

        _paletteHelper.SetTheme(theme);
        ApplySemanticPalette(isDark);

        _logger.LogInformation("Theme applied: {Theme} (dark={IsDark}), accent {Accent}.", CurrentTheme, isDark, AccentColor);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Swaps Storava's own semantic brushes — risk levels, status tags — for the ones built for
    /// this base theme.
    /// <para>
    /// MaterialDesign's PaletteHelper only knows about its own resources, so without this the tag
    /// colours would stay as they were: a dark-theme tint on a white window, where the label sits
    /// on a background it was never measured against. The two palettes hold identical keys, so
    /// anything referencing them with <c>DynamicResource</c> follows the swap on its own.
    /// </para>
    /// </summary>
    private static void ApplySemanticPalette(bool isDark)
    {
        // Fully qualified: Storava.Application is in scope here, so a bare "Application" binds to
        // the namespace rather than to WPF's type.
        var application = System.Windows.Application.Current;
        if (application is null)
            return;

        var wanted = new Uri(
            isDark
                ? "pack://application:,,,/Resources/Theme/Palette.Dark.xaml"
                : "pack://application:,,,/Resources/Theme/Palette.Light.xaml",
            UriKind.Absolute);

        var dictionaries = application.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source is { } source &&
            source.OriginalString.Contains("Theme/Palette.", StringComparison.OrdinalIgnoreCase));

        if (current is not null)
        {
            if (current.Source == wanted)
                return;

            // Replaced in place rather than removed and appended: a dictionary's position decides
            // which duplicate key wins, and moving it to the end would let it override things it
            // was never meant to.
            dictionaries[dictionaries.IndexOf(current)] = new ResourceDictionary { Source = wanted };
            return;
        }

        dictionaries.Add(new ResourceDictionary { Source = wanted });
    }

    private static bool ResolveIsDark(AppTheme theme) => theme switch
    {
        AppTheme.Light => false,
        AppTheme.Dark => true,
        _ => IsSystemInDarkMode()
    };

    private static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex)!;
            return true;
        }
        catch
        {
            color = Colors.Teal;
            return false;
        }
    }
}
