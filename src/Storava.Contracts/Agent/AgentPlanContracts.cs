namespace Storava.Contracts.Agent;

/// <summary>
/// Acting on several folders at once, with one approval covering all of them.
/// <para>
/// The single-item calls ask the user to type the folder's own name, which is a good gate for one
/// folder and no gate at all for twelve — typing one name says nothing about the other eleven. So a
/// plan is approved by a short code that the page displays and the user types back, and that code
/// is derived from every step in the plan. Add a folder, change a destination, switch a move from a
/// junction to a plain one, and the code changes, which makes an approval impossible to spend on a
/// set other than the one that was read.
/// </para>
/// <para>
/// Like the single-item calls this is two round trips: the first measures every folder as it is
/// <em>now</em> and hands back what would happen, the second performs it. Removal always means the
/// Recycle Bin; the interface underneath has no permanent delete.
/// </para>
/// </summary>
public sealed class AgentPlanRequest
{
    /// <summary>The walk the items came from. The Agent only acts on what it measured itself.</summary>
    public string ScanId { get; set; } = string.Empty;

    public List<AgentPlanItem> Items { get; set; } = [];
}

/// <summary>One folder in a plan, and what should happen to it.</summary>
public sealed class AgentPlanItem
{
    public string ItemId { get; set; } = string.Empty;

    /// <summary>"delete" or "move". Nothing else is a change to storage.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Required for a move, and it has to be on another drive.</summary>
    public string? DestinationPath { get; set; }

    /// <summary>
    /// <c>"junction"</c> or <c>"copy"</c>; absent means a junction. See
    /// <see cref="AgentActionRequest.MoveMethod"/> for why the difference matters.
    /// </summary>
    public string? MoveMethod { get; set; }
}

/// <summary>
/// What the whole plan would do, measured against the disk as it is now rather than as the scan
/// recorded it.
/// </summary>
public sealed record AgentPlanPreview(
    string PlanId,
    IReadOnlyList<AgentPlanStep> Steps,
    /// <summary>How much would be freed if every runnable step succeeds.</summary>
    long TotalBytes,
    /// <summary>How many steps would actually run. Refused ones are listed but not counted here.</summary>
    int RunnableCount,
    /// <summary>The exact text the user has to type back. Read off the screen, never remembered.</summary>
    string ConfirmationPhrase,
    /// <summary>Bound to every step, so an approval cannot survive a change to any of them.</summary>
    string Fingerprint,
    /// <summary>Warnings about the plan as a whole, beyond each step's own.</summary>
    IReadOnlyList<string> Warnings);

/// <summary>One step of a plan, as the user sees it before approving anything.</summary>
public sealed record AgentPlanStep(
    string StepId,
    string ItemId,
    string Action,
    string SourcePath,
    string? DestinationPath,
    /// <summary>A fresh measurement, not the scan's figure.</summary>
    long MeasuredBytes,
    IReadOnlyList<string> Warnings,
    /// <summary>
    /// Set when this step cannot run. It is still listed rather than dropped: a folder silently
    /// missing from the plan is worse than one shown with the reason it was refused.
    /// </summary>
    string? RefusedReason = null,
    string? RefusedMessage = null)
{
    public bool CanRun => RefusedReason is null;
}

/// <summary>Spending one approval on the plan it was granted for.</summary>
public sealed class AgentPlanConfirmation
{
    public string PlanId { get; set; } = string.Empty;

    /// <summary>Echoed back from the preview the user read.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>The code shown on the preview, typed by hand.</summary>
    public string TypedPhrase { get; set; } = string.Empty;
}

/// <summary>What actually happened, step by step.</summary>
public sealed record AgentPlanOutcome(
    string PlanId,
    IReadOnlyList<AgentPlanStepOutcome> Steps,
    int SucceededCount,
    int FailedCount,
    int SkippedCount,
    long TotalBytesFreed);

/// <summary>What happened to one folder, in the same terms the desktop execution log records.</summary>
public sealed record AgentPlanStepOutcome(
    string StepId,
    string ItemId,
    string SourcePath,
    bool Succeeded,
    string Status,
    long BytesFreed,
    /// <summary>Where the original went, so the user can find it in the Recycle Bin.</summary>
    string? RecycledPath,
    /// <summary>Set when a junction was left at the old location.</summary>
    string? LinkPath,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>Paths for the batch acting surface.</summary>
public static class AgentPlanPaths
{
    public const string Preview = "/v1/plans/preview";
    public const string Execute = "/v1/plans/execute";
}
