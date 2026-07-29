using System.Text.Json.Serialization;

namespace Storava.Contracts.Workspace;

/// <summary>
/// Whether the paths in an archive name real locations or positions inside the scanned folder.
/// <para>
/// This is the difference between the editions, not a detail. The desktop and the Agent walk the
/// file system and know where things are; the browser is only ever granted one folder and knows
/// nothing above it. An archive has to say which it holds, because a reader that assumes the wrong
/// one either shows paths that do not exist or refuses to act on paths that do.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ArchivePathKind>))]
public enum ArchivePathKind
{
    /// <summary>Full operating-system paths, as the desktop edition and the Agent record them.</summary>
    Absolute = 0,

    /// <summary>Positions under the scanned root, as the browser edition records them.</summary>
    RootRelative
}

/// <summary>
/// Which edition is writing archives in this process.
/// <para>
/// A registered value rather than a constant, because the Agent shares the desktop application's
/// entire archive stack. Left as a constant it would stamp its own exports as desktop-written, and
/// saying where a file came from is the manifest's one job.
/// </para>
/// </summary>
public sealed record ArchiveIdentity(ArchiveProducer Producer)
{
    public static readonly ArchiveIdentity Desktop = new(ArchiveProducer.Desktop);
    public static readonly ArchiveIdentity Agent = new(ArchiveProducer.Agent);
    public static readonly ArchiveIdentity Browser = new(ArchiveProducer.Browser);
}

/// <summary>Which edition wrote an archive. Shown to the user, never used to gate a feature.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ArchiveProducer>))]
public enum ArchiveProducer
{
    Unknown = 0,
    Desktop,
    Browser,
    Agent
}

/// <summary>
/// One scan item as it travels between editions.
/// <para>
/// Deliberately not either edition's internal shape. The desktop links its tree by parent id and
/// records a single matched rule; the browser links by parent path and records several. Picking
/// one of them as "the" format would make the other's export lossy in a way nobody could see, so
/// this carries both linkages and the superset of the fields, and each edition maps to it.
/// </para>
/// <para>
/// Names are camelCase and written explicitly. An archive outlives the code that wrote it, and a
/// serializer's default naming is not something to bet a file format on.
/// </para>
/// </summary>
public sealed class ArchiveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>How the desktop links a tree. Null for a root, or for an archive that links by path.</summary>
    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    /// <summary>How the browser links a tree. Null when the archive links by id.</summary>
    [JsonPropertyName("parentPath")]
    public string? ParentPath { get; set; }

    /// <summary>Absolute or root-relative according to the manifest's <c>pathKind</c>.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>"file" or "folder".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = ArchiveItemKinds.File;

    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>Size on disk. Only a deep desktop scan knows this; null everywhere else.</summary>
    [JsonPropertyName("allocatedSize")]
    public long? AllocatedSize { get; set; }

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("folderCount")]
    public int FolderCount { get; set; }

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("modifiedAt")]
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>
    /// The shared vocabulary, which is the desktop's: a storage purpose such as
    /// <c>PackageCaches</c>, not a file-type bucket. The browser groups by extension for display
    /// and maps into this on the way out, because "what is this for" is the question an archive is
    /// worth carrying an answer to.
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "Unknown";

    [JsonPropertyName("technology")]
    public string? Technology { get; set; }

    /// <summary>
    /// Every rule that matched. The desktop records one and the browser several, so this is a list
    /// and the desktop reads the first — a superset loses nothing in either direction.
    /// </summary>
    [JsonPropertyName("ruleIds")]
    public List<string> RuleIds { get; set; } = [];

    /// <summary>"Unknown", "Low", "Medium", "High" or "Protected".</summary>
    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "Unknown";

    [JsonPropertyName("isProtected")]
    public bool IsProtected { get; set; }

    [JsonPropertyName("isReparsePoint")]
    public bool IsReparsePoint { get; set; }

    /// <summary>What the local rules permitted where this was scanned. Advice, never permission.</summary>
    [JsonPropertyName("canDelete")]
    public bool CanDelete { get; set; }

    [JsonPropertyName("canMove")]
    public bool CanMove { get; set; }
}

