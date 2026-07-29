namespace Storava.Contracts.Agent;

/// <summary>
/// The only part of the Agent that changes anything on disk, and the shapes are written to make
/// that obvious.
/// <para>
/// Acting is deliberately two calls. The first measures the folder as it is <em>now</em> and hands
/// back a fingerprint of exactly what would happen; the second will only proceed if the user types
/// the folder's own name and echoes that same fingerprint. Change the destination between the two
/// and the fingerprint no longer matches, so an approval can never be spent on something other
/// than what was read.
/// </para>
/// <para>
/// Removal always means the Recycle Bin. The interface underneath has no permanent-delete
/// operation at all, so no request here can destroy data outright.
/// </para>
/// </summary>
public sealed class AgentActionRequest
{
    /// <summary>The walk the item came from. The Agent only acts on something it measured itself.</summary>
    public string ScanId { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    /// <summary>"delete" or "move". Nothing else is a change to storage.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Required for a move, and it has to be on another drive.</summary>
    public string? DestinationPath { get; set; }

    /// <summary>
    /// How a move should leave the old location: <c>"junction"</c> to put an NTFS directory
    /// junction there, or <c>"copy"</c> to move the folder and leave nothing behind.
    /// <para>
    /// The difference matters more than it sounds. A junction means every path that pointed at the
    /// folder still works — a build that hard-codes it, a launcher, a config file written years
    /// ago — because Windows follows the link. A plain move frees exactly the same space and breaks
    /// all of them. The Agent used to decide this for the user and always chose the junction.
    /// </para>
    /// <para>
    /// Absent means junction, which is what the Agent has always done. Ignored for a delete.
    /// </para>
    /// </summary>
    public string? MoveMethod { get; set; }
}

/// <summary>The two ways a move can leave the old location, as they travel over the channel.</summary>
public static class AgentMoveMethods
{
    /// <summary>Leave an NTFS directory junction, so the old path keeps working.</summary>
    public const string Junction = "junction";

    /// <summary>Move it and leave nothing behind.</summary>
    public const string Copy = "copy";
}

/// <summary>
/// What would happen, measured against the disk as it is now rather than as the scan recorded it.
/// </summary>
public sealed record AgentActionPreview(
    string StepId,
    string Action,
    string SourcePath,
    string? DestinationPath,
    /// <summary>A fresh measurement, not the scan's figure.</summary>
    long MeasuredBytes,
    /// <summary>The exact text the user has to type back to confirm.</summary>
    string ConfirmationPhrase,
    /// <summary>Bound to everything that changes what the step would do.</summary>
    string Fingerprint,
    IReadOnlyList<string> Warnings);

/// <summary>Spending an approval on the step it was granted for.</summary>
public sealed class AgentActionConfirmation
{
    public string StepId { get; set; } = string.Empty;

    /// <summary>Echoed back from the preview the user read.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>The folder's own name, typed by hand.</summary>
    public string TypedName { get; set; } = string.Empty;
}

/// <summary>What actually happened, in the same terms the desktop execution log records.</summary>
public sealed record AgentActionOutcome(
    bool Succeeded,
    string Status,
    long BytesFreed,
    /// <summary>Where the original went, so the user can find it in the Recycle Bin.</summary>
    string? RecycledPath,
    /// <summary>Set when a junction was left at the old location.</summary>
    string? LinkPath,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>Paths for the acting surface.</summary>
public static class AgentActionPaths
{
    public const string Preview = "/v1/actions/preview";
    public const string Execute = "/v1/actions/execute";
}
