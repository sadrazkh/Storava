using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Storava.App.Converters;

/// <summary>Converts a <see cref="Color"/> to a frozen brush for legend swatches.</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Color color)
            return Brushes.Transparent;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
