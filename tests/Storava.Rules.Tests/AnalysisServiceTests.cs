using Storava.Application.Scanning;
using Storava.Domain.Enums;

namespace Storava.Rules.Tests;

/// <summary>
/// Covers building recommendations from persisted (already classified) rows, which is the path
/// the app actually uses after a scan.
/// </summary>
public class PersistedRecommendationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    private static long Gb(double value) => (long)(value * 1024 * 1024 * 1024);

    private static ScanItemView View(
        string path,
        long size,
        string? ruleId = "nuget.global-packages",
        StorageCategory category = StorageCategory.PackageCaches,
        RiskLevel risk = RiskLevel.Low,
        bool canDelete = true,
        bool canMove = true,
        bool isProtected = false) => new(
            Id: Guid.NewGuid().ToString("N"),
            ParentId: null,
            Path: path,
            Name: System.IO.Path.GetFileName(path.TrimEnd('\\')),
            Extension: null,
            ItemType: ItemType.Folder,
            Size: size,
            AllocatedSize: size,
            FileCount: 100,
            FolderCount: 10,
            Depth: 3,
            CreationTime: Now.AddYears(-1),
            LastWriteTime: Now.AddDays(-120),
            IsReparsePoint: false,
            IsProtected: isProtected,
            IsHidden: false,
            IsSystem: false,
            RiskLevel: risk,
            Category: category,
            DetectedTechnology: "NuGet",
            KnownRuleId: ruleId,
            Confidence: 0.98,
            CanDelete: canDelete,
            CanMove: canMove,
            CanRegenerate: true);

    [Fact]
    public void BuildFromPersisted_ProducesAdviceWithNoActionDefault()
    {
        var views = new[] { View(@"C:\Users\a\.nuget\packages", Gb(12)) };

        var results = TestFixtures.Builder().BuildFromPersisted("session", views, "en", Now);

        var recommendation = Assert.Single(results);
        Assert.Equal(SuggestedAction.NoAction, recommendation.SuggestedAction);
        Assert.Equal(views[0].Id, recommendation.ScanItemId);
        Assert.Equal(Gb(12), recommendation.EstimatedSpace);
        Assert.True(recommendation.Score > 0);
    }

    [Fact]
    public void BuildFromPersisted_SkipsProtectedRows()
    {
        var views = new[]
        {
            View(@"C:\Windows\Temp", Gb(9), isProtected: true),
            View(@"C:\Windows\System32", Gb(20), risk: RiskLevel.Protected)
        };

        var results = TestFixtures.Builder().BuildFromPersisted("session", views, "en", Now);

        Assert.Empty(results);
    }

    [Fact]
    public void BuildFromPersisted_SkipsUnidentifiedRows()
    {
        var views = new[] { View(@"D:\Clients\Acme", Gb(30), ruleId: null) };

        var results = TestFixtures.Builder().BuildFromPersisted("session", views, "en", Now);

        Assert.Empty(results);
    }

    [Fact]
    public void BuildFromPersisted_SkipsNonActionableRows()
    {
        var views = new[] { View(@"D:\src\p\.git", Gb(6), canDelete: false, canMove: false) };

        var results = TestFixtures.Builder().BuildFromPersisted("session", views, "en", Now);

        Assert.Empty(results);
    }

    [Fact]
    public void BuildFromPersisted_SkipsRowsBelowThreshold()
    {
        var views = new[] { View(@"C:\Users\a\.nuget\packages", 1024 * 1024) };

        var results = TestFixtures.Builder().BuildFromPersisted("session", views, "en", Now);

        Assert.Empty(results);
    }

    [Fact]
    public void BuildFromPersisted_IgnoresRowsWhoseRuleNoLongerExists()
    {
        // A scan from an older catalog version may reference a rule that has since been removed.
        var views = new[] { View(@"D:\whatever", Gb(5), ruleId: "removed.rule.id") };

        var results = TestFixtures.Builder().BuildFromPersisted("session", views, "en", Now);

        Assert.Empty(results);
    }

    [Fact]
    public void BuildFromPersisted_RanksLargerItemsFirst()
    {
        var views = new[]
        {
            View(@"D:\a\node_modules", Gb(2), "npm.node-modules"),
            View(@"C:\Users\a\.nuget\packages", Gb(30)),
            View(@"D:\b\node_modules", Gb(9), "npm.node-modules")
        };

        var results = TestFixtures.Builder().BuildFromPersisted("session", views, "en", Now);

        Assert.Equal(3, results.Count);
        Assert.Contains(".nuget", results[0].Path);
        Assert.True(results[0].Score >= results[1].Score);
        Assert.True(results[1].Score >= results[2].Score);
    }
}
