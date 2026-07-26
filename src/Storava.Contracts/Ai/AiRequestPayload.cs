using System.Text.Json.Serialization;

namespace Storava.Contracts.Ai;

/// <summary>
/// The complete structured summary sent to the AI. It never contains the file tree, real paths,
/// file contents, the user name or any credential — only aggregates and sanitized shapes.
/// The user sees this exact object before anything is sent.
/// </summary>
public sealed class AiRequestPayload
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("privacy")]
    public AiPrivacyStatement Privacy { get; init; } = new();

    [JsonPropertyName("system")]
    public required AiSystemInfo System { get; init; }

    [JsonPropertyName("categories")]
    public IReadOnlyList<AiCategoryUsage> Categories { get; init; } = [];

    [JsonPropertyName("topCandidates")]
    public IReadOnlyList<AiCandidate> TopCandidates { get; init; } = [];

    [JsonPropertyName("unknownItems")]
    public IReadOnlyList<AiCandidate> UnknownItems { get; init; } = [];

    [JsonPropertyName("userGoal")]
    public required AiUserGoal UserGoal { get; init; }
}

/// <summary>Declares, in the payload itself, what it does and does not contain.</summary>
public sealed class AiPrivacyStatement
{
    [JsonPropertyName("containsFileContent")]
    public bool ContainsFileContent => false;

    [JsonPropertyName("containsRealPaths")]
    public bool ContainsRealPaths => false;

    [JsonPropertyName("containsUserName")]
    public bool ContainsUserName => false;

    [JsonPropertyName("containsApiKeys")]
    public bool ContainsApiKeys => false;

    [JsonPropertyName("pathsAreSanitized")]
    public bool PathsAreSanitized => true;
}

public sealed class AiSystemInfo
{
    [JsonPropertyName("os")]
    public required string Os { get; init; }

    [JsonPropertyName("selectedLanguage")]
    public required string SelectedLanguage { get; init; }

    [JsonPropertyName("drive")]
    public required string Drive { get; init; }

    [JsonPropertyName("capacityGb")]
    public double CapacityGb { get; init; }

    [JsonPropertyName("freeGb")]
    public double FreeGb { get; init; }

    [JsonPropertyName("scannedGb")]
    public double ScannedGb { get; init; }

    [JsonPropertyName("fileCount")]
    public int FileCount { get; init; }

    [JsonPropertyName("folderCount")]
    public int FolderCount { get; init; }
}

public sealed class AiCategoryUsage
{
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("sizeGb")]
    public double SizeGb { get; init; }

    [JsonPropertyName("share")]
    public double Share { get; init; }
}

/// <summary>
/// One item the AI may comment on. It is identified only by <see cref="ScanItemId"/>; the real
/// path stays on the machine and is resolved locally from the database afterwards.
/// </summary>
public sealed class AiCandidate
{
    [JsonPropertyName("scanItemId")]
    public required string ScanItemId { get; init; }

    [JsonPropertyName("sanitizedPath")]
    public required string SanitizedPath { get; init; }

    [JsonPropertyName("sizeGb")]
    public double SizeGb { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("technology")]
    public string? Technology { get; init; }

    [JsonPropertyName("riskLevel")]
    public required string RiskLevel { get; init; }

    [JsonPropertyName("canDelete")]
    public bool CanDelete { get; init; }

    [JsonPropertyName("canMove")]
    public bool CanMove { get; init; }

    [JsonPropertyName("canRegenerate")]
    public bool CanRegenerate { get; init; }

    [JsonPropertyName("hasOfficialMigration")]
    public bool HasOfficialMigration { get; init; }

    [JsonPropertyName("daysSinceLastWrite")]
    public int? DaysSinceLastWrite { get; init; }
}

public sealed class AiUserGoal
{
    [JsonPropertyName("targetFreeSpaceGb")]
    public double TargetFreeSpaceGb { get; init; }

    [JsonPropertyName("allowDelete")]
    public bool AllowDelete { get; init; }

    [JsonPropertyName("allowMove")]
    public bool AllowMove { get; init; }
}
