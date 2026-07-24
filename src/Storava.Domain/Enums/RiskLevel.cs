namespace Storava.Domain.Enums;

/// <summary>
/// Risk of acting (move/delete) on an item. Ordered so higher values mean higher risk.
/// </summary>
public enum RiskLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    /// <summary>System-critical location that must never be modified, even on AI advice.</summary>
    Protected = 4
}
