using Storava.Domain.Entities;

namespace Storava.Application.Abstractions;

/// <summary>
/// Stores the plan a user has drafted for a scan. One plan per scan session: the plan describes
/// what to do about *that* scan's findings, so a new scan starts a new plan.
/// </summary>
public interface IStoragePlanRepository
{
    /// <summary>Returns the stored plan for a session, or null when none has been saved.</summary>
    Task<StoragePlan?> GetForSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Writes the plan and its steps, replacing whatever was stored for that session.</summary>
    Task SaveAsync(StoragePlan plan, CancellationToken cancellationToken = default);

    Task DeleteForSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
