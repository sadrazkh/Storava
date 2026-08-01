using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Contracts.Agent;
using Storava.Domain.Common;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Migrations;

namespace Storava.Agent.Scanning;

/// <summary>
/// Acting on several folders under one approval.
/// <para>
/// Every safeguard here is the one the single-item path already uses:
/// <see cref="ExecutionGuard"/> decides whether a step may run and
/// <see cref="PlanExecutionService"/> performs the copy-verify-recycle ordering that makes an
/// interrupted move recoverable. What changes is only where the approval sits — on the plan rather
/// than on each folder — because typing one folder's name says nothing about the eleven beside it.
/// </para>
/// </summary>
public sealed class AgentPlanService(
    AgentScanService scans,
    PlanExecutionService executor,
    IPlanExecutionRepository executions,
    IFileSystemActions fileSystem,
    ExecutionGuard guard,
    IScanQueryService query,
    ILogger<AgentPlanService> logger)
{
    /// <summary>
    /// The most folders one plan may cover.
    /// <para>
    /// Not a technical limit. A list nobody will read to the end is not something a person can
    /// meaningfully approve in one go, and this is the call that approves everything at once.
    /// </para>
    /// </summary>
    public const int MaximumItems = 50;

    /// <summary>
    /// Prepared plans waiting for their approval. Held in memory only: an approval that outlived
    /// the process would be one nobody is still looking at.
    /// </summary>
    private readonly ConcurrentDictionary<string, PendingPlan> _pending = new(StringComparer.Ordinal);

    public sealed record PrepareResult(AgentPlanPreview? Preview, AgentProblem? Problem)
    {
        public static PrepareResult Refused(string reason, string message) =>
            new(null, new AgentProblem(reason, message));
    }

    public async Task<PrepareResult> PrepareAsync(AgentPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Items.Count == 0)
            return PrepareResult.Refused("empty_plan", "A plan has to name at least one folder.");

        if (request.Items.Count > MaximumItems)
        {
            return PrepareResult.Refused(
                "too_many_items",
                $"A plan covers at most {MaximumItems} folders, so that what is being approved can still be read.");
        }

        string? sessionId = scans.ResolveCompletedSession(request.ScanId);
        if (sessionId is null)
        {
            return PrepareResult.Refused(
                "unknown_scan",
                "That walk is not one this agent finished, so it has nothing to act on.");
        }

        // The same folder twice would be measured once and acted on twice, and the second attempt
        // would find nothing there. Caught here rather than left to fail halfway through a run.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in request.Items)
        {
            if (!seen.Add(item.ItemId))
                return PrepareResult.Refused("duplicate_item", "The same folder appears in the plan more than once.");
        }

        var execution = new PlanExecution
        {
            Id = Guid.NewGuid().ToString("n"),
            // The Agent has no saved plan; the run is the record, and it lands in the same
            // execution log the desktop History page reads.
            PlanId = "agent-plan",
            SessionId = sessionId
        };

        var steps = new List<AgentPlanStep>(request.Items.Count);
        var order = 0;

        foreach (var requested in request.Items)
        {
            var prepared = await PrepareStepAsync(execution, sessionId, requested, order, cancellationToken)
                .ConfigureAwait(false);

            steps.Add(prepared);
            order++;
        }

        // Only the runnable steps carry the approval. A refused one is shown so the user knows it
        // was left out, but it can never run, so letting it move the code would be noise.
        var runnable = execution.Steps.Where(step => step.Status == ExecutionStatus.Pending).ToList();

        if (runnable.Count == 0)
        {
            return PrepareResult.Refused(
                "nothing_runnable",
                "None of these folders can be acted on. Each one's reason is listed beside it.");
        }

        await executions.SaveAsync(execution, cancellationToken).ConfigureAwait(false);

        var fingerprint = PlanConfirmation.ComputeFingerprint(runnable);
        _pending[execution.Id] = new PendingPlan(execution, fingerprint);

        var warnings = new List<string>();
        if (steps.Any(step => !step.CanRun))
            warnings.Add("some_steps_refused");

        if (runnable.Any(step => step.Action == SuggestedAction.Delete))
            warnings.Add("recycle_bin");

        logger.LogInformation(
            "Prepared an agent plan of {Runnable} runnable step(s) out of {Total} for review.",
            runnable.Count, steps.Count);

        return new PrepareResult(
            new AgentPlanPreview(
                execution.Id,
                steps,
                runnable.Sum(step => step.MeasuredBytes),
                runnable.Count,
                PlanConfirmation.ComputePhrase(fingerprint),
                fingerprint,
                warnings),
            null);
    }

    /// <summary>
    /// Measures one folder and adds it to the run, either as something to do or as something
    /// refused with its reason.
    /// <para>
    /// A refusal about a folder that really exists is written onto the run as a skipped step, the
    /// way the desktop's own planner does it, so the execution log accounts for every folder the
    /// user chose rather than only the ones that were tried. A request naming something that was
    /// never in the walk has no folder to record and is only reported back.
    /// </para>
    /// </summary>
    private async Task<AgentPlanStep> PrepareStepAsync(
        PlanExecution execution,
        string sessionId,
        AgentPlanItem requested,
        int order,
        CancellationToken cancellationToken)
    {
        var stepId = Guid.NewGuid().ToString("n");

        // Refuses a folder the walk really found, and records the refusal on the run.
        AgentPlanStep Skip(ScanItemView folder, SuggestedAction refusedAction, string reason, string message)
        {
            execution.Add(new PlanExecutionStep
            {
                Id = stepId,
                ExecutionId = execution.Id,
                PlanEntryId = "agent-plan",
                ScanItemId = folder.Id,
                SourcePath = folder.Path,
                Title = folder.Name,
                Action = refusedAction,
                Order = order,
                Status = ExecutionStatus.Skipped,
                ErrorCode = reason,
                ErrorMessage = message,
                CompletedAt = DateTimeOffset.Now
            });

            return Refused(stepId, folder.Id, folder.Path, reason, message);
        }

        if (!TryParseAction(requested.Action, out var action))
            return Refused(stepId, requested.ItemId, string.Empty, "bad_action", "Only 'delete' and 'move' change storage.");

        var item = await query.GetByIdAsync(sessionId, requested.ItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
            return Refused(stepId, requested.ItemId, string.Empty, "unknown_item", "That item was not part of the walk.");

        if (!item.IsFolder)
            return Skip(item, action, "not_a_folder", "Only folders can be moved or deleted.");

        if (item.IsProtected || item.RiskLevel == RiskLevel.Protected)
        {
            return Skip(item, action, "protected",
                "This is a protected system location and can never be acted on.");
        }

        // The rule catalog decides what is permitted and nothing downstream may widen it.
        if (action == SuggestedAction.Delete && !item.CanDelete)
        {
            return Skip(item, action, "not_permitted",
                "The local rules do not permit deleting this item.");
        }

        if (action == SuggestedAction.Move && !item.CanMove)
        {
            return Skip(item, action, "not_permitted",
                "The local rules do not permit moving this item.");
        }

        var source = guard.ValidateSource(item.Path);
        if (source.IsFailure)
            return Skip(item, action, source.Error.Code, source.Error.Message);

        // Measured now, not read from the scan: the number the user approves has to be the disk's.
        var measured = await fileSystem.MeasureAsync(item.Path, cancellationToken).ConfigureAwait(false);
        if (measured.IsFailure)
            return Skip(item, action, measured.Error.Code, measured.Error.Message);

        var method = AgentActionService.ResolveMoveMethod(requested.MoveMethod, action);
        if (method.IsFailure)
            return Skip(item, action, method.Error.Code, method.Error.Message);

        string? destination = action == SuggestedAction.Move
            ? (requested.DestinationPath ?? string.Empty).Trim()
            : null;

        if (action == SuggestedAction.Move)
        {
            var allowed = guard.ValidateDestination(item.Path, destination, measured.Value.Bytes);
            if (allowed.IsFailure)
                return Skip(item, action, allowed.Error.Code, allowed.Error.Message);
        }

        var step = new PlanExecutionStep
        {
            Id = stepId,
            ExecutionId = execution.Id,
            PlanEntryId = "agent-plan",
            ScanItemId = item.Id,
            SourcePath = item.Path,
            Title = item.Name,
            Action = action,
            Method = method.Value,
            Order = order,
            MeasuredBytes = measured.Value.Bytes,
            DestinationPath = destination,
            Status = ExecutionStatus.Pending
        };

        execution.Add(step);

        var warnings = new List<string>();
        if (item.Size > 0)
        {
            double ratio = (double)measured.Value.Bytes / item.Size;
            if (ratio > 1.1) warnings.Add("grew_since_scan");
            else if (ratio < 0.9) warnings.Add("shrank_since_scan");
        }

        if (item.RiskLevel == RiskLevel.High) warnings.Add("high_risk");

        if (action == SuggestedAction.Move)
        {
            warnings.Add(method.Value == MigrationMethod.Junction
                ? "junction_left_behind"
                : "old_path_will_break");
        }

        return new AgentPlanStep(
            stepId, item.Id, action.ToString().ToLowerInvariant(), item.Path, destination,
            measured.Value.Bytes, warnings);
    }

    private static AgentPlanStep Refused(
        string stepId, string itemId, string path, string reason, string message) =>
        new(stepId, itemId, string.Empty, path, null, 0, [], reason, message);

    /// <summary>
    /// Runs an approved plan, step by step, and reports what happened to each.
    /// <para>
    /// A failed step does not stop the rest. The folders are independent, and abandoning nine
    /// because the tenth was locked would leave the user to work out which of the two lists they
    /// were now looking at. Every step is attempted and every outcome is returned.
    /// </para>
    /// </summary>
    public async Task<AgentPlanOutcome?> ExecuteAsync(
        AgentPlanConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        if (!_pending.TryGetValue(confirmation.PlanId, out var pending))
            return null;

        // Both halves have to hold: the fingerprint the page echoes back must be the plan as it
        // stands, and the code the user typed must be the one this plan produces. The first stops
        // an approval being replayed against a changed plan, the second is the deliberate act.
        if (!string.Equals(confirmation.Fingerprint, pending.Fingerprint, StringComparison.Ordinal))
            return null;

        if (!PlanConfirmation.Matches(pending.Fingerprint, confirmation.TypedPhrase))
            return null;

        // Removed only once it is going to run, so a mistyped code can be tried again but an
        // approved plan cannot be executed twice.
        _pending.TryRemove(confirmation.PlanId, out _);

        var outcomes = new List<AgentPlanStepOutcome>();

        foreach (var step in pending.Execution.Steps.Where(s => s.Status == ExecutionStatus.Pending)
                     .OrderBy(s => s.Order).ToList())
        {
            // Each step still goes through the guard, with the confirmation it expects. The plan's
            // code is what the user typed; the folder's own name is supplied here because the
            // approval already covered this exact step — its fingerprint is part of the plan's.
            var result = await executor.ExecuteStepAsync(
                pending.Execution,
                step,
                new StepConfirmation
                {
                    StepId = step.Id,
                    Fingerprint = StepConfirmation.Compute(step),
                    TypedName = ExecutionGuard.ApprovalWord
                },
                progress: null,
                cancellationToken).ConfigureAwait(false);

            outcomes.Add(new AgentPlanStepOutcome(
                step.Id,
                step.ScanItemId,
                step.SourcePath,
                result.IsSuccess,
                step.Status.ToString(),
                step.BytesFreed,
                step.RecycledPath,
                step.LinkPath,
                result.IsFailure ? result.Error.Code : step.ErrorCode,
                result.IsFailure ? result.Error.Message : step.ErrorMessage));
        }

        // The steps refused at preparation are reported too, so the totals account for every
        // folder the user selected rather than only the ones that were tried.
        int skipped = pending.Execution.Steps.Count(s => s.Status == ExecutionStatus.Skipped);

        var outcome = new AgentPlanOutcome(
            pending.Execution.Id,
            outcomes,
            outcomes.Count(o => o.Succeeded),
            outcomes.Count(o => !o.Succeeded),
            skipped,
            outcomes.Sum(o => o.BytesFreed));

        logger.LogInformation(
            "An agent plan finished: {Succeeded} done, {Failed} failed, {Bytes} bytes freed.",
            outcome.SucceededCount, outcome.FailedCount, outcome.TotalBytesFreed);

        return outcome;
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

    private sealed record PendingPlan(PlanExecution Execution, string Fingerprint);
}
