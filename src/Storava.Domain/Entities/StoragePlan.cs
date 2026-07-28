using Storava.Domain.Common;
using Storava.Domain.Enums;

namespace Storava.Domain.Entities;

/// <summary>
/// A saved list of steps the user intends to carry out for one scan.
/// <para>
/// The plan is a *document*, not a command. Storava has no code path that reads a plan and
/// changes the file system; producing one is the whole of this phase. The invariants enforced
/// here are what stop a plan from ever describing something unsafe: a step must be an action the
/// local rules already permit for that item, the same item cannot appear twice, and a step
/// nested inside another never counts its space twice.
/// </para>
/// </summary>
public sealed class StoragePlan
{
    private readonly List<StoragePlanEntry> _entries = [];

    public required string Id { get; init; }
    public required string SessionId { get; init; }

    public string? Name { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>How much the user wants to free, in bytes. Zero means no goal was set.</summary>
    public long GoalBytes { get; set; }

    public IReadOnlyList<StoragePlanEntry> Entries => _entries;

    /// <summary>Space the plan would free, with nested steps counted once.</summary>
    public long TotalReclaimable => _entries.Sum(e => e.EffectiveSpace);

    public int MoveCount => _entries.Count(e => e.Action == SuggestedAction.Move);

    public int DeleteCount => _entries.Count(e => e.Action == SuggestedAction.Delete);

    /// <summary>The highest risk anywhere in the plan — what the user is really signing up for.</summary>
    public RiskLevel HighestRisk => _entries.Count == 0
        ? RiskLevel.Unknown
        : _entries.MaxBy(e => SafetyRank(e.RiskLevel))!.RiskLevel;

    /// <summary>Progress towards <see cref="GoalBytes"/>, clamped to 0..1. Zero when no goal is set.</summary>
    public double GoalProgress => GoalBytes <= 0
        ? 0
        : Math.Clamp((double)TotalReclaimable / GoalBytes, 0, 1);

    public bool MeetsGoal => GoalBytes > 0 && TotalReclaimable >= GoalBytes;

    public StoragePlanEntry? FindByScanItem(string scanItemId) =>
        _entries.FirstOrDefault(e => string.Equals(e.ScanItemId, scanItemId, StringComparison.Ordinal));

    /// <summary>
    /// Adds a step for a piece of advice from the rule catalog. A convenience over the candidate
    /// overload, which holds all the rules — this only says how a recommendation becomes one.
    /// </summary>
    public Result<StoragePlanEntry> TryAdd(
        Recommendation recommendation,
        SuggestedAction action,
        string entryId) =>
        TryAdd(PlanCandidate.FromRecommendation(recommendation), action, entryId);

    /// <summary>
    /// Adds a step for <paramref name="candidate"/>, or explains why it cannot be added.
    /// The caller is responsible for the protected-path check, which needs platform knowledge the
    /// domain does not have — see the plan service.
    /// </summary>
    public Result<StoragePlanEntry> TryAdd(
        PlanCandidate candidate,
        SuggestedAction action,
        string entryId)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        if (!string.Equals(candidate.SessionId, SessionId, StringComparison.Ordinal))
            return Result.Failure<StoragePlanEntry>(PlanErrors.WrongSession);

        // Only these two do anything to storage. Review/Ignore/NoAction are notes, not steps.
        if (action is not (SuggestedAction.Move or SuggestedAction.Delete))
            return Result.Failure<StoragePlanEntry>(PlanErrors.NotAPlannableAction);

        // A recognised item's rule is final; an unrecognised one has no rule to overrule, so the
        // user's own choice stands. See PlanCandidate.Permits for why the two cannot be one check.
        if (!candidate.Permits(action))
        {
            return Result.Failure<StoragePlanEntry>(action == SuggestedAction.Delete
                ? PlanErrors.DeleteNotPermitted
                : PlanErrors.MoveNotPermitted);
        }

        if (candidate.RiskLevel == RiskLevel.Protected)
            return Result.Failure<StoragePlanEntry>(PlanErrors.ProtectedPath);

        // A link is a pointer, not storage. Deleting one frees nothing and moving one would copy
        // whatever it points at — someone else's data, from a place the user never chose.
        if (candidate.IsReparsePoint)
            return Result.Failure<StoragePlanEntry>(PlanErrors.IsLink);

        if (FindByScanItem(candidate.ScanItemId) is not null)
            return Result.Failure<StoragePlanEntry>(PlanErrors.AlreadyInPlan);

        var method = candidate.OfficialMigrationMethod != MigrationMethod.None
            ? candidate.OfficialMigrationMethod
            : candidate.FallbackMigrationMethod;

        // Nothing in the catalog said how to relocate this, because nothing in the catalog knows
        // it. A junction is the mechanism that needs no privilege, so it is what a user-chosen
        // move falls back to.
        if (action == SuggestedAction.Move && method == MigrationMethod.None)
            method = MigrationMethod.Junction;

        var entry = new StoragePlanEntry
        {
            Id = entryId,
            PlanId = Id,
            RecommendationId = candidate.RecommendationId,
            ScanItemId = candidate.ScanItemId,
            Path = candidate.Path,
            Title = candidate.Title,
            Action = action,
            EstimatedSpace = candidate.EstimatedSpace,
            RiskLevel = candidate.RiskLevel,
            Category = candidate.Category,
            Technology = candidate.Technology,
            IsFolder = candidate.IsFolder,
            HasNoRule = !candidate.IsIdentified,
            Method = action == SuggestedAction.Move ? method : MigrationMethod.None,
            MethodHint = action == SuggestedAction.Move ? candidate.MethodHint : null,
            Warning = candidate.Warning
        };

        _entries.Add(entry);
        Recalculate();
        return Result.Success(entry);
    }

