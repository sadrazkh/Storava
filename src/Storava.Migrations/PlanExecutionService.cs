using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Migration;
using Storava.Domain.Common;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Migrations.Preflight;

namespace Storava.Migrations;

/// <summary>
/// Carries out a saved plan, one confirmed step at a time.
/// <para>
/// The order of operations is the whole design. For a move the copy happens first and is verified
/// against a fresh measurement of the source; only then does the original go to the Recycle Bin.
/// At no point do both copies fail to exist, so an interruption anywhere leaves the user's data
/// somewhere they can reach it. For a delete there is no copy step because the Recycle Bin *is*
/// the copy.
/// </para>
/// </summary>
public sealed class PlanExecutionService
{
    private readonly ExecutionGuard _guard;
    private readonly IFileSystemActions _fileSystem;
    private readonly IPlanExecutionRepository _executions;
    private readonly ILogger<PlanExecutionService> _logger;

    public PlanExecutionService(
        ExecutionGuard guard,
        IFileSystemActions fileSystem,
        IPlanExecutionRepository executions,
        ILogger<PlanExecutionService> logger)
    {
        _guard = guard;
        _fileSystem = fileSystem;
        _executions = executions;
        _logger = logger;
    }

    /// <summary>
    /// Re-checks every step of a plan against the disk as it is now, without changing anything.
    /// This is the dry run: what it reports is exactly what execution would attempt.
    /// </summary>
    public async Task<PreflightReport> PreflightAsync(
        StoragePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var results = new List<StepPreflight>();

        foreach (var entry in plan.Entries.OrderBy(e => e.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await PreflightEntryAsync(entry, cancellationToken).ConfigureAwait(false));
        }

        return new PreflightReport { Steps = results };
    }

