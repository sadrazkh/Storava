using System.Globalization;
using System.Windows.Data;

namespace Storava.App.Converters;

/// <summary>
/// Resolves a resource key to its localized value, falling back to a general wording when no key
/// was given.
/// <para>
/// Separate from <see cref="LocKeyConverter"/>, which returns an empty string for an empty key —
/// correct where the caller always knows its own key, and wrong here. The one place this is used is
/// the indicator the shell shows while a page loads, and a page that starts working without saying
/// what it is doing should still say <em>something</em>; an empty line under a spinner reads as a
/// bug in the spinner.
/// </para>
/// </summary>
public sealed class LocKeyOrDefaultConverter : IValueConverter
{
    /// <summary>Used whenever a page begins loading without naming what it is fetching.</summary>
    public const string FallbackKey = "Str.Common.Loading";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrEmpty(key))
            key = FallbackKey;

        return System.Windows.Application.Current?.TryFindResource(key) as string ?? key;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
