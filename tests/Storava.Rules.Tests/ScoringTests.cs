using Storava.Domain.Enums;
using Storava.Rules.Scoring;

namespace Storava.Rules.Tests;

public class ScoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    private static RecommendationScore ScoreOf(
        long sizeBytes,
        DateTimeOffset? lastWrite = null,
        bool isSystem = false,
        RiskLevel risk = RiskLevel.Low,
        bool canRegenerate = true,
        bool canMove = true,
        MigrationMethod official = MigrationMethod.OfficialSetting,
        StorageCategory category = StorageCategory.PackageCaches)
    {
        var classification = new ClassificationResult(
            category, "Tech", "some.rule", risk, 0.9,
            CanDelete: true, CanMove: canMove, CanRegenerate: canRegenerate,
            official, MigrationMethod.Junction);

        var input = new ScoringInput(sizeBytes, null, lastWrite ?? Now.AddDays(-200), isSystem);
        return new RecommendationScoreCalculator().Calculate(input, classification, Now);
    }

    private static long Gb(double value) => (long)(value * 1024 * 1024 * 1024);

    [Fact]
    public void Score_GrowsWithSize()
    {
        double small = ScoreOf(Gb(1)).Total;
        double medium = ScoreOf(Gb(10)).Total;
        double large = ScoreOf(Gb(100)).Total;

        Assert.True(small < medium, $"{small} should be less than {medium}");
        Assert.True(medium < large, $"{medium} should be less than {large}");
    }

    [Fact]
    public void Score_SizeHasDiminishingReturnsPerGigabyte()
    {
        // One more gigabyte matters far less to an already-huge item than to a small one,
        // so a single enormous folder cannot dominate the entire ranking.
        double marginalAtOneGb = ScoreOf(Gb(2)).SizeScore - ScoreOf(Gb(1)).SizeScore;
        double marginalAtTenGb = ScoreOf(Gb(11)).SizeScore - ScoreOf(Gb(10)).SizeScore;

        Assert.True(marginalAtOneGb > marginalAtTenGb,
            $"{marginalAtOneGb} should exceed {marginalAtTenGb}");
    }

    [Fact]
    public void Score_SizeIsCapped()
    {
        Assert.Equal(70, ScoreOf(Gb(500)).SizeScore);
        Assert.Equal(70, ScoreOf(Gb(5000)).SizeScore);
    }

    [Fact]
    public void Score_ProtectedItem_IsHeavilyPenalised()
    {
        var score = ScoreOf(Gb(50), risk: RiskLevel.Protected);

        Assert.Equal(100, score.SystemRiskPenalty);
        Assert.True(score.Total < 0);
    }

    [Fact]
    public void Score_RecentlyUsedItem_RanksBelowStaleItem()
    {
        double stale = ScoreOf(Gb(5), lastWrite: Now.AddDays(-400)).Total;
        double active = ScoreOf(Gb(5), lastWrite: Now.AddHours(-2)).Total;

        Assert.True(active < stale);
    }

    [Fact]
    public void Score_InactivityTiersIncrease()
    {
        Assert.Equal(0, ScoreOf(Gb(1), lastWrite: Now.AddDays(-10)).InactivityScore);
        Assert.Equal(4, ScoreOf(Gb(1), lastWrite: Now.AddDays(-40)).InactivityScore);
        Assert.Equal(9, ScoreOf(Gb(1), lastWrite: Now.AddDays(-100)).InactivityScore);
        Assert.Equal(14, ScoreOf(Gb(1), lastWrite: Now.AddDays(-200)).InactivityScore);
        Assert.Equal(20, ScoreOf(Gb(1), lastWrite: Now.AddDays(-400)).InactivityScore);
    }

    [Fact]
    public void Score_SystemFlag_AddsRiskPenalty()
    {
        double normal = ScoreOf(Gb(5)).Total;
        double system = ScoreOf(Gb(5), isSystem: true).Total;

        Assert.Equal(15, ScoreOf(Gb(5), isSystem: true).SystemRiskPenalty);
        Assert.True(system < normal);
    }

    [Fact]
    public void Score_OfficialMigration_ScoresAboveJunctionOnly()
    {
        double withOfficial = ScoreOf(Gb(5), official: MigrationMethod.OfficialSetting).Total;
        double junctionOnly = ScoreOf(Gb(5), official: MigrationMethod.None).Total;

        Assert.True(withOfficial > junctionOnly);
        Assert.Equal(6, ScoreOf(Gb(5), official: MigrationMethod.None).MigrationRiskPenalty);
    }

    [Fact]
    public void Score_RegeneratableContent_ScoresHigher()
    {
        double regeneratable = ScoreOf(Gb(5), canRegenerate: true).Total;
        double irreplaceable = ScoreOf(Gb(5), canRegenerate: false).Total;

        Assert.True(regeneratable > irreplaceable);
    }

    [Fact]
    public void Score_TotalMatchesComponentFormula()
    {
        var score = ScoreOf(Gb(8), isSystem: true, risk: RiskLevel.Medium);

        double expected = score.SizeScore + score.RegeneratableScore + score.KnownCacheScore
            + score.InactivityScore + score.DuplicateScore + score.MoveBenefitScore
            - score.SystemRiskPenalty - score.ActiveUsagePenalty - score.MigrationRiskPenalty;

        Assert.Equal(expected, score.Total, precision: 10);
    }
}