    private async Task<StepPreflight> PreflightEntryAsync(
        StoragePlanEntry entry,
        CancellationToken cancellationToken)
    {
        var source = _guard.ValidateSource(entry.Path);
        if (source.IsFailure)
            return new StepPreflight { Entry = entry, Blocker = source.Error };

        // A step nested inside another frees nothing extra; it is reported, not silently dropped.
        if (entry.IsCovered)
        {
            return new StepPreflight
            {
                Entry = entry,
                Blocker = Error.None,
                MeasuredBytes = 0,
                Warnings = [PreflightWarnings.CoveredByAnotherStep]
            };
        }

        var measured = await _fileSystem.MeasureAsync(entry.Path, cancellationToken).ConfigureAwait(false);
        if (measured.IsFailure)
            return new StepPreflight { Entry = entry, Blocker = measured.Error };

        long bytes = measured.Value.Bytes;
        var warnings = new List<Error>();

        // A tenth either way is normal churn for a cache; more than that is worth saying out loud,
        // because the number the user approved came from the scan.
        if (entry.EstimatedSpace > 0)
        {
            double ratio = (double)bytes / entry.EstimatedSpace;
            if (ratio > 1.1)
                warnings.Add(PreflightWarnings.GrewSinceScan);
            else if (ratio < 0.9)
                warnings.Add(PreflightWarnings.ShrankSinceScan);
        }

        if (entry.Action == SuggestedAction.Move && entry.Method != MigrationMethod.OfficialSetting)
            warnings.Add(PreflightWarnings.NoOfficialMethod);

        if (entry.RiskLevel == RiskLevel.High)
            warnings.Add(PreflightWarnings.HighRisk);

        return new StepPreflight
        {
            Entry = entry,
            Blocker = Error.None,
            MeasuredBytes = bytes,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Turns the runnable half of a preflight into a run that can be executed. Blocked steps are
    /// recorded as skipped rather than left out, so the run accounts for every step of the plan.
    /// </summary>
    public async Task<PlanExecution> CreateExecutionAsync(
        StoragePlan plan,
        PreflightReport preflight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(preflight);

        var execution = new PlanExecution
        {
            Id = Guid.NewGuid().ToString("n"),
            PlanId = plan.Id,
            SessionId = plan.SessionId
        };

        foreach (var result in preflight.Steps)
        {
            var entry = result.Entry;
            var step = new PlanExecutionStep
            {
                Id = Guid.NewGuid().ToString("n"),
                ExecutionId = execution.Id,
                PlanEntryId = entry.Id,
                ScanItemId = entry.ScanItemId,
                SourcePath = entry.Path,
                Title = entry.Title,
                Action = entry.Action,
                Method = entry.Method,
                Order = entry.Order,
                MeasuredBytes = result.MeasuredBytes,
                Status = result.CanRun ? ExecutionStatus.Pending : ExecutionStatus.Skipped
            };

            if (!result.CanRun)
            {
                step.ErrorCode = result.Blocker.Code;
                step.ErrorMessage = result.Blocker.Message;
                step.CompletedAt = DateTimeOffset.Now;
            }

            execution.Add(step);
        }

        await _executions.SaveAsync(execution, cancellationToken).ConfigureAwait(false);
        return execution;
    }

    /// <summary>The user passed over a step. Nothing is touched and the run moves on.</summary>
    public async Task SkipAsync(
        PlanExecution execution,
        PlanExecutionStep step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(step);

        execution.Finish(step, ExecutionStatus.Skipped);
        await _executions.SaveStepAsync(step, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one confirmed step. Returns failure rather than throwing for anything the user could
    /// have caused; only a bug throws.
    /// </summary>
    public async Task<Result> ExecuteStepAsync(
        PlanExecution execution,
        PlanExecutionStep step,
        StepConfirmation confirmation,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(step);

        // Measured again here, not reused from preflight: the gate has to judge the disk as it is
        // at this instant, and the free-space check depends on this number.
        var measured = await _fileSystem.MeasureAsync(step.SourcePath, cancellationToken).ConfigureAwait(false);
        if (measured.IsFailure)
            return await FailAsync(execution, step, measured.Error, cancellationToken).ConfigureAwait(false);

        var allowed = _guard.ValidateForExecution(step, confirmation, measured.Value.Bytes);
        if (allowed.IsFailure)
        {
            // A refused step is not a failed one — nothing was attempted, so it stays retryable.
            _logger.LogWarning("A step was refused before it ran: {Code}.", allowed.Error.Code);
            return allowed;
        }

        var begun = execution.Begin(step);
        if (begun.IsFailure)
            return begun;

        step.MeasuredBytes = measured.Value.Bytes;

        // Persisted while the status is still Running, so an abrupt exit leaves the row that the
        // recovery path keys off. Everything below this line can touch the disk.
        await _executions.SaveStepAsync(step, cancellationToken).ConfigureAwait(false);

        try
        {
            return step.Action switch
            {
                SuggestedAction.Delete => await ExecuteDeleteAsync(execution, step, cancellationToken)
                    .ConfigureAwait(false),
                SuggestedAction.Move => await ExecuteMoveAsync(
                    execution, step, measured.Value, progress, cancellationToken).ConfigureAwait(false),
                _ => await FailAsync(execution, step, ExecutionErrors.ActionNotPermitted, cancellationToken)
                    .ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException)
        {
            return await FailAsync(execution, step, ExecutionErrors.Cancelled, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A plan step threw while running.");
            return await FailAsync(
                execution, step, Error.Unexpected(ex.Message), cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task<Result> ExecuteDeleteAsync(
        PlanExecution execution,
        PlanExecutionStep step,
        CancellationToken cancellationToken)
    {
        // Recorded before the call: if the process dies mid-operation, the row still points at
        // where the folder went.
        step.RecycledPath = step.SourcePath;
        await _executions.SaveStepAsync(step, cancellationToken).ConfigureAwait(false);

        var recycled = await _fileSystem.MoveToRecycleBinAsync(step.SourcePath, cancellationToken).ConfigureAwait(false);
        if (recycled.IsFailure)
        {
            step.RecycledPath = null;
            return await FailAsync(execution, step, ExecutionErrors.RecycleFailed, CancellationToken.None)
                .ConfigureAwait(false);
        }

        step.BytesFreed = step.MeasuredBytes;
        execution.Finish(step, ExecutionStatus.Completed);
        await _executions.SaveStepAsync(step, CancellationToken.None).ConfigureAwait(false);

        _logger.LogInformation("Deleted a planned folder to the Recycle Bin, freeing {Bytes} bytes.", step.BytesFreed);
        return Result.Success();
    }

    private async Task<Result> ExecuteMoveAsync(
        PlanExecution execution,
        PlanExecutionStep step,
        DirectoryFacts sourceFacts,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        string destination = step.DestinationPath!;

        // 1. Copy. The source is untouched throughout, so a failure here costs nothing but time.
        Result copied;
        try
        {
            copied = await _fileSystem
                .CopyDirectoryAsync(step.SourcePath, destination, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caught here rather than at the top of ExecuteStepAsync: a cancelled copy has usually
            // written something, and only this scope knows where to clean it up.
            copied = Result.Failure(ExecutionErrors.Cancelled);
        }

        if (copied.IsFailure)
        {
            await DiscardCopyAsync(destination).ConfigureAwait(false);

            var reason = copied.Error == ExecutionErrors.Cancelled || cancellationToken.IsCancellationRequested
                ? ExecutionErrors.Cancelled
                : ExecutionErrors.CopyFailed;

            return await FailAsync(execution, step, reason, CancellationToken.None, ExecutionStatus.RolledBack)
                .ConfigureAwait(false);
        }

        // 2. Verify against a real measurement of what landed, not against the copier's own report.
        var destinationFacts = await _fileSystem.MeasureAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (destinationFacts.IsFailure || !destinationFacts.Value.Matches(sourceFacts))
        {
            _logger.LogWarning(
                "A move was abandoned because the copy did not match: expected {Bytes} bytes in {Files} files.",
                sourceFacts.Bytes, sourceFacts.FileCount);

            await DiscardCopyAsync(destination).ConfigureAwait(false);
            return await FailAsync(execution, step, ExecutionErrors.VerificationFailed, CancellationToken.None,
                ExecutionStatus.RolledBack).ConfigureAwait(false);
        }

        // 3. Only now is the original expendable — a verified copy exists.
        step.RecycledPath = step.SourcePath;
        await _executions.SaveStepAsync(step, CancellationToken.None).ConfigureAwait(false);

        var recycled = await _fileSystem.MoveToRecycleBinAsync(step.SourcePath, CancellationToken.None)
            .ConfigureAwait(false);

        if (recycled.IsFailure)
        {
            // The source is still in place, so the copy is the redundant one. Removing it puts the
            // machine back exactly where it started.
            step.RecycledPath = null;
            await DiscardCopyAsync(destination).ConfigureAwait(false);
            return await FailAsync(execution, step, ExecutionErrors.RecycleFailed, CancellationToken.None,
                ExecutionStatus.RolledBack).ConfigureAwait(false);
        }

        step.BytesFreed = sourceFacts.Bytes;

        // 4. Leave a link behind so anything hard-coded to the old path keeps working.
        if (step.Method is MigrationMethod.Junction or MigrationMethod.SymbolicLink)
        {
            var link = _fileSystem.CreateDirectoryLink(step.SourcePath, destination, step.Method);
            if (link.IsSuccess)
            {
                step.LinkPath = step.SourcePath;
            }
            else
            {
                // The space *was* freed and the data *is* safe, so calling this a failure would be
                // a lie. It is recorded on a completed step and surfaced as a warning instead.
                step.ErrorCode = ExecutionErrors.LinkFailed.Code;
                step.ErrorMessage = ExecutionErrors.LinkFailed.Message;
                _logger.LogWarning("A folder was moved but the link back could not be created.");
            }
        }

        execution.Finish(step, ExecutionStatus.Completed);
        await _executions.SaveStepAsync(step, CancellationToken.None).ConfigureAwait(false);

        _logger.LogInformation("Moved a planned folder, freeing {Bytes} bytes.", step.BytesFreed);
        return Result.Success();
    }

    /// <summary>
    /// Removes a copy Storava made itself. It goes to the Recycle Bin like everything else — the
    /// app has no permanent-delete capability at all, not even for its own leftovers.
    /// </summary>
    private async Task DiscardCopyAsync(string destination)
    {
        if (!_fileSystem.DirectoryExists(destination))
            return;

        var discarded = await _fileSystem.MoveToRecycleBinAsync(destination, CancellationToken.None)
            .ConfigureAwait(false);

        if (discarded.IsFailure)
            _logger.LogWarning("A partial copy could not be cleaned up and was left at the destination.");
    }

    private async Task<Result> FailAsync(
        PlanExecution execution,
        PlanExecutionStep step,
        Error error,
        CancellationToken cancellationToken,
        ExecutionStatus status = ExecutionStatus.Failed)
    {
        execution.Finish(step, status, error);
        await _executions.SaveStepAsync(step, cancellationToken).ConfigureAwait(false);
        return Result.Failure(error);
    }

    /// <summary>
    /// Settles a step that was left <see cref="ExecutionStatus.Running"/> by a crash, by reading
    /// the disk rather than guessing. The three states are distinguishable because the source is
    /// only ever removed *after* a verified copy exists.
    /// </summary>
    public async Task<Result> RecoverAsync(
        PlanExecution execution,
        PlanExecutionStep step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(step);

        if (!step.NeedsRecovery)
            return Result.Success();

        bool sourceExists = _fileSystem.DirectoryExists(step.SourcePath);
        bool destinationExists = !string.IsNullOrWhiteSpace(step.DestinationPath)
                                 && _fileSystem.DirectoryExists(step.DestinationPath);

        if (step.Action == SuggestedAction.Delete)
        {
            // The Recycle Bin either took it or it did not; there is no half state.
            execution.Finish(
                step,
                sourceExists ? ExecutionStatus.Failed : ExecutionStatus.Completed,
                sourceExists ? ExecutionErrors.RecycleFailed : null);

            if (!sourceExists)
                step.BytesFreed = step.MeasuredBytes;
        }
        else if (sourceExists && destinationExists)
        {
            // The copy never got as far as removing the original, so the copy is the leftover.
            await DiscardCopyAsync(step.DestinationPath!).ConfigureAwait(false);
            execution.Finish(step, ExecutionStatus.RolledBack, ExecutionErrors.CopyFailed);
        }
        else if (!sourceExists && destinationExists)
        {
            // The source was only removed after verification, so the move did complete.
            step.BytesFreed = step.MeasuredBytes;
            execution.Finish(step, ExecutionStatus.Completed);
        }
        else if (sourceExists)
        {
            execution.Finish(step, ExecutionStatus.RolledBack, ExecutionErrors.CopyFailed);
        }
        else
        {
            // Neither path is there. Storava cannot have caused this: it removes the source only
            // once the destination holds a verified copy.
            execution.Finish(step, ExecutionStatus.Failed, ExecutionErrors.SourceMissing);
        }

        await _executions.SaveStepAsync(step, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("An interrupted step was settled as {Status}.", step.Status);
        return Result.Success();
    }
}
