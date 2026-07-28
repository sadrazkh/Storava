using Storava.Application.Abstractions;
using Storava.Application.Planning;
using Storava.Application.Scanning;
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

    /// <summary>
    /// A step the user chose for themselves has to come back as one.
    /// <para>
    /// Losing <c>IsFolder</c> across the reload would be silent and expensive: a file step read
    /// back as a folder asks for a junction, which the operating system refuses for anything that
    /// is not a directory, so the move would run and then fail at the last stage.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AUserChosenStep_SurvivesAReloadWithItsKindAndItsMissingRule()
    {
        using var host = new TestHost();
        var planning = host.Get<StoragePlanService>();

        var plan = await planning.LoadOrCreateAsync(SessionId);

        var folder = Chosen("i-folder", @"D:\Games\SomeGame", 40_000, isFolder: true);
        var file = Chosen("i-file", @"D:\media\raw.mkv", 8_000, isFolder: false);

        Assert.True(planning.Include(plan, folder, SessionId, SuggestedAction.Delete).IsSuccess);
        Assert.True(planning.Include(plan, file, SessionId, SuggestedAction.Move).IsSuccess);
        await planning.SaveAsync(plan);

        var reloaded = await planning.LoadOrCreateAsync(SessionId);

        var reloadedFolder = reloaded.FindByScanItem("i-folder")!;
        var reloadedFile = reloaded.FindByScanItem("i-file")!;

        Assert.True(reloadedFolder.IsFolder);
        Assert.False(reloadedFile.IsFolder);

        // Both were picked by hand, so neither has advice behind it.
        Assert.True(reloadedFolder.HasNoRule);
        Assert.True(reloadedFile.HasNoRule);
        Assert.Null(reloadedFile.RecommendationId);
    }

    /// <summary>Advice from the catalog must not come back looking user-chosen.</summary>
    [Fact]
    public async Task AdviceFromTheCatalog_SurvivesAReloadStillLinkedToItsRecommendation()
    {
        using var host = new TestHost();
        var planning = host.Get<StoragePlanService>();
        var recommendations = host.Get<IRecommendationRepository>();

        var advice = Advice("r1", "i1", @"C:\dev\nuget", 900);
        await recommendations.ReplaceForSessionAsync(SessionId, [advice]);

        var plan = await planning.LoadOrCreateAsync(SessionId);
        Assert.True(planning.Include(plan, advice, SuggestedAction.Delete).IsSuccess);
        await planning.SaveAsync(plan);

        var reloaded = (await planning.LoadOrCreateAsync(SessionId)).FindByScanItem("i1")!;

        Assert.False(reloaded.HasNoRule);
        Assert.Equal("r1", reloaded.RecommendationId);
        Assert.True(reloaded.IsFolder);
    }

    private static ScanItemView Chosen(string id, string path, long size, bool isFolder) => new(
        Id: id,
        ParentId: null,
        Path: path,
        Name: System.IO.Path.GetFileName(path),
        Extension: null,
        ItemType: isFolder ? ItemType.Folder : ItemType.File,
        Size: size,
        AllocatedSize: size,
        FileCount: isFolder ? 10 : 0,
        FolderCount: 0,
        Depth: 2,
        CreationTime: null,
        LastWriteTime: null,
        IsReparsePoint: false,
        IsProtected: false,
        IsHidden: false,
        IsSystem: false,
        RiskLevel: RiskLevel.Unknown,
        Category: StorageCategory.Unknown,
        DetectedTechnology: null,
        KnownRuleId: null,
        Confidence: 0,
        CanDelete: false,
        CanMove: false,
        CanRegenerate: false);
}
