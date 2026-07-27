using System.Text.Json.Serialization;

namespace Storava.Contracts.Workspace;

/// <summary>
/// The manifest at the root of a <c>.storava</c> archive. It records what the file contains, the
/// schema it was written against and a hash per entry, so an import can refuse a file that was
/// truncated or edited. It deliberately holds no settings and no secrets.
/// </summary>
public sealed class StoravaArchiveManifest
{
    /// <summary>
    /// Bumped only when the layout changes in a way older readers cannot handle.
    /// <para>
    /// Version 2 made the archive something all three editions can read. Version 1 was the desktop
    /// writing its own entity shapes with whatever names the serializer chose; version 2 carries an
    /// explicit interchange schema, says whether its paths are absolute or root-relative, and names
    /// the edition that wrote it. A version 1 file is still read — archives outlive releases — but
    /// nothing writes one any more.
    /// </para>
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The oldest layout this build can still open.</summary>
    public const int MinimumReadableSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Version of Storava that produced the file, for diagnostics.</summary>
    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("scanDate")]
    public DateTimeOffset ScanDate { get; set; }

    [JsonPropertyName("os")]
    public string Os { get; set; } = string.Empty;

    /// <summary>Culture the scan's generated text was produced in, e.g. "fa-IR".</summary>
    [JsonPropertyName("culture")]
    public string Culture { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("rootPath")]
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether <see cref="RootPath"/> and every item path are real locations or positions inside
    /// the scanned folder. Absent in version 1, which only the desktop wrote, so absent reads as
    /// absolute.
    /// </summary>
    [JsonPropertyName("pathKind")]
    public ArchivePathKind PathKind { get; set; } = ArchivePathKind.Absolute;

    /// <summary>Which edition wrote this. Shown to the user; never used to gate a feature.</summary>
    [JsonPropertyName("producedBy")]
    public ArchiveProducer ProducedBy { get; set; } = ArchiveProducer.Unknown;

    [JsonPropertyName("itemCount")]
    public int ItemCount { get; set; }

    [JsonPropertyName("recommendationCount")]
    public int RecommendationCount { get; set; }

    /// <summary>SHA-256 of each payload entry, keyed by entry name.</summary>
    [JsonPropertyName("hashes")]
    public Dictionary<string, string> Hashes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>States plainly that the archive carries no credentials.</summary>
    [JsonPropertyName("containsSecrets")]
    public bool ContainsSecrets => false;

    [JsonPropertyName("containsSettings")]
    public bool ContainsSettings => false;
}

/// <summary>Entry names inside a <c>.storava</c> archive.</summary>
public static class StoravaArchiveEntries
{
    public const string Manifest = "manifest.json";
    public const string Scan = "scan.json";

    /// <summary>Scan items, one JSON object per line so the file can be streamed.</summary>
    public const string Items = "items.dat";

    public const string Categories = "categories.json";
    public const string Recommendations = "recommendations.json";

    /// <summary>The file extension, including the dot.</summary>
    public const string Extension = ".storava";
}
