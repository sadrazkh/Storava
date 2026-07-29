using Storava.Domain.Common;
using Storava.Domain.Enums;

namespace Storava.Domain.Entities;

/// <summary>
/// One attempt at carrying out a saved plan. A plan may be run more than once — a step that was
/// skipped or failed can be retried later — so the execution, not the plan, owns the history of
/// what happened to the disk.
/// <para>
/// The entity enforces the ordering rule that makes the run safe to interrupt: steps run strictly
/// one at a time, in the plan's safest-first order, and the next one cannot start while another is
/// still <see cref="ExecutionStatus.Running"/>. That invariant is what lets a crash be recovered
/// from a single row.
/// </para>
/// </summary>
public sealed class PlanExecution
{
    private readonly List<PlanExecutionStep> _steps = [];

    public required string Id { get; init; }
    public required string PlanId { get; init; }
    public required string SessionId { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }

    public IReadOnlyList<PlanExecutionStep> Steps => _steps;

    public int CompletedCount => _steps.Count(s => s.Status == ExecutionStatus.Completed);

    public int FailedCount => _steps.Count(s => s.Status is ExecutionStatus.Failed or ExecutionStatus.RolledBack);

    public int SkippedCount => _steps.Count(s => s.Status == ExecutionStatus.Skipped);

    public int PendingCount => _steps.Count(s => !s.IsFinished);

    /// <summary>Only what actually left the drive — a skipped or failed step contributes nothing.</summary>
    public long TotalBytesFreed => _steps
        .Where(s => s.Status == ExecutionStatus.Completed)
        .Sum(s => s.BytesFreed);

    public bool IsFinished => _steps.Count > 0 && _steps.All(s => s.IsFinished);

    /// <summary>The step a crash would have left half-done, if there is one.</summary>
    public PlanExecutionStep? StepNeedingRecovery => _steps.FirstOrDefault(s => s.NeedsRecovery);

    /// <summary>The next step to offer the user, in plan order. Null once nothing is left.</summary>
    public PlanExecutionStep? NextPending => _steps
        .Where(s => s.Status == ExecutionStatus.Pending)
        .OrderBy(s => s.Order)
        .FirstOrDefault();

    public PlanExecutionStep? FindStep(string stepId) =>
        _steps.FirstOrDefault(s => string.Equals(s.Id, stepId, StringComparison.Ordinal));

    public void Add(PlanExecutionStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(step);
    }

    /// <summary>Restores a run read back from storage, in plan order.</summary>
    public void Load(IEnumerable<PlanExecutionStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        _steps.Clear();
        _steps.AddRange(steps.OrderBy(s => s.Order));
    }

    /// <summary>
    /// Marks a step as started, refusing if another one is still in flight. Two concurrent steps
    /// could target nested paths, and the second would then be acting on a folder the first has
    /// already moved out from under it.
    /// </summary>
    public Result Begin(PlanExecutionStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (_steps.Any(s => s.Status == ExecutionStatus.Running))
            return Result.Failure(ExecutionErrors.AnotherStepRunning);

        if (step.Status != ExecutionStatus.Pending)
            return Result.Failure(ExecutionErrors.StepNotPending);

        step.Status = ExecutionStatus.Running;
        step.StartedAt = DateTimeOffset.Now;
        return Result.Success();
    }

    public void Finish(PlanExecutionStep step, ExecutionStatus status, Error? error = null)
    {
        ArgumentNullException.ThrowIfNull(step);

        step.Status = status;
        step.CompletedAt = DateTimeOffset.Now;

        if (error is not null && error != Error.None)
        {
            step.ErrorCode = error.Code;
            step.ErrorMessage = error.Message;
        }

        if (IsFinished)
            CompletedAt = DateTimeOffset.Now;
    }
}

/// <summary>Why a run, or one of its steps, could not proceed. Each code has a localized message.</summary>
public static class ExecutionErrors
{
    public static readonly Error AnotherStepRunning =
        new("exec.another_running", "Another step is still running.");

    public static readonly Error StepNotPending =
        new("exec.not_pending", "This step has already been dealt with.");

    public static readonly Error NotConfirmed =
        new("exec.not_confirmed", "This step has not been confirmed.");

    public static readonly Error ConfirmationStale =
        new("exec.confirmation_stale", "Something changed after you confirmed. Confirm again.");

    public static readonly Error ProtectedPath =
        new("exec.protected_path", "This is a protected system location and can never be acted on.");

    public static readonly Error SourceMissing =
        new("exec.source_missing", "The folder is no longer there.");

    public static readonly Error SourceIsLink =
        new("exec.source_is_link", "The folder is a junction or symbolic link, not real storage.");

    public static readonly Error DestinationRequired =
        new("exec.destination_required", "Choose where the folder should go.");

    public static readonly Error DestinationInvalid =
        new("exec.destination_invalid", "That destination cannot be used.");

    public static readonly Error DestinationNotEmpty =
        new("exec.destination_not_empty", "The destination folder already exists and is not empty.");

    public static readonly Error DestinationInsideSource =
        new("exec.destination_inside_source", "The destination is inside the folder being moved.");

    public static readonly Error DestinationSameVolume =
        new("exec.destination_same_volume", "The destination is on the same drive, so nothing would be freed.");

    public static readonly Error NotEnoughSpace =
        new("exec.not_enough_space", "There is not enough free space at the destination.");

    public static readonly Error ActionNotPermitted =
        new("exec.action_not_permitted", "The local rules do not permit this action on this item.");

    public static readonly Error CopyFailed =
        new("exec.copy_failed", "The folder could not be copied to the destination.");

    public static readonly Error VerificationFailed =
        new("exec.verification_failed", "The copy did not match the original, so the original was left alone.");

    public static readonly Error RecycleFailed =
        new("exec.recycle_failed", "The folder could not be sent to the Recycle Bin.");

    /// <summary>
    /// Something still had a file inside the folder open. By far the most common way a move fails,
    /// and the one a person can actually do something about — so it says so instead of leaving them
    /// to guess why the work was undone.
    /// </summary>
    public static readonly Error RecycleSourceInUse =
        new("exec.recycle_in_use", "Another program is still using something inside this folder.");

    /// <summary>Windows refused on permission grounds, which no amount of closing programs will fix.</summary>
    public static readonly Error RecycleAccessDenied =
        new("exec.recycle_access_denied", "Windows would not allow this folder to be removed.");

    public static readonly Error LinkFailed =
        new("exec.link_failed", "The folder was moved but the link back to the old location could not be created.");

    public static readonly Error Cancelled =
        new("exec.cancelled", "The step was cancelled.");
}
