using Storava.Domain.Enums;

namespace Storava.Domain.Entities;

/// <summary>
/// One step the user has chosen to put in a plan. It is a snapshot of a <see cref="Recommendation"/>
/// at the moment it was picked, so a later re-scan cannot silently change what the plan says.
/// <para>
/// An entry is still only a written intention. Nothing in Storava reads a plan and acts on it;
/// carrying a step out is a separate, explicitly confirmed step in a later phase.
/// </para>
/// </summary>
public sealed class StoragePlanEntry
{
    public required string Id { get; init; }
    public required string PlanId { get; init; }

    /// <summary>
    /// The advice this step came from, or null when the user picked the item out of the scan
    /// themselves and there is no advice to point back to.
    /// </summary>
    public string? RecommendationId { get; init; }

    /// <summary>The scan item the step refers to. Never a free-form path.</summary>
    public required string ScanItemId { get; init; }

    public required string Path { get; init; }
    public required string Title { get; init; }

    /// <summary>
    /// What the user chose to do. Only <see cref="SuggestedAction.Move"/> and
    /// <see cref="SuggestedAction.Delete"/> can ever reach a plan — see <see cref="StoragePlan.TryAdd"/>.
    /// </summary>
    public required SuggestedAction Action { get; init; }

    /// <summary>Space this step would free on its own, in bytes.</summary>
    public long EstimatedSpace { get; init; }

    /// <summary>
    /// False for a file. It does not change whether the step is allowed, only how it is carried
    /// out — a file is copied and linked differently from a tree.
    /// </summary>
    public bool IsFolder { get; init; } = true;

    /// <summary>
    /// True when no rule in the catalog recognised this item and the user chose the action on
    /// their own. Nothing behaves differently because of it; it is here so the confirmation can
    /// say plainly that Storava has no knowledge of this item beyond its size.
    /// </summary>
    public bool HasNoRule { get; init; }

    public RiskLevel RiskLevel { get; init; } = RiskLevel.Unknown;
    public StorageCategory Category { get; init; } = StorageCategory.Unknown;
    public string? Technology { get; init; }

    /// <summary>The best relocation mechanism known for this item, for a Move step.</summary>
    public MigrationMethod Method { get; init; } = MigrationMethod.None;

    public string? MethodHint { get; init; }
    public string? Warning { get; init; }

    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Position in the ordered plan, assigned by <see cref="StoragePlan.Recalculate"/>.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Set when another entry already covers this path (an ancestor folder is also in the plan).
    /// A covered step frees nothing extra, so it is excluded from the plan total rather than
    /// counted twice.
    /// </summary>
    public string? CoveredByEntryId { get; set; }

    public bool IsCovered => CoveredByEntryId is not null;

    /// <summary>What this step actually contributes to the plan total.</summary>
    public long EffectiveSpace => IsCovered ? 0 : EstimatedSpace;
}
