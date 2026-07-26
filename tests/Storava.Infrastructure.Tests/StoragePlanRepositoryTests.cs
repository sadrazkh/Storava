using Storava.Application.Abstractions;
using Storava.Application.Planning;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// Covers the phase-5 round trip: a plan drafted through the service survives a reload with its
/// steps, goal and de-duplicated total intact, and the protected-path gate holds at the service
/// boundary where the platform knowledge actually lives.
/// </summary>
public class StoragePlanRepositoryTests
{
    private const string SessionId = "session-plan";

    private static Recommendation Advice(
        string id,
        string scanItemId,
        string path,
        long space,
        bool canDelete = true,
        bool canMove = true,
        RiskLevel risk = RiskLevel.Low) => new()
        {
            Id = id,
            SessionId = SessionId,
            ScanItemId = scanItemId,
            Path = path,
            Title = $"Handle {scanItemId}",
            Reason = "It is a regenerable cache.",
            EstimatedSpace = space,
            RiskLevel = risk,
            CanDelete = canDelete,
            CanMove = canMove,
            OfficialMigrationMethod = MigrationMethod.OfficialSetting,
            OfficialMigrationHint = "Set the cache directory in the tool's settings."
        };

    [Fact]
    public async Task Plan_SurvivesAReload_WithStepsGoalAndTotal()
    {
        using var host = new TestHost();
        var planning = host.Get<StoragePlanService>();
        var recommendations = host.Get<IRecommendationRepository>();

        var advice = new[]
        {
            Advice("r1", "i1", @"C:\dev\nuget", 900),
            Advice("r2", "i2", @"C:\dev\npm", 400)
        };
        await recommendations.ReplaceForSessionAsync(SessionId, advice);

        var plan = await planning.LoadOrCreateAsync(SessionId);
        plan.GoalBytes = 1000;
        Assert.True(planning.Include(plan, advice[0], SuggestedAction.Delete).IsSuccess);
        Assert.True(planning.Include(plan, advice[1], SuggestedAction.Move).IsSuccess);
        await planning.SaveAsync(plan);

        var reloaded = await planning.LoadOrCreateAsync(SessionId);

        Assert.Equal(2, reloaded.Entries.Count);
        Assert.Equal(1000, reloaded.GoalBytes);
        Assert.Equal(1300, reloaded.TotalReclaimable);
        Assert.Equal(1, reloaded.MoveCount);
        Assert.Equal(1, reloaded.DeleteCount);

        var moved = reloaded.FindByScanItem("i2")!;
        Assert.Equal(SuggestedAction.Move, moved.Action);
        Assert.Equal(MigrationMethod.OfficialSetting, moved.Method);
        Assert.Equal("Set the cache directory in the tool's settings.", moved.MethodHint);
    }

    [Fact]
    public async Task NestedSteps_StayDeduplicated_AcrossAReload()
    {
        using var host = new TestHost();
        var planning = host.Get<StoragePlanService>();

        var parent = Advice("r1", "parent", @"C:\dev\cache", 900);
        var child = Advice("r2", "child", @"C:\dev\cache\pkg", 400);

        var plan = await planning.LoadOrCreateAsync(SessionId);
        planning.Include(plan, parent, SuggestedAction.Delete);
        planning.Include(plan, child, SuggestedAction.Delete);
        await planning.SaveAsync(plan);

        var reloaded = await planning.LoadOrCreateAsync(SessionId);

        Assert.Equal(900, reloaded.TotalReclaimable);
        Assert.True(reloaded.FindByScanItem("child")!.IsCovered);
    }

    [Fact]
    public async Task SavingAgain_ReplacesThePreviousPlan_RatherThanAppending()
    {
        using var host = new TestHost();
        var planning = host.Get<StoragePlanService>();

        var first = Advice("r1", "i1", @"C:\dev\a", 100);
        var second = Advice("r2", "i2", @"C:\dev\b", 200);

        var plan = await planning.LoadOrCreateAsync(SessionId);
        planning.Include(plan, first, SuggestedAction.Delete);
        await planning.SaveAsync(plan);

        var reopened = await planning.LoadOrCreateAsync(SessionId);
        planning.Exclude(reopened, "i1");
        planning.Include(reopened, second, SuggestedAction.Delete);
        await planning.SaveAsync(reopened);

        var reloaded = await planning.LoadOrCreateAsync(SessionId);

        var only = Assert.Single(reloaded.Entries);
        Assert.Equal("i2", only.ScanItemId);
        Assert.Equal(200, reloaded.TotalReclaimable);
    }

    [Fact]
    public async Task Refuses_AProtectedLocation_EvenWhenTheAdviceClaimsItIsSafe()
    {
        using var host = new TestHost();
        var planning = host.Get<StoragePlanService>();
        var protectedPaths = host.Get<IProtectedPathService>();

        string protectedRoot = protectedPaths.ProtectedRoots[0];
        // The capability flags are deliberately permissive; the gate must not rely on them.
        var advice = Advice("r1", "i1", Path.Combine(protectedRoot, "System32"), 5_000_000, risk: RiskLevel.Low);

        var plan = await planning.LoadOrCreateAsync(SessionId);
        var result = planning.Include(plan, advice, SuggestedAction.Delete);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.protected_path", result.Error.Code);
        Assert.Empty(plan.Entries);
    }

    [Fact]
    public async Task Discard_RemovesTheStoredPlan()
    {
        using var host = new TestHost();
        var planning = host.Get<StoragePlanService>();

        var plan = await planning.LoadOrCreateAsync(SessionId);
        planning.Include(plan, Advice("r1", "i1", @"C:\dev\a", 100), SuggestedAction.Delete);
        await planning.SaveAsync(plan);

        await planning.DiscardAsync(SessionId);

        var reloaded = await planning.LoadOrCreateAsync(SessionId);
        Assert.Empty(reloaded.Entries);
    }
}
