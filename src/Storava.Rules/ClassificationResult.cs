using Storava.Domain.Enums;

namespace Storava.Rules;

/// <summary>The outcome of classifying one item: what it is and what may be done with it.</summary>
public sealed record ClassificationResult(
    StorageCategory Category,
    string? Technology,
    string? RuleId,
    RiskLevel RiskLevel,
    double Confidence,
    bool CanDelete,
    bool CanMove,
    bool CanRegenerate,
    MigrationMethod OfficialMigrationMethod,
    MigrationMethod FallbackMigrationMethod)
{
    /// <summary>An item nothing in the catalog recognises, and which is safe to leave alone.</summary>
    public static ClassificationResult Unknown { get; } = new(
        StorageCategory.Unknown, null, null, RiskLevel.Unknown, 0,
        CanDelete: false, CanMove: false, CanRegenerate: false,
        MigrationMethod.None, MigrationMethod.None);

    /// <summary>A system-critical location: identified, but never actionable.</summary>
    public static ClassificationResult Protected { get; } = new(
        StorageCategory.WindowsSystem, "Windows", "system.protected", RiskLevel.Protected, 1,
        CanDelete: false, CanMove: false, CanRegenerate: false,
        MigrationMethod.None, MigrationMethod.None);
}
