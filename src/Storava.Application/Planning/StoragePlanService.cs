using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Domain.Common;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Application.Planning;

/// <summary>
/// Builds and stores the storage plan for a scan.
/// <para>
/// This service deliberately has no ability to move or delete anything — it depends only on the
/// repositories and the protected-path check. Producing the plan and carrying it out are separate
/// concerns, and only the first one exists.
/// </para>
/// </summary>
public sealed class StoragePlanService
{
    private readonly IStoragePlanRepository _plans;
    private readonly IRecommendationRepository _recommendations;
    private readonly IProtectedPathService _protectedPaths;
    private readonly ILogger<StoragePlanService> _logger;

    public StoragePlanService(
        IStoragePlanRepository plans,
        IRecommendationRepository recommendations,
        IProtectedPathService protectedPaths,
        ILogger<StoragePlanService> logger)
    {
        _plans = plans;
        _recommendations = recommendations;
        _protectedPaths = protectedPaths;
        _logger = logger;
    }

    /// <summary>Loads the saved plan for a session, or starts an empty draft.</summary>
    public async Task<StoragePlan> LoadOrCreateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var stored = await _plans.GetForSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            // A stored step could have become unsafe since it was written — for example the item
            // now resolves under a protected location. Drop those rather than showing them.
            var unsafeEntries = stored.Entries
                .Where(e => _protectedPaths.IsProtected(e.Path))
                .ToList();

            foreach (var entry in unsafeEntries)
            {
                stored.RemoveByScanItem(entry.ScanItemId);
                _logger.LogWarning("Dropped a saved plan step because its path is now protected.");
            }

            return stored;
        }

        return new StoragePlan
        {
            Id = Guid.NewGuid().ToString("n"),
            SessionId = sessionId
        };
    }

    /// <summary>The advice this plan can be built from, best first.</summary>
    public Task<IReadOnlyList<Recommendation>> GetCandidatesAsync(
        string sessionId,
        CancellationToken cancellationToken = default) =>
        _recommendations.GetBySessionAsync(sessionId, cancellationToken);

    /// <summary>
    /// Adds one step. The protected-path check lives here rather than in the entity because it
    /// needs platform knowledge; every other invariant is enforced by <see cref="StoragePlan.TryAdd"/>.
    /// </summary>
    public Result<StoragePlanEntry> Include(
        StoragePlan plan,
        Recommendation recommendation,
        SuggestedAction action)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(recommendation);

        if (_protectedPaths.IsProtected(recommendation.Path))
        {
            _logger.LogWarning("Refused to plan a step for a protected location.");
            return Result.Failure<StoragePlanEntry>(PlanErrors.ProtectedPath);
        }

        return plan.TryAdd(recommendation, action, Guid.NewGuid().ToString("n"));
    }

    public bool Exclude(StoragePlan plan, string scanItemId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.RemoveByScanItem(scanItemId);
    }

    public Task SaveAsync(StoragePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        plan.Recalculate();
        _logger.LogInformation(
            "Saving storage plan: {Steps} step(s), {Moves} move(s), {Deletes} delete(s).",
            plan.Entries.Count, plan.MoveCount, plan.DeleteCount);

        return _plans.SaveAsync(plan, cancellationToken);
    }

    public Task DiscardAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _plans.DeleteForSessionAsync(sessionId, cancellationToken);
}
