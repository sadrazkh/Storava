using Storava.Domain.Enums;

namespace Storava.Domain.Entities;

/// <summary>
/// Something the user wants a plan step for, reduced to the facts a step actually needs.
/// <para>
/// It exists so that advice from the rule catalog and a folder the user picked out of the scan
/// themselves arrive at <see cref="StoragePlan.TryAdd"/> by the same road. The alternative — a
/// second overload for the second source — would be two copies of every invariant, and the copy
/// that is not the one being read is the copy that rots.
/// </para>
/// </summary>
public sealed record PlanCandidate
{
    public required string SessionId { get; init; }

    /// <summary>The scan item this refers to. Never a free-form path typed by anyone.</summary>
    public required string ScanItemId { get; init; }

    /// <summary>
    /// The advice behind this, when there is any. Null for something the user picked out of the
    /// scan on their own — that has no recommendation to point back to.
    /// </summary>
    public string? RecommendationId { get; init; }

    public required string Path { get; init; }
    public required string Title { get; init; }

    public long EstimatedSpace { get; init; }
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Unknown;
    public StorageCategory Category { get; init; } = StorageCategory.Unknown;
    public string? Technology { get; init; }

    /// <summary>False for a file, which changes how a move is carried out but not whether it may be.</summary>
    public bool IsFolder { get; init; } = true;

    /// <summary>
    /// Whether the rule catalog recognised this item.
    /// <para>
    /// This is the whole reason <see cref="CanDelete"/> and <see cref="CanMove"/> cannot be read on
    /// their own. Both are false for an item no rule matched — not because deleting it is forbidden
    /// but because nothing was asked. Treating that silence as a refusal is what made the plan
    /// reachable only for the three dozen things the catalog happens to know.
    /// </para>
    /// </summary>
    public bool IsIdentified { get; init; }

    /// <summary>What the catalog permits. Meaningful only when <see cref="IsIdentified"/>.</summary>
    public bool CanDelete { get; init; }

    public bool CanMove { get; init; }

    /// <summary>A folder that is really a pointer somewhere else. Never actionable, either way.</summary>
    public bool IsReparsePoint { get; init; }

    public MigrationMethod OfficialMigrationMethod { get; init; } = MigrationMethod.None;
    public MigrationMethod FallbackMigrationMethod { get; init; } = MigrationMethod.None;

    public string? MethodHint { get; init; }
    public string? Warning { get; init; }

    /// <summary>
    /// Whether the catalog has an opinion about <paramref name="action"/> that must be honoured.
    /// <para>
    /// For a recognised item its answer is knowledge and is final — a rule saying a folder must not
    /// be deleted knows something the user does not. For an unrecognised one there is no opinion to
    /// honour, and the user's own choice stands.
    /// </para>
    /// </summary>
    public bool Permits(SuggestedAction action) => !IsIdentified || action switch
    {
        SuggestedAction.Delete => CanDelete,
        SuggestedAction.Move => CanMove,
        _ => false
    };

    /// <summary>Projects advice from the rule catalog, which by definition is identified.</summary>
    public static PlanCandidate FromRecommendation(Recommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        return new PlanCandidate
        {
            SessionId = recommendation.SessionId,
            ScanItemId = recommendation.ScanItemId,
            RecommendationId = recommendation.Id,
            Path = recommendation.Path,
            Title = recommendation.Title,
            EstimatedSpace = recommendation.EstimatedSpace,
            RiskLevel = recommendation.RiskLevel,
            Category = recommendation.Category,
            Technology = recommendation.Technology,
            IsIdentified = true,
            CanDelete = recommendation.CanDelete,
            CanMove = recommendation.CanMove,
            OfficialMigrationMethod = recommendation.OfficialMigrationMethod,
            FallbackMigrationMethod = recommendation.FallbackMigrationMethod,
            MethodHint = recommendation.OfficialMigrationHint,
            Warning = recommendation.Warning
        };
    }
}
