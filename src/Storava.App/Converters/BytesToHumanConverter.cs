using System.Globalization;
using System.Windows.Data;
using Storava.Domain.ValueObjects;

namespace Storava.App.Converters;

/// <summary>
/// Converts a byte count (long) to a human-readable size. Formatting uses
/// <see cref="DisplayCulture"/>, which the app keeps in step with the selected language —
/// WPF does not pass the UI culture to converters by default.
/// </summary>
public sealed class BytesToHumanConverter : IValueConverter
{
    /// <summary>Culture used for all size formatting in the UI.</summary>
    public static CultureInfo DisplayCulture { get; set; } = CultureInfo.InvariantCulture;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long bytes = value switch
        {
            long l => l,
            int i => i,
            _ => 0
        };
        return new ByteSize(Math.Max(0, bytes)).Humanize(DisplayCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
