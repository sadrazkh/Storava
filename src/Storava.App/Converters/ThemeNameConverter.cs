using System.Globalization;
using System.Windows.Data;
using Storava.Application.Common;

namespace Storava.App.Converters;

/// <summary>Maps an <see cref="AppTheme"/> to its localized display name.</summary>
public sealed class ThemeNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AppTheme theme)
            return string.Empty;

        string key = theme switch
        {
            AppTheme.Light => "Str.Settings.Theme.Light",
            AppTheme.Dark => "Str.Settings.Theme.Dark",
            _ => "Str.Settings.Theme.System"
        };

        return System.Windows.Application.Current?.TryFindResource(key) as string ?? theme.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
