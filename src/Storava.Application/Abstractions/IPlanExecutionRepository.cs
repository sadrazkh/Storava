using Storava.Domain.Entities;

namespace Storava.Application.Abstractions;

/// <summary>
/// Stores what was actually done to the disk. Unlike the plan repository this is append-only in
/// spirit: a run is written as it happens so an interrupted session can be recovered, and old runs
/// stay around as the user's record of every change Storava made.
/// </summary>
public interface IPlanExecutionRepository
{
    /// <summary>Writes the run and all of its steps, replacing the stored copy of that run.</summary>
    Task SaveAsync(PlanExecution execution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a single step. Called before and after every disk operation, so the row on disk is
    /// never behind what has already happened.
    /// </summary>
    Task SaveStepAsync(PlanExecutionStep step, CancellationToken cancellationToken = default);

    Task<PlanExecution?> GetAsync(string executionId, CancellationToken cancellationToken = default);

    /// <summary>The most recent run for a scan session, or null when the plan has never been run.</summary>
    Task<PlanExecution?> GetLatestForSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Every run, newest first. This is the audit trail shown to the user.</summary>
    Task<IReadOnlyList<PlanExecution>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}
