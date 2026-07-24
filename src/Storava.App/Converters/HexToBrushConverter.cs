using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Storava.App.Converters;

/// <summary>Converts a "#RRGGBB" string to a <see cref="SolidColorBrush"/>.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            }
            catch (FormatException)
            {
            }
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
