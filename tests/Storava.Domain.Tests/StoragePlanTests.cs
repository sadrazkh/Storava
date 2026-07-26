using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Domain.Tests;

/// <summary>
/// A plan is the document a user acts on, so its two dangerous failure modes are covered here:
/// describing a step the local rules never permitted, and overstating how much space it frees.
/// </summary>
public class StoragePlanTests
{
    private const string SessionId = "session-1";
    private static int _counter;

    private static StoragePlan NewPlan() => new()
    {
        Id = "plan-1",
        SessionId = SessionId
    };

    private static Recommendation Advice(
        string scanItemId = "item-1",
        string path = @"C:\cache\nuget",
        long space = 1024,
        bool canMove = true,
        bool canDelete = true,
        RiskLevel risk = RiskLevel.Low,
        string sessionId = SessionId,
        MigrationMethod official = MigrationMethod.None,
        MigrationMethod fallback = MigrationMethod.None) => new()
        {
            Id = $"rec-{Interlocked.Increment(ref _counter)}",
            SessionId = sessionId,
            ScanItemId = scanItemId,
            Path = path,
            Title = "Move the cache",
            Reason = "It is rebuilt on demand.",
            EstimatedSpace = space,
            RiskLevel = risk,
            CanMove = canMove,
            CanDelete = canDelete,
            OfficialMigrationMethod = official,
            FallbackMigrationMethod = fallback
        };

    private static string NextEntryId() => $"entry-{Interlocked.Increment(ref _counter)}";

    // --- what may never enter a plan -------------------------------------------------

    [Theory]
    [InlineData(SuggestedAction.Review)]
    [InlineData(SuggestedAction.NoAction)]
    [InlineData(SuggestedAction.Ignore)]
    public void Rejects_ActionsThatAreNotSteps(SuggestedAction action)
    {
        var result = NewPlan().TryAdd(Advice(), action, NextEntryId());

        Assert.True(result.IsFailure);
        Assert.Equal("plan.not_plannable", result.Error.Code);
    }

    [Fact]
    public void Rejects_Delete_WhenTheRulesDoNotAllowIt()
    {
        var result = NewPlan().TryAdd(Advice(canDelete: false), SuggestedAction.Delete, NextEntryId());

        Assert.True(result.IsFailure);
        Assert.Equal("plan.delete_not_permitted", result.Error.Code);
    }

    [Fact]
    public void Rejects_Move_WhenTheRulesDoNotAllowIt()
    {
        var result = NewPlan().TryAdd(Advice(canMove: false), SuggestedAction.Move, NextEntryId());

        Assert.True(result.IsFailure);
        Assert.Equal("plan.move_not_permitted", result.Error.Code);
    }

    [Fact]
    public void Rejects_ProtectedItems_EvenWhenTheFlagsSayOtherwise()
    {
        // A protected item must never be planned, whatever the capability flags claim.
        var result = NewPlan().TryAdd(
            Advice(risk: RiskLevel.Protected, canDelete: true, canMove: true),
            SuggestedAction.Delete,
            NextEntryId());

        Assert.True(result.IsFailure);
        Assert.Equal("plan.protected_path", result.Error.Code);
    }

    [Fact]
    public void Rejects_AdviceFromAnotherScan()
    {
        var result = NewPlan().TryAdd(Advice(sessionId: "other"), SuggestedAction.Move, NextEntryId());

        Assert.True(result.IsFailure);
        Assert.Equal("plan.wrong_session", result.Error.Code);
    }

    [Fact]
    public void Rejects_TheSameItemTwice()
    {
        var plan = NewPlan();
        Assert.True(plan.TryAdd(Advice(), SuggestedAction.Move, NextEntryId()).IsSuccess);

        var second = plan.TryAdd(Advice(), SuggestedAction.Delete, NextEntryId());

        Assert.True(second.IsFailure);
        Assert.Equal("plan.already_added", second.Error.Code);
        Assert.Single(plan.Entries);
    }

    // --- the total must be honest -----------------------------------------------------

