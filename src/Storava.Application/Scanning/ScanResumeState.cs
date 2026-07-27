using System.Text.Json;
using System.Text.Json.Serialization;

namespace Storava.Application.Scanning;

/// <summary>
/// What an interrupted scan needs in order to carry on instead of starting over.
/// <para>
/// A scan is a post-order walk with an explicit stack, so the work still outstanding at the moment
/// it stopped is exactly the chain of folders on that stack: the root, the folder inside it that
/// was being walked, and so on down to the deepest one. Each of them carries the totals it had
/// already accumulated, which is why resuming does not have to re-measure the subtrees that were
/// finished before the interruption — those are already in the database.
/// </para>
/// <para>
/// What deliberately is <em>not</em> stored is which entries of each pending folder had been
/// consumed. That list can run to hundreds of thousands of names, and it is already recorded: the
/// items themselves are in the database. A resumed scan re-enumerates each pending folder and skips
/// the children that are already stored under it.
/// </para>
/// </summary>
public sealed class ScanResumeState
{
    /// <summary>Bumped when the shape changes; an unreadable state is discarded, not guessed at.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Outermost first, deepest last — the scanner's stack from bottom to top.</summary>
    [JsonPropertyName("pending")]
    public List<ResumeFolder> Pending { get; set; } = [];

    [JsonPropertyName("files")]
    public long FilesScanned { get; set; }

    [JsonPropertyName("folders")]
    public long FoldersScanned { get; set; }

    [JsonPropertyName("bytes")]
    public long BytesScanned { get; set; }

    /// <summary>
    /// Errors counted so far. Unreadable paths are not stored as rows, so unlike the totals above
    /// this number cannot be recovered from the database and has to travel in the state.
    /// </summary>
    [JsonPropertyName("errors")]
    public int ErrorCount { get; set; }

    /// <summary>The exclusions the interrupted run was using, so the rest of it matches.</summary>
    [JsonPropertyName("excludedPaths")]
    public List<string> ExcludedPaths { get; set; } = [];

    [JsonPropertyName("excludedExtensions")]
    public List<string> ExcludedExtensions { get; set; } = [];

    /// <summary>True when there is outstanding work worth carrying on with.</summary>
    [JsonIgnore]
    public bool HasWork => Pending.Count > 0;

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>
    /// Reads a stored state, returning null when it is absent, malformed, or written by a version
    /// this build does not understand. A scan that cannot be resumed safely is simply not resumed.
    /// </summary>
    public static ScanResumeState? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize<ScanResumeState>(json, SerializerOptions);
            return state is { Version: CurrentVersion, Pending.Count: > 0 } ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>One folder that was still being walked, with the totals it had reached.</summary>
public sealed class ResumeFolder
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>The id its folder row will be written with, so children already stored still match.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("allocated")]
    public long Allocated { get; set; }

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("folderCount")]
    public int FolderCount { get; set; }

    /// <summary>
    /// Names of the children already written for this folder, filled in from the database when the
    /// scan resumes. Never serialized: the database is the record, and the list can be enormous.
    /// </summary>
    [JsonIgnore]
    public HashSet<string> CompletedChildren { get; } = new(StringComparer.OrdinalIgnoreCase);
}
