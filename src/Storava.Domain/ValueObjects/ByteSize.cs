using System.Globalization;

namespace Storava.Domain.ValueObjects;

/// <summary>
/// A non-negative size in bytes with culture-neutral humanization helpers.
/// Used everywhere sizes are aggregated or displayed.
/// </summary>
public readonly record struct ByteSize : IComparable<ByteSize>
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public ByteSize(long bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Size cannot be negative.");
        Bytes = bytes;
    }

    public long Bytes { get; }

    public double Gigabytes => Bytes / 1024d / 1024d / 1024d;

    public static ByteSize Zero => new(0);
    public static ByteSize FromGigabytes(double gb) => new((long)(gb * 1024d * 1024d * 1024d));

    public static ByteSize operator +(ByteSize a, ByteSize b) => new(a.Bytes + b.Bytes);

    public int CompareTo(ByteSize other) => Bytes.CompareTo(other.Bytes);

    /// <summary>
    /// Formats the size using the given culture (e.g. "1.5 GB").
    /// The unit label stays Latin; the number is localized.
    /// </summary>
    public string Humanize(CultureInfo? culture = null, int decimals = 1)
    {
        culture ??= CultureInfo.InvariantCulture;
        double value = Bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        string number = unit == 0
            ? value.ToString("0", culture)
            : value.ToString("0." + new string('#', decimals), culture);
        return $"{number} {Units[unit]}";
    }

    public override string ToString() => Humanize();
}
