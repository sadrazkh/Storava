using System.Globalization;
using System.Text;
using Storava.Reporting.Model;

namespace Storava.Reporting.Export;

/// <summary>
/// Writes the recommendation table as CSV. A UTF-8 BOM is emitted so Excel opens Persian text
/// correctly, which it otherwise mangles.
/// </summary>
public sealed class CsvReportWriter
{
    public string Write(StorageReport report, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(report);
        culture ??= CultureInfo.InvariantCulture;

        // Cultures that use ',' as the decimal separator need ';' as the field separator,
        // otherwise Excel splits numbers across columns.
        char separator = culture.NumberFormat.NumberDecimalSeparator == "," ? ';' : ',';

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(separator,
        [
            "Title", "Path", "Category", "Risk", "Technology",
            "EstimatedBytes", "EstimatedGb", "Confidence",
            "CanDelete", "CanMove", "CanRegenerate", "Source", "Reason", "Warning"
        ]));

        foreach (var item in report.Recommendations)
        {
            builder.AppendLine(string.Join(separator,
            [
                Escape(item.Title, separator),
                Escape(item.Path, separator),
                Escape(item.CategoryLabel, separator),
                Escape(item.RiskLabel, separator),
                Escape(item.Technology ?? string.Empty, separator),
                item.EstimatedSpace.ToString(CultureInfo.InvariantCulture),
                ((double)item.EstimatedSpace / (1024 * 1024 * 1024)).ToString("0.###", culture),
                item.Confidence.ToString("0.##", culture),
                item.CanDelete ? "yes" : "no",
                item.CanMove ? "yes" : "no",
                item.CanRegenerate ? "yes" : "no",
                item.Source.ToString(),
                Escape(item.Reason, separator),
                Escape(item.Warning ?? string.Empty, separator)
            ]));
        }

        return builder.ToString();
    }

    /// <summary>UTF-8 with BOM, which is what spreadsheet apps expect.</summary>
    public static Encoding Encoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static string Escape(string value, char separator)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        bool needsQuotes = value.Contains(separator) || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        string sanitized = value.Replace("\r", " ").Replace("\n", " ");

        return needsQuotes
            ? $"\"{sanitized.Replace("\"", "\"\"")}\""
            : sanitized;
    }
}
