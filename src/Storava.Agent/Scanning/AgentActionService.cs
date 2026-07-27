using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Contracts.Agent;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Migrations;

namespace Storava.Agent.Scanning;

/// <summary>
/// The one part of the Agent that changes the disk, and it changes nothing the desktop edition
/// would not.
/// <para>
/// Every safeguard here belongs to code that already existed: <see cref="ExecutionGuard"/> decides
/// whether a step may run, <see cref="StepConfirmation"/> binds the approval to what the user read,
/// and <see cref="PlanExecutionService"/> performs the copy-verify-recycle ordering that makes an
/// interrupted move recoverable. None of it was re-implemented for the browser, because a second
/// implementation is a second place for the rules to drift.
/// </para>
/// <para>
/// On top of that this adds one constraint of its own: the Agent will only act on an item from a
/// walk it performed. A page cannot name an arbitrary path — it can only point at something the
/// Agent measured and the rule catalog already judged.
/// </para>
/// </summary>
public sealed class AgentActionService(
    AgentScanService scans,
    PlanExecutionService executor,
    IPlanExecutionRepository executions,
    IFileSystemActions fileSystem,
    ExecutionGuard guard,
    IScanQueryService query,
    ILogger<AgentActionService> logger)
{
    /// <summary>
    /// Prepared steps waiting for their approval. Held in memory only: an approval that outlived
    /// the process would be one nobody is still looking at.
    /// </summary>
    private readonly ConcurrentDictionary<string, PendingStep> _pending = new(StringComparer.Ordinal);

    public sealed record PrepareResult(AgentActionPreview? Preview, AgentProblem? Problem)
    {
        public static PrepareResult Refused(string reason, string message) =>
            new(null, new AgentProblem(reason, message));
    }

    public async Task<PrepareResult> PrepareAsync(
        AgentActionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseAction(request.Action, out var action))
            return PrepareResult.Refused("bad_action", "Only 'delete' and 'move' change storage.");

        string? sessionId = scans.ResolveCompletedSession(request.ScanId);
        if (sessionId is null)
        {
            return PrepareResult.Refused(
                "unknown_scan",
                "That walk is not one this agent finished, so it has nothing to act on.");
        }

        var item = await query.GetByIdAsync(sessionId, request.ItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
            return PrepareResult.Refused("unknown_item", "That item was not part of the walk.");

        // Directory operations throughout: measuring, copying and verifying are all folder-shaped.
        if (!item.IsFolder)
            return PrepareResult.Refused("not_a_folder", "Only folders can be moved or deleted.");

        if (item.IsProtected || item.RiskLevel == RiskLevel.Protected)
            return PrepareResult.Refused("protected", "This is a protected system location and can never be acted on.");

        // The same limit the desktop plan enforces: the rule catalog decides what is permitted and
        // nothing downstream may widen it.
        if (action == SuggestedAction.Delete && !item.CanDelete)
            return PrepareResult.Refused("not_permitted", "The local rules do not permit deleting this item.");

        if (action == SuggestedAction.Move && !item.CanMove)
            return PrepareResult.Refused("not_permitted", "The local rules do not permit moving this item.");

        var source = guard.ValidateSource(item.Path);
        if (source.IsFailure)
            return PrepareResult.Refused(source.Error.Code, source.Error.Message);

        // Measured now, not read from the scan: the number the user approves has to be the disk's.
        var measured = await fileSystem.MeasureAsync(item.Path, cancellationToken).ConfigureAwait(false);
        if (measured.IsFailure)
            return PrepareResult.Refused(measured.Error.Code, measured.Error.Message);

        string? destination = action == SuggestedAction.Move
            ? (request.DestinationPath ?? string.Empty).Trim()
            : null;

        if (action == SuggestedAction.Move)
        {
            var allowed = guard.ValidateDestination(item.Path, destination, measured.Value.Bytes);
            if (allowed.IsFailure)
                return PrepareResult.Refused(allowed.Error.Code, allowed.Error.Message);
        }

        var execution = new PlanExecution
        {
            Id = Guid.NewGuid().ToString("n"),
            // The Agent has no saved plan; the run is the record, and it lands in the same
            // execution log the desktop History page reads.
            PlanId = "agent",
            SessionId = sessionId
        };

        var step = new PlanExecutionStep
        {
            Id = Guid.NewGuid().ToString("n"),
            ExecutionId = execution.Id,
            PlanEntryId = "agent",
            ScanItemId = item.Id,
            SourcePath = item.Path,
            Title = item.Name,
            Action = action,
            Method = action == SuggestedAction.Move ? MigrationMethod.Junction : MigrationMethod.None,
            Order = 0,
            MeasuredBytes = measured.Value.Bytes,
            DestinationPath = destination,
            Status = ExecutionStatus.Pending
        };

        execution.Add(step);
        await executions.SaveAsync(execution, cancellationToken).ConfigureAwait(false);

        _pending[step.Id] = new PendingStep(execution, step);

        var warnings = new List<string>();
        if (item.Size > 0)
        {
            double ratio = (double)measured.Value.Bytes / item.Size;
            if (ratio > 1.1) warnings.Add("grew_since_scan");
            else if (ratio < 0.9) warnings.Add("shrank_since_scan");
        }

        if (item.RiskLevel == RiskLevel.High) warnings.Add("high_risk");
        if (action == SuggestedAction.Move) warnings.Add("junction_left_behind");

        logger.LogInformation("Prepared a {Action} step for review.", action);

        return new PrepareResult(
            new AgentActionPreview(
                step.Id,
                action.ToString().ToLowerInvariant(),
                step.SourcePath,
                step.DestinationPath,
                step.MeasuredBytes,
                ExecutionGuard.GetLeafName(step.SourcePath),
                StepConfirmation.Compute(step),
                warnings),
            null);
    }

    public async Task<AgentActionOutcome?> ExecuteAsync(
        AgentActionConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove(confirmation.StepId, out var pending))
            return null;

        // Every check that matters happens inside the guard: the fingerprint must match the step as
        // it stands, the typed name must be the folder's own, the source must still be there and
        // unprotected, and a move's destination must still hold. Passing the browser's values
        // straight in keeps this method from becoming a second, weaker gate.
        var result = await executor.ExecuteStepAsync(
            pending.Execution,
            pending.Step,
            new StepConfirmation
            {
                StepId = confirmation.StepId,
                Fingerprint = confirmation.Fingerprint,
                TypedName = confirmation.TypedName
            },
            progress: null,
            cancellationToken).ConfigureAwait(false);

        var step = pending.Step;

        if (result.IsFailure && step.Status == ExecutionStatus.Pending)
        {
            // Refused before anything was attempted, so the step is still offerable. Put it back
            // rather than making the user prepare it again.
            _pending[step.Id] = pending;
        }

        logger.LogInformation("An agent step finished as {Status}.", step.Status);

        return new AgentActionOutcome(
            result.IsSuccess,
            step.Status.ToString(),
            step.BytesFreed,
            step.RecycledPath,
            step.LinkPath,
            result.IsFailure ? result.Error.Code : step.ErrorCode,
            result.IsFailure ? result.Error.Message : step.ErrorMessage);
    }

    private static bool TryParseAction(string? value, out SuggestedAction action)
    {
        if (string.Equals(value, "delete", StringComparison.OrdinalIgnoreCase))
        {
            action = SuggestedAction.Delete;
            return true;
        }

        if (string.Equals(value, "move", StringComparison.OrdinalIgnoreCase))
        {
            action = SuggestedAction.Move;
            return true;
        }

        action = SuggestedAction.NoAction;
        return false;
    }

    private sealed record PendingStep(PlanExecution Execution, PlanExecutionStep Step);
}
