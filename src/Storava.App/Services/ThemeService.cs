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
        _logger.LogInformation("Theme applied: {Theme} (dark={IsDark}), accent {Accent}.", CurrentTheme, isDark, AccentColor);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
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