    [Fact]
    public void NestedStep_IsCounted_Once()
    {
        var plan = NewPlan();
        plan.TryAdd(Advice("parent", @"C:\dev\node_modules", 900), SuggestedAction.Delete, NextEntryId());
        plan.TryAdd(Advice("child", @"C:\dev\node_modules\lib", 400), SuggestedAction.Delete, NextEntryId());

        var child = plan.FindByScanItem("child")!;

        Assert.True(child.IsCovered);
        Assert.Equal(0, child.EffectiveSpace);
        // Not 1300: deleting the parent already removes the child.
        Assert.Equal(900, plan.TotalReclaimable);
    }

    [Fact]
    public void CoverageIsRecomputed_WhenTheCoveringStepIsRemoved()
    {
        var plan = NewPlan();
        plan.TryAdd(Advice("parent", @"C:\dev\node_modules", 900), SuggestedAction.Delete, NextEntryId());
        plan.TryAdd(Advice("child", @"C:\dev\node_modules\lib", 400), SuggestedAction.Delete, NextEntryId());

        plan.RemoveByScanItem("parent");

        var child = plan.FindByScanItem("child")!;
        Assert.False(child.IsCovered);
        Assert.Equal(400, plan.TotalReclaimable);
    }

    [Fact]
    public void SiblingWithASharedPrefix_IsNotTreatedAsNested()
    {
        var plan = NewPlan();
        plan.TryAdd(Advice("a", @"C:\data", 100), SuggestedAction.Delete, NextEntryId());
        plan.TryAdd(Advice("b", @"C:\database", 50), SuggestedAction.Delete, NextEntryId());

        Assert.False(plan.FindByScanItem("b")!.IsCovered);
        Assert.Equal(150, plan.TotalReclaimable);
    }

    [Fact]
    public void DeeplyNestedSteps_CollapseToTheOutermostFolder()
    {
        var plan = NewPlan();
        plan.TryAdd(Advice("a", @"C:\a", 500), SuggestedAction.Delete, NextEntryId());
        plan.TryAdd(Advice("b", @"C:\a\b", 300), SuggestedAction.Delete, NextEntryId());
        plan.TryAdd(Advice("c", @"C:\a\b\c", 200), SuggestedAction.Delete, NextEntryId());

        Assert.Equal(500, plan.TotalReclaimable);
        Assert.True(plan.FindByScanItem("b")!.IsCovered);
        Assert.True(plan.FindByScanItem("c")!.IsCovered);
    }

    // --- ordering and summary ---------------------------------------------------------

    [Fact]
    public void OrdersSteps_SafestFirst_WithUnknownRankedAboveLowAndMedium()
    {
        var plan = NewPlan();
        plan.TryAdd(Advice("high", @"C:\p1", 10, risk: RiskLevel.High), SuggestedAction.Delete, NextEntryId());
        plan.TryAdd(Advice("unknown", @"C:\p2", 10, risk: RiskLevel.Unknown), SuggestedAction.Delete, NextEntryId());
        plan.TryAdd(Advice("low", @"C:\p3", 10, risk: RiskLevel.Low), SuggestedAction.Delete, NextEntryId());
        plan.TryAdd(Advice("medium", @"C:\p4", 10, risk: RiskLevel.Medium), SuggestedAction.Delete, NextEntryId());

        var order = plan.Entries.OrderBy(e => e.Order).Select(e => e.ScanItemId).ToArray();

        Assert.Equal(["low", "medium", "unknown", "high"], order);
    }

    [Fact]
    public void PrefersAnOfficialRelocationMethod_OverALinkFallback()
    {
        var plan = NewPlan();
        plan.TryAdd(
            Advice("link", @"C:\p1", 10, fallback: MigrationMethod.Junction),
            SuggestedAction.Move, NextEntryId());
        plan.TryAdd(
            Advice("official", @"C:\p2", 10, official: MigrationMethod.OfficialSetting),
            SuggestedAction.Move, NextEntryId());

        Assert.Equal(1, plan.FindByScanItem("official")!.Order);
        Assert.Equal(MigrationMethod.Junction, plan.FindByScanItem("link")!.Method);
    }

