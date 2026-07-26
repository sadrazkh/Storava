using System.Text.Json.Serialization;

namespace Storava.Contracts.Workspace;

/// <summary>
/// The manifest at the root of a <c>.storava</c> archive. It records what the file contains, the
/// schema it was written against and a hash per entry, so an import can refuse a file that was
/// truncated or edited. It deliberately holds no settings and no secrets.
/// </summary>
public sealed class StoravaArchiveManifest
{
    /// <summary>Bumped only when the layout changes in a way older readers cannot handle.</summary>
    public const int CurrentSchemaVersion = 1;

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
