using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Storava.App.Converters;

/// <summary>Non-null → Visible, null → Collapsed. Pass parameter "invert" to reverse.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasValue = value is not null;
        bool invert = parameter as string == "invert";
        return (hasValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
