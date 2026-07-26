using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Storava.Reporting.Model;

namespace Storava.Reporting.Export;

/// <summary>Writes the report as JSON for archiving or feeding into other tools.</summary>
public sealed class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep Persian text readable rather than escaped to \uXXXX.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Write(StorageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, Options);
    }
}
