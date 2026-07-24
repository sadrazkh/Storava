using System.Globalization;
using System.Windows.Data;

namespace Storava.App.Converters;

/// <summary>
/// Resolves a resource key (string) to its localized value from the merged application
/// resources. Rail items refresh after a language change so the text stays live.
/// </summary>
public sealed class LocKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0)
            return string.Empty;

        return System.Windows.Application.Current?.TryFindResource(key) as string ?? key;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
