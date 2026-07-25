using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Rules.Scoring;

/// <summary>
/// Computes the ranking score for a candidate. Weights are deliberately simple and readable;
/// the goal is a sensible ordering, not a precise model.
/// </summary>
public sealed class RecommendationScoreCalculator
{
    private const long OneGigabyte = 1024L * 1024 * 1024;

    public RecommendationScore Calculate(ScanItem item, ClassificationResult classification, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Calculate(ScoringInput.From(item), classification, now);
    }

    /// <param name="now">Reference time for inactivity, injectable so results are testable.</param>
    public RecommendationScore Calculate(ScoringInput input, ClassificationResult classification, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(classification);

        // Protected locations are never candidates; return a strongly negative score so they
        // can never surface at the top of any ranking.
        if (classification.RiskLevel == RiskLevel.Protected)
            return RecommendationScore.Zero with { SystemRiskPenalty = 100 };

        double sizeGb = (double)input.Size / OneGigabyte;

        // Logarithmic so each extra gigabyte is worth less than the last, and one enormous
        // item cannot dominate the whole ranking: 0.5 GB ≈ 19, 1 GB ≈ 26, 10 GB ≈ 50, capped at 70.
        double sizeScore = Math.Min(70, 25 * Math.Log10(1 + 10 * sizeGb));

        double regeneratable = classification.CanRegenerate ? 15 : 0;
        double knownCache = classification.RuleId is not null
            ? classification.Category switch
            {
                StorageCategory.PackageCaches or StorageCategory.BuildArtifacts
                    or StorageCategory.IdeCaches or StorageCategory.BrowserCaches
                    or StorageCategory.TemporaryFiles => 15,
                _ => 8
            }
            : 0;

        double inactivity = InactivityScore(input, now);
        double moveBenefit = classification.CanMove
            ? (classification.OfficialMigrationMethod == MigrationMethod.OfficialSetting ? 12 : 6)
            : 0;

        double systemRisk = classification.RiskLevel switch
        {
            RiskLevel.High => 25,
            RiskLevel.Medium => 8,
            _ => 0
        };
        if (input.IsSystem)
            systemRisk += 15;

        double activeUsage = ActiveUsagePenalty(input, now);

        double migrationRisk = classification.CanMove && classification.OfficialMigrationMethod == MigrationMethod.None
            ? 6   // junction-only relocation is riskier than an officially supported move
            : 0;

        return new RecommendationScore(
            SizeScore: sizeScore,
            RegeneratableScore: regeneratable,
            KnownCacheScore: knownCache,
            InactivityScore: inactivity,
            DuplicateScore: 0, // populated once duplicate detection lands
            MoveBenefitScore: moveBenefit,
            SystemRiskPenalty: systemRisk,
            ActiveUsagePenalty: activeUsage,
            MigrationRiskPenalty: migrationRisk);
    }

    private static double InactivityScore(ScoringInput input, DateTimeOffset now)
    {
        var lastTouched = input.LastWriteTime ?? input.CreationTime;
        if (lastTouched is null)
            return 0;

        double days = (now - lastTouched.Value).TotalDays;
        return days switch
        {
            >= 365 => 20,
            >= 180 => 14,
            >= 90 => 9,
            >= 30 => 4,
            _ => 0
        };
    }

    private static double ActiveUsagePenalty(ScoringInput input, DateTimeOffset now)
    {
        var lastTouched = input.LastWriteTime;
        if (lastTouched is null)
            return 0;

        double days = (now - lastTouched.Value).TotalDays;
        return days switch
        {
            < 1 => 12,
            < 7 => 6,
            _ => 0
        };
    }
}