    public bool RemoveByScanItem(string scanItemId)
    {
        int removed = _entries.RemoveAll(e => string.Equals(e.ScanItemId, scanItemId, StringComparison.Ordinal));
        if (removed == 0)
            return false;

        Recalculate();
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        UpdatedAt = DateTimeOffset.Now;
    }

    /// <summary>Restores a plan read back from storage without re-running the add-time checks.</summary>
    public void Load(IEnumerable<StoragePlanEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries.Clear();
        _entries.AddRange(entries);
        Recalculate();
    }

    /// <summary>
    /// Marks nested steps as covered and numbers the steps safest-first: lowest risk, then an
    /// official relocation mechanism ahead of a junction or symlink, then the biggest win.
    /// </summary>
    public void Recalculate()
    {
        MarkCoveredEntries();

        var ordered = _entries
            .OrderBy(e => SafetyRank(e.RiskLevel))
            .ThenBy(e => e.Action == SuggestedAction.Move ? 0 : 1)
            .ThenBy(e => e.Method == MigrationMethod.OfficialSetting ? 0 : 1)
            .ThenByDescending(e => e.EffectiveSpace)
            .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Order = i + 1;

        UpdatedAt = DateTimeOffset.Now;
    }

    /// <summary>
    /// A step whose path sits under another step's path frees nothing extra — the ancestor already
    /// covers it. Counting both would overstate the plan, which is the one number the user acts on.
    /// </summary>
    private void MarkCoveredEntries()
    {
        foreach (var entry in _entries)
            entry.CoveredByEntryId = null;

        // Shortest paths first, so an ancestor is always resolved before anything beneath it —
        // that is what makes the `candidate.IsCovered` skip below correct for a nested chain.
        var byDepth = _entries.OrderBy(e => e.Path.Length).ToList();

        foreach (var entry in byDepth)
        {
            foreach (var candidate in byDepth)
            {
                if (ReferenceEquals(candidate, entry) || candidate.IsCovered)
                    continue;

                if (IsUnder(entry.Path, candidate.Path))
                {
                    entry.CoveredByEntryId = candidate.Id;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Ordering key for "safest first". The enum's own order cannot be used: it puts
    /// <see cref="RiskLevel.Unknown"/> at zero, which would present an unclassified item as the
    /// safest thing in the plan. Unknown belongs between Medium and High.
    /// </summary>
    private static int SafetyRank(RiskLevel risk) => risk switch
    {
        RiskLevel.Low => 0,
        RiskLevel.Medium => 1,
        RiskLevel.Unknown => 2,
        RiskLevel.High => 3,
        _ => 4
    };

    private static bool IsUnder(string path, string ancestor)
    {
        if (path.Length <= ancestor.Length)
            return false;

        if (!path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase))
            return false;

        // "C:\data" must not be treated as an ancestor of "C:\database".
        char boundary = path[ancestor.Length];
        return boundary is '\\' or '/' || ancestor.EndsWith('\\') || ancestor.EndsWith('/');
    }
}

/// <summary>Why a step could not be added. Each one is shown to the user verbatim.</summary>
public static class PlanErrors
{
    public static readonly Error WrongSession =
        new("plan.wrong_session", "That advice belongs to a different scan.");

    public static readonly Error NotAPlannableAction =
        new("plan.not_plannable", "Only Move and Delete can be planned.");

    public static readonly Error DeleteNotPermitted =
        new("plan.delete_not_permitted", "The local rules do not allow deleting this item.");

    public static readonly Error MoveNotPermitted =
        new("plan.move_not_permitted", "The local rules do not allow moving this item.");

    public static readonly Error ProtectedPath =
        new("plan.protected_path", "This is a protected system location and can never be planned.");

    public static readonly Error AlreadyInPlan =
        new("plan.already_added", "This item is already in the plan.");

    public static readonly Error IsLink =
        new("plan.is_link", "This is a link to somewhere else, not storage of its own.");
}
