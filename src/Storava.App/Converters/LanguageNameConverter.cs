using System.Globalization;
using System.Windows.Data;
using Storava.Application.Common;

namespace Storava.App.Converters;

/// <summary>Shows each language in its own native name, independent of the active UI language.</summary>
public sealed class LanguageNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AppLanguage language
            ? language switch { AppLanguage.Persian => "فارسی", _ => "English" }
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
