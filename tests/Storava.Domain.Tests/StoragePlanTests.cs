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

    // --- what the user may choose for themselves ------------------------------------
    //
    // Most of a real disk is not in the rule catalog. Refusing to plan anything the catalog does
    // not recognise is what made the whole feature reachable for about three dozen folders.

    private static PlanCandidate Chosen(
        string scanItemId = "item-1",
        string path = @"C:\games\assets",
        long space = 4096,
        bool isFolder = true,
        bool isReparsePoint = false,
        RiskLevel risk = RiskLevel.Unknown,
        string sessionId = SessionId) => new()
        {
            SessionId = sessionId,
            ScanItemId = scanItemId,
            Path = path,
            Title = "assets",
            EstimatedSpace = space,
            RiskLevel = risk,
            IsFolder = isFolder,
            IsReparsePoint = isReparsePoint,
            // Nothing matched it, so both capability flags are false — not as a refusal, but
            // because no rule was ever consulted.
            IsIdentified = false,
            CanDelete = false,
            CanMove = false
        };

    [Theory]
    [InlineData(SuggestedAction.Delete)]
    [InlineData(SuggestedAction.Move)]
    public void Accepts_AnItemNoRuleRecognises(SuggestedAction action)
    {
        var plan = NewPlan();

        var result = plan.TryAdd(Chosen(), action, NextEntryId());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasNoRule);
        Assert.Null(result.Value.RecommendationId);
    }

    /// <summary>
    /// The catalog's refusal is knowledge, and knowledge outranks a click. Only silence is treated
    /// as permission — see PlanCandidate.Permits.
    /// </summary>
    [Fact]
    public void StillRefuses_WhatARuleExplicitlyForbids()
    {
        var plan = NewPlan();
        var known = Chosen() with { IsIdentified = true, CanDelete = false, CanMove = true };

        var result = plan.TryAdd(known, SuggestedAction.Delete, NextEntryId());

        Assert.True(result.IsFailure);
        Assert.Equal(PlanErrors.DeleteNotPermitted.Code, result.Error.Code);
    }

    /// <summary>
    /// A junction or symlink is a pointer. Deleting one frees nothing, and moving one would copy
    /// whatever it points at — data from somewhere the user never selected.
    /// </summary>
    [Theory]
    [InlineData(SuggestedAction.Delete)]
    [InlineData(SuggestedAction.Move)]
    public void Refuses_ALinkEvenWhenTheUserChoseIt(SuggestedAction action)
    {
        var plan = NewPlan();

        var result = plan.TryAdd(Chosen(isReparsePoint: true), action, NextEntryId());

        Assert.True(result.IsFailure);
        Assert.Equal(PlanErrors.IsLink.Code, result.Error.Code);
    }

    [Fact]
    public void Refuses_AProtectedItemEvenWhenTheUserChoseIt()
    {
        var plan = NewPlan();

        var result = plan.TryAdd(Chosen(risk: RiskLevel.Protected), SuggestedAction.Delete, NextEntryId());

        Assert.True(result.IsFailure);
        Assert.Equal(PlanErrors.ProtectedPath.Code, result.Error.Code);
    }

    /// <summary>
    /// Nothing in the catalog said how to relocate an unrecognised folder, but a move still has to
    /// leave something behind. A junction is the mechanism that needs no elevation.
    /// </summary>
    [Fact]
    public void AUserChosenMove_FallsBackToTheMechanismThatNeedsNoPrivilege()
    {
        var plan = NewPlan();

        var result = plan.TryAdd(Chosen(), SuggestedAction.Move, NextEntryId());

        Assert.True(result.IsSuccess);
        Assert.Equal(MigrationMethod.Junction, result.Value.Method);
    }

    [Fact]
    public void AFile_IsCarriedThroughAsAFile()
    {
        var plan = NewPlan();

        var result = plan.TryAdd(Chosen(isFolder: false), SuggestedAction.Move, NextEntryId());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsFolder);
    }

    [Fact]
    public void AdviceFromTheCatalog_IsNotMarkedAsUserChosen()
    {
        var plan = NewPlan();

        var result = plan.TryAdd(Advice(), SuggestedAction.Delete, NextEntryId());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasNoRule);
        Assert.NotNull(result.Value.RecommendationId);
    }

    // --- how a move is carried out ---------------------------------------------------
    //
    // A junction left at the old path is usually the whole reason for moving a folder rather than
    // deleting it: everything hard-coded to that path keeps working. Whether one is left is the
    // user's call, so what they chose has to survive into the step.

    [Fact]
    public void AskingForAJunction_PlansAJunction()
    {
        var plan = NewPlan();
        var candidate = Chosen() with { RequestedMethod = MigrationMethod.Junction };

        var result = plan.TryAdd(candidate, SuggestedAction.Move, NextEntryId());

        Assert.True(result.IsSuccess);
        Assert.Equal(MigrationMethod.Junction, result.Value.Method);
    }

    /// <summary>
    /// A move with no junction asked for must leave nothing behind. Falling back to a junction
    /// here would quietly do the opposite of what was chosen.
    /// </summary>
    [Fact]
    public void AskingForNoJunction_PlansNoLink()
    {
        var plan = NewPlan();
        var candidate = Chosen() with { RequestedMethod = MigrationMethod.None };

        var result = plan.TryAdd(candidate, SuggestedAction.Move, NextEntryId());

        Assert.True(result.IsSuccess);
        Assert.Equal(MigrationMethod.None, result.Value.Method);
    }

    /// <summary>The user's choice outranks what the catalog would have picked.</summary>
    [Fact]
    public void TheRequestedMechanism_OverridesTheCatalogs()
    {
        var plan = NewPlan();
        var candidate = Chosen() with
        {
            IsIdentified = true,
            CanMove = true,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            RequestedMethod = MigrationMethod.Junction
        };

        var result = plan.TryAdd(candidate, SuggestedAction.Move, NextEntryId());

        Assert.Equal(MigrationMethod.Junction, result.Value.Method);
    }

    /// <summary>Saying nothing still falls back to the mechanism that needs no privilege.</summary>
    [Fact]
    public void SayingNothing_StillFallsBackToAJunction()
    {
        var plan = NewPlan();

        var result = plan.TryAdd(Chosen(), SuggestedAction.Move, NextEntryId());

        Assert.Equal(MigrationMethod.Junction, result.Value.Method);
    }

    /// <summary>A delete has nothing to leave behind whatever was asked for.</summary>
    [Fact]
    public void ADelete_NeverCarriesAMechanism()
    {
        var plan = NewPlan();
        var candidate = Chosen() with { RequestedMethod = MigrationMethod.Junction };

        var result = plan.TryAdd(candidate, SuggestedAction.Delete, NextEntryId());

        Assert.Equal(MigrationMethod.None, result.Value.Method);
    }
}
