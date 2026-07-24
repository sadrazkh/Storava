using Storava.Application.Common;

namespace Storava.Application.Abstractions;

/// <summary>Applies the visual theme (light/dark) and accent color live.</summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    string AccentColor { get; }

    event EventHandler? ThemeChanged;

    void ApplyTheme(AppTheme theme);
    void ApplyAccent(string hexColor);
}
