using Storava.Domain.Entities;

namespace Storava.AI.Validation;

/// <summary>
/// The outcome of checking an AI response against the local scan: what survived, and exactly
/// why anything else was thrown away.
/// </summary>
public sealed record AiValidationResult(
    IReadOnlyList<Recommendation> Accepted,
    IReadOnlyList<RejectedRecommendation> Rejected,
    string? Summary,
    string? MainCause,
    string? Overview,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> NextSteps)
{
    public bool HasContent =>
        Accepted.Count > 0 || !string.IsNullOrWhiteSpace(Summary) || !string.IsNullOrWhiteSpace(Overview);
}

/// <summary>A discarded suggestion, kept so the reason can be surfaced and logged.</summary>
public sealed record RejectedRecommendation(string? ScanItemId, string? Title, RejectionReason Reason);

public enum RejectionReason
{
    /// <summary>No scan item id, or one that was never in the payload.</summary>
    UnknownScanItem = 0,

    /// <summary>The referenced item is a protected system location.</summary>
    ProtectedPath = 1,

    /// <summary>The action was not one Storava recognises.</summary>
    UnknownAction = 2,

    /// <summary>The action contradicts what the local rules allow for that item.</summary>
    ActionNotPermitted = 3,

    /// <summary>Required text was missing or the numbers were nonsensical.</summary>
    InconsistentData = 4,

    /// <summary>The same item was recommended more than once.</summary>
    Duplicate = 5
}
