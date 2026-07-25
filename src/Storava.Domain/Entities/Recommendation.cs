using Storava.Domain.Enums;

namespace Storava.Domain.Entities;

/// <summary>
/// A suggestion about one scanned item. A recommendation is advice only: it always starts at
/// <see cref="SuggestedAction.NoAction"/> and carries no authority to change anything. Every
/// recommendation is bound to a real <see cref="ScanItemId"/> from the local scan.
/// </summary>
public sealed class Recommendation
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }

    /// <summary>The scan item this advice refers to. Never a free-form path.</summary>
    public required string ScanItemId { get; init; }

    /// <summary>Copied from the scan for display; the source of truth stays the scan item.</summary>
    public required string Path { get; init; }

    public required string Title { get; init; }
    public required string Reason { get; init; }

    public SuggestedAction SuggestedAction { get; init; } = SuggestedAction.NoAction;
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Unknown;
    public StorageCategory Category { get; init; } = StorageCategory.Unknown;
    public string? Technology { get; init; }
    public string? RuleId { get; init; }

    /// <summary>Space that could be reclaimed, in bytes.</summary>
    public long EstimatedSpace { get; init; }

    public double Confidence { get; init; }

    /// <summary>Ranking score. Ordering and explanation only — never an authorisation.</summary>
    public double Score { get; init; }

    public bool CanDelete { get; init; }
    public bool CanMove { get; init; }
    public bool CanRegenerate { get; init; }

    public MigrationMethod OfficialMigrationMethod { get; init; } = MigrationMethod.None;
    public MigrationMethod FallbackMigrationMethod { get; init; } = MigrationMethod.None;
    public string? OfficialMigrationHint { get; init; }

    public string? Warning { get; init; }

    /// <summary>Where this came from: the local rule engine, or (in a later phase) the AI.</summary>
    public RecommendationSource Source { get; init; } = RecommendationSource.RuleEngine;
}

public enum RecommendationSource
{
    RuleEngine = 0,
    Ai = 1
}
