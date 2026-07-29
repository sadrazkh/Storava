using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
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
    /// Adds one step for a piece of advice from the rule catalog.
    /// <para>
    /// The protected-path check lives here rather than in the entity because it needs platform
    /// knowledge; every other invariant is enforced by <see cref="StoragePlan.TryAdd"/>.
    /// </para>
    /// </summary>
    public Result<StoragePlanEntry> Include(
        StoragePlan plan,
        Recommendation recommendation,
        SuggestedAction action,
        MigrationMethod? method = null)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        var candidate = PlanCandidate.FromRecommendation(recommendation);
        return Include(plan, method is null ? candidate : candidate with { RequestedMethod = method }, action);
    }

    /// <summary>
    /// Adds one step for an item the user picked out of the scan themselves.
    /// <para>
    /// This is the path for everything the rule catalog does not recognise, which is most of a real
    /// disk. What the catalog gives up in return is its judgement: there is no measured risk, no
    /// known way to relocate the thing, and no assurance that anything still works afterwards. So
    /// the guards that remain are the ones that do not depend on recognising the item — it must not
    /// be protected, must not be a link, and must actually be there.
    /// </para>
    /// </summary>
    /// <param name="sessionId">
    /// The scan <paramref name="item"/> was read from. Passed in rather than taken from the plan
    /// because a scanned item does not carry one, and taking it from the plan would turn
    /// <see cref="StoragePlan.TryAdd"/>'s wrong-session check into something that always agrees
    /// with itself.
    /// </param>
    /// <param name="method">
    /// How a move should be carried out. Null leaves it to the catalog; a value is the user having
    /// chosen whether a junction is left at the old path.
    /// </param>
    public Result<StoragePlanEntry> Include(
        StoragePlan plan,
        ScanItemView item,
        string sessionId,
        SuggestedAction action,
        MigrationMethod? method = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (item.IsProtected)
        {
            _logger.LogWarning("Refused to plan a step for an item the scan marked protected.");
            return Result.Failure<StoragePlanEntry>(PlanErrors.ProtectedPath);
        }

        return Include(plan, ToCandidate(item, sessionId) with { RequestedMethod = method }, action);
    }

    private Result<StoragePlanEntry> Include(
        StoragePlan plan,
        PlanCandidate candidate,
        SuggestedAction action)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (_protectedPaths.IsProtected(candidate.Path))
        {
            _logger.LogWarning("Refused to plan a step for a protected location.");
            return Result.Failure<StoragePlanEntry>(PlanErrors.ProtectedPath);
        }

        return plan.TryAdd(candidate, action, Guid.NewGuid().ToString("n"));
    }

    /// <summary>
    /// Reads a scanned item as a plan candidate.
    /// <para>
    /// The capability flags are carried across as the rules set them, and are read only when the
    /// item was recognised — see <see cref="PlanCandidate.Permits"/>. An unrecognised item has both
    /// set to false simply because no rule was consulted, and reading that as a refusal is the bug
    /// this whole path exists to undo.
    /// </para>
    /// </summary>
    private static PlanCandidate ToCandidate(ScanItemView item, string sessionId) => new()
    {
        SessionId = sessionId,
        ScanItemId = item.Id,
        RecommendationId = null,
        Path = item.Path,
        Title = item.Name,
        EstimatedSpace = item.Size,
        RiskLevel = item.RiskLevel,
        Category = item.Category,
        Technology = item.DetectedTechnology,
        IsFolder = item.IsFolder,
        IsIdentified = item.IsIdentified,
        CanDelete = item.CanDelete,
        CanMove = item.CanMove,
        IsReparsePoint = item.IsReparsePoint
    };

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
