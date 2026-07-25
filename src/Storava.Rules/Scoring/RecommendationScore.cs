namespace Storava.Rules.Scoring;

/// <summary>
/// The component breakdown behind a recommendation's ranking. Kept transparent so the UI can
/// explain *why* something is ranked highly. The score only ever affects ordering and wording —
/// it never authorises an operation.
/// </summary>
public sealed record RecommendationScore(
    double SizeScore,
    double RegeneratableScore,
    double KnownCacheScore,
    double InactivityScore,
    double DuplicateScore,
    double MoveBenefitScore,
    double SystemRiskPenalty,
    double ActiveUsagePenalty,
    double MigrationRiskPenalty)
{
    public double Total =>
        SizeScore
        + RegeneratableScore
        + KnownCacheScore
        + InactivityScore
        + DuplicateScore
        + MoveBenefitScore
        - SystemRiskPenalty
        - ActiveUsagePenalty
        - MigrationRiskPenalty;

    public static RecommendationScore Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}
