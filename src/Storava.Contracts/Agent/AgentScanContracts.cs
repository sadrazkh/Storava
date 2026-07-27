using System.Text.Json.Serialization;

namespace Storava.Contracts.Agent;

/// <summary>
/// The wire shapes for what an Agent can do once a page has connected: list the machine's drives,
/// walk one, and hand back what it found.
/// <para>
/// These carry real operating-system paths, which is the whole reason the Agent exists — and the
/// reason none of it goes anywhere near the account server. They cross one hop, from a process on
/// this machine to a page on this machine.
/// </para>
/// </summary>
public sealed record AgentDrive(
    string Name,
    string? VolumeLabel,
    string DriveFormat,
    long TotalBytes,
    long FreeBytes,
    bool IsReady);

/// <summary>What the page asks the Agent to walk.</summary>
public sealed class AgentScanRequest
{
    /// <summary>An absolute path on this machine. Anything else is refused.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>"quick" or "deep"; deep also reads each file's size on disk.</summary>
    public string Mode { get; set; } = "quick";
}

/// <summary>
/// Serialized by name, declared here rather than left to whatever options a caller happens to
/// have. The page compares against string literals: as integers every comparison would silently
/// fail and a finished walk would look like one that never ends.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AgentScanState>))]
public enum AgentScanState
{
    Running = 0,
    Completed,
    Cancelled,
    Failed
}

/// <summary>
/// Where a walk has got to. Polled rather than streamed: the numbers are cumulative, so a missed
/// update costs nothing and there is no stream to leak or reconnect.
/// </summary>
public sealed record AgentScanProgress(
    string ScanId,
    AgentScanState State,
    string RootPath,
    string CurrentPath,
    long Files,
    long Folders,
    long Bytes,
    int Errors,
    double ElapsedSeconds,
    string? Error);

/// <summary>One item a walk found, with the path only a local process can give.</summary>
public sealed record AgentScanItem(
    string Id,
    string Path,
    string Name,
    bool IsFolder,
    long Size,
    int FileCount,
    int FolderCount,
    string Category,
    string? Technology,
    string? RuleId,
    string Risk,
    bool IsProtected,
    bool IsReparsePoint,
    /// <summary>What the local rule catalog permits. The page offers nothing these deny.</summary>
    bool CanDelete,
    bool CanMove);

/// <summary>A page of results, largest first.</summary>
public sealed record AgentScanItems(string ScanId, IReadOnlyList<AgentScanItem> Items);

/// <summary>A refusal the page can act on, rather than a bare status code.</summary>
public sealed record AgentProblem(string Reason, string Message);

/// <summary>Paths under the Agent's scanning surface, so both sides spell them once.</summary>
public static class AgentScanPaths
{
    public const string Drives = "/v1/drives";
    public const string Scans = "/v1/scans";

    public static string Scan(string scanId) => $"{Scans}/{scanId}";
    public static string Cancel(string scanId) => $"{Scans}/{scanId}/cancel";
    public static string Items(string scanId) => $"{Scans}/{scanId}/items";

    /// <summary>Downloads the whole walk as a portable <c>.storava</c> archive.</summary>
    public static string Archive(string scanId) => $"{Scans}/{scanId}/archive";
}
