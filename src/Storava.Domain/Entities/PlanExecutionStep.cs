using Storava.Domain.Enums;

namespace Storava.Domain.Entities;

/// <summary>
/// The record of one plan step being carried out. Unlike a <see cref="StoragePlanEntry"/> — which
/// is an intention — this is an audit row: it says what was actually done to the disk, when, and
/// what the result was.
/// <para>
/// Every field that describes an irreversible act (<see cref="RecycledPath"/>,
/// <see cref="LinkPath"/>) is written *before* the act, so a crash mid-step still leaves a trace
/// the user can follow.
/// </para>
/// </summary>
public sealed class PlanExecutionStep
{
    public required string Id { get; init; }
    public required string ExecutionId { get; init; }

    /// <summary>The plan entry this run came from.</summary>
    public required string PlanEntryId { get; init; }

    public required string ScanItemId { get; init; }

    /// <summary>Where the folder was when the step started.</summary>
    public required string SourcePath { get; init; }

    public required string Title { get; init; }

    /// <summary>Only <see cref="SuggestedAction.Move"/> or <see cref="SuggestedAction.Delete"/>.</summary>
    public required SuggestedAction Action { get; init; }

    public MigrationMethod Method { get; init; } = MigrationMethod.None;

    /// <summary>
    /// False for a file. Carried from the plan entry rather than probed at execution time: what is
    /// on disk now decides how to copy, but what the user approved decides what they approved, and
    /// a folder replaced by a file between the two is exactly the substitution to refuse.
    /// </summary>
    public bool IsFolder { get; init; } = true;

    /// <summary>True when no rule recognised this item and the user chose the action themselves.</summary>
    public bool HasNoRule { get; init; }

    public int Order { get; init; }

    /// <summary>Chosen by the user for a move. Null for a delete.</summary>
    public string? DestinationPath { get; set; }

    public ExecutionStatus Status { get; set; } = ExecutionStatus.Pending;

    /// <summary>What the folder measured just before the step ran — the honest "freed" number.</summary>
    public long MeasuredBytes { get; set; }

    public long BytesFreed { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Set once the source has gone to the Recycle Bin, so a user can find it again.</summary>
    public string? RecycledPath { get; set; }

    /// <summary>Set once a junction or symbolic link has been left behind at the old location.</summary>
    public string? LinkPath { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsFinished => Status
        is ExecutionStatus.Completed
        or ExecutionStatus.Failed
        or ExecutionStatus.Skipped
        or ExecutionStatus.RolledBack
        or ExecutionStatus.Cancelled;

    /// <summary>
    /// True while the disk still holds an intermediate state for this step — a copy at the
    /// destination with the source not yet removed. This is what the rollback path looks for.
    /// </summary>
    public bool NeedsRecovery => Status == ExecutionStatus.Running;
}