    [Fact]
    public void HighestRisk_ReportsTheWorstStep_NotTheLowestEnumValue()
    {
        var plan = NewPlan();
        // Unknown is 0 in the enum but is *not* the safest thing in a plan.
        plan.TryAdd(Advice("a", @"C:\p1", 10, risk: RiskLevel.Low), SuggestedAction.Delete, NextEntryId());
        plan.TryAdd(Advice("b", @"C:\p2", 10, risk: RiskLevel.Unknown), SuggestedAction.Delete, NextEntryId());

        Assert.Equal(RiskLevel.Unknown, plan.HighestRisk);
    }

    [Fact]
    public void CountsMovesAndDeletesSeparately()
    {
        var plan = NewPlan();
        plan.TryAdd(Advice("a", @"C:\p1"), SuggestedAction.Move, NextEntryId());
        plan.TryAdd(Advice("b", @"C:\p2"), SuggestedAction.Delete, NextEntryId());
        plan.TryAdd(Advice("c", @"C:\p3"), SuggestedAction.Delete, NextEntryId());

        Assert.Equal(1, plan.MoveCount);
        Assert.Equal(2, plan.DeleteCount);
    }

    [Fact]
    public void GoalProgress_TracksTheDedupedTotal_AndIsClamped()
    {
        var plan = NewPlan();
        plan.GoalBytes = 1000;

        Assert.Equal(0, plan.GoalProgress);
        Assert.False(plan.MeetsGoal);

        plan.TryAdd(Advice("a", @"C:\p1", 500), SuggestedAction.Delete, NextEntryId());
        Assert.Equal(0.5, plan.GoalProgress, 3);

        plan.TryAdd(Advice("b", @"C:\p2", 5000), SuggestedAction.Delete, NextEntryId());
        Assert.Equal(1, plan.GoalProgress);
        Assert.True(plan.MeetsGoal);
    }

    [Fact]
    public void MoveStep_DoesNotCarryADeleteOnlyMigrationMethod()
    {
        var plan = NewPlan();
        plan.TryAdd(
            Advice("a", @"C:\p1", official: MigrationMethod.OfficialSetting),
            SuggestedAction.Delete, NextEntryId());

        // A delete has nowhere to move to, so no relocation method belongs on the step.
        Assert.Equal(MigrationMethod.None, plan.FindByScanItem("a")!.Method);
    }

    [Fact]
    public void Load_RederivesCoverageAndOrder_FromStoredEntries()
    {
        var stored = new[]
        {
            new StoragePlanEntry
            {
                Id = "e1", PlanId = "plan-1", RecommendationId = "r1", ScanItemId = "child",
                Path = @"C:\a\b", Title = "child", Action = SuggestedAction.Delete,
                EstimatedSpace = 300, RiskLevel = RiskLevel.Low,
                // Deliberately wrong on disk: Load must not trust these.
                Order = 99, CoveredByEntryId = null
            },
            new StoragePlanEntry
            {
                Id = "e2", PlanId = "plan-1", RecommendationId = "r2", ScanItemId = "parent",
                Path = @"C:\a", Title = "parent", Action = SuggestedAction.Delete,
                EstimatedSpace = 700, RiskLevel = RiskLevel.Low,
                Order = 1
            }
        };

        var plan = NewPlan();
        plan.Load(stored);

        Assert.Equal(700, plan.TotalReclaimable);
        Assert.True(plan.FindByScanItem("child")!.IsCovered);
        Assert.Equal(1, plan.FindByScanItem("parent")!.Order);
    }

    [Fact]
    public void Clear_EmptiesThePlan()
    {
        var plan = NewPlan();
        plan.TryAdd(Advice(), SuggestedAction.Move, NextEntryId());

        plan.Clear();

        Assert.Empty(plan.Entries);
        Assert.Equal(0, plan.TotalReclaimable);
    }
}