public static class ArchiveItemKinds
{
    public const string File = "file";
    public const string Folder = "folder";
}

/// <summary>
/// One piece of advice about one item, as it travels between editions.
/// <para>
/// This exists because it did not. The archive has always carried a recommendations entry, but the
/// desktop wrote its own domain entity straight into it — PascalCase names and internal enums —
/// which no other edition had any way to read. The browser consequently threw the entry away on
/// import and wrote an empty one on export, so advice produced by the Agent or the desktop simply
/// vanished on the way to a browser and never came back.
/// </para>
/// <para>
/// Narrower than the desktop's own record, but only where narrowing is safe. The score and the
/// confidence stay behind: they rank one machine's advice against itself and mean nothing to a
/// reader applying its own catalog.
/// </para>
/// <para>
/// The migration fields travel, and it took a regression to notice they had to. They are facts
/// about the technology — npm's cache honours a path setting, and that is true wherever the folder
/// is read — not judgements about the machine that wrote them. Dropping them made a plan built from
/// an imported archive fall back to a different way of moving a folder than the one the catalog
/// documented, silently, on the desktop that produced it in the first place.
/// </para>
/// </summary>
public sealed class ArchiveRecommendation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The item this is about, by its id in the same archive's item list.
    /// <para>
    /// An edition that mints its own ids on import has to remap this as it reads, or the advice
    /// arrives pointing at nothing.
    /// </para>
    /// </summary>
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Where the item was, in whatever form the manifest declares paths take.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Why it was raised. The part a person actually reads.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>"Unknown", "Low", "Medium", "High" or "Protected".</summary>
    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "Unknown";

    /// <summary>How much this could free, as measured where it was scanned.</summary>
    [JsonPropertyName("estimatedBytes")]
    public long EstimatedBytes { get; set; }

    [JsonPropertyName("ruleId")]
    public string? RuleId { get; set; }

    /// <summary>The shared storage-purpose vocabulary, such as "PackageCaches".</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "Unknown";

    /// <summary>What produced it, when the catalog recognised a tool. Shown, never acted on.</summary>
    [JsonPropertyName("technology")]
    public string? Technology { get; set; }

    /// <summary>
    /// How the tool itself supports relocating this, if it does — "None", "Junction",
    /// "SymbolicLink" or whatever the catalog recorded.
    /// <para>
    /// A property of the technology rather than of the machine, which is why it travels: losing it
    /// makes a move fall back to a mechanism the tool never documented.
    /// </para>
    /// </summary>
    [JsonPropertyName("officialMethod")]
    public string OfficialMethod { get; set; } = "None";

    /// <summary>What to do when the tool offers nothing of its own.</summary>
    [JsonPropertyName("fallbackMethod")]
    public string FallbackMethod { get; set; } = "None";

    /// <summary>How the official method is actually configured, in the tool's own terms.</summary>
    [JsonPropertyName("methodHint")]
    public string? MethodHint { get; set; }

    /// <summary>Anything the catalog wants read before this is acted on.</summary>
    [JsonPropertyName("warning")]
    public string? Warning { get; set; }

    /// <summary>
    /// "RuleEngine" or "Ai", so a reader can tell a deterministic match from a model's suggestion.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "RuleEngine";

    /// <summary>
    /// What the rules permitted on the machine that produced this. Advice, never permission: the
    /// edition reading it applies its own rules before anything is offered.
    /// </summary>
    [JsonPropertyName("canDelete")]
    public bool CanDelete { get; set; }

    [JsonPropertyName("canMove")]
    public bool CanMove { get; set; }
}

/// <summary>The scanned run itself, as it travels.</summary>
public sealed class ArchiveScan
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The absolute root for an absolute archive; the folder's own name for a relative one. Named
    /// "root" rather than "rootPath" because in one case it is not a path at all.
    /// </summary>
    [JsonPropertyName("root")]
    public string Root { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>"quick" or "deep".</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "quick";

    /// <summary>"completed", "cancelled", "failed" or "imported".</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "completed";

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("totalFolders")]
    public int TotalFolders { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }
}
