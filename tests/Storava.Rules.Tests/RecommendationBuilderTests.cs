using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Rules.Tests;

public class RecommendationBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    private static long Gb(double value) => (long)(value * 1024 * 1024 * 1024);

    [Fact]
    public void Build_AlwaysDefaultsToNoAction()
    {
        // The core safety guarantee: advice never pre-selects an action for the user.
        var items = new[]
        {
            TestFixtures.Folder(@"C:\Users\a\.nuget\packages", Gb(12)),
            TestFixtures.Folder(@"C:\Users\a\.gradle", Gb(8)),
            TestFixtures.Folder(@"D:\src\proj\node_modules", Gb(2))
        };

        var results = TestFixtures.Builder().Build("session", items, "en", Now);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(SuggestedAction.NoAction, r.SuggestedAction));
    }

    [Fact]
    public void Build_NeverRecommendsProtectedPaths()
    {
        var items = new[]
        {
            TestFixtures.Folder(@"C:\Windows\System32", Gb(20)),
            TestFixtures.Folder(@"C:\Windows\Temp", Gb(15)),
            TestFixtures.Folder(@"C:\Program Files\App\bin", Gb(30))
        };

        var results = TestFixtures.Builder().Build("session", items, "en", Now);

        Assert.Empty(results);
    }

    [Fact]
    public void Build_EveryRecommendationIsBoundToARealScanItem()
    {
        var item = TestFixtures.Folder(@"C:\Users\a\.nuget\packages", Gb(12));

        var results = TestFixtures.Builder().Build("session", [item], "en", Now);

        var recommendation = Assert.Single(results);
        Assert.Equal(item.Id, recommendation.ScanItemId);
        Assert.Equal(item.Path, recommendation.Path);
        Assert.Equal("session", recommendation.SessionId);
        Assert.Equal(RecommendationSource.RuleEngine, recommendation.Source);
    }

    [Fact]
    public void Build_SkipsUnidentifiedItems()
    {
        var items = new[]
        {
            TestFixtures.Folder(@"D:\Clients\Acme\Contracts", Gb(40)),
            TestFixtures.Folder(@"D:\Personal\Photos", Gb(30))
        };

        var results = TestFixtures.Builder().Build("session", items, "en", Now);

        Assert.Empty(results);
    }

    [Fact]
    public void Build_SkipsItemsBelowSizeThreshold()
    {
        var tiny = TestFixtures.Folder(@"D:\src\proj\node_modules", size: 1024 * 1024);

        var results = TestFixtures.Builder().Build("session", [tiny], "en", Now);

        Assert.Empty(results);
    }

    [Fact]
    public void Build_SkipsItemsThatAreNeitherDeletableNorMovable()
    {
        // .git is identified but intentionally not actionable.
        var repo = TestFixtures.Folder(@"D:\src\proj\.git", Gb(5));

        var results = TestFixtures.Builder().Build("session", [repo], "en", Now);

        Assert.Empty(results);
    }

    [Fact]
    public void Build_OrdersByScoreDescending()
    {
        var items = new[]
        {
            TestFixtures.Folder(@"D:\a\node_modules", Gb(1)),
            TestFixtures.Folder(@"C:\Users\a\.nuget\packages", Gb(40)),
            TestFixtures.Folder(@"C:\Users\a\.gradle", Gb(10))
        };

        var results = TestFixtures.Builder().Build("session", items, "en", Now);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Score >= results[1].Score);
        Assert.True(results[1].Score >= results[2].Score);
        Assert.Contains(".nuget", results[0].Path);
    }

    [Fact]
    public void Build_DropsNestedDuplicatesOfTheSameRule()
    {
        // An outer bin\ already covers the inner one; listing both is noise.
        var outer = TestFixtures.Folder(@"D:\src\proj\bin", Gb(10));
        var inner = TestFixtures.Folder(@"D:\src\proj\bin\Debug\bin", Gb(6));

        var results = TestFixtures.Builder().Build("session", [outer, inner], "en", Now);

        var single = Assert.Single(results);
        Assert.Equal(outer.Path, single.Path);
    }

    [Fact]
    public void Build_KeepsSiblingsOfTheSameRule()
    {
        var first = TestFixtures.Folder(@"D:\src\one\bin", Gb(4));
        var second = TestFixtures.Folder(@"D:\src\two\bin", Gb(3));

        var results = TestFixtures.Builder().Build("session", [first, second], "en", Now);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Build_RespectsLimit()
    {
        var items = Enumerable.Range(0, 20)
            .Select(i => TestFixtures.Folder($@"D:\src\p{i}\node_modules", Gb(1)))
            .ToArray();

        var results = TestFixtures.Builder().Build("session", items, "en", Now, limit: 5);

        Assert.Equal(5, results.Count);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fa")]
    public void Build_ProducesTextInRequestedLanguage(string language)
    {
        var item = TestFixtures.Folder(@"C:\Users\a\.nuget\packages", Gb(12));

        var recommendation = Assert.Single(TestFixtures.Builder().Build("session", [item], language, Now));

        Assert.NotEmpty(recommendation.Title);
        Assert.NotEmpty(recommendation.Reason);

        bool hasPersian = recommendation.Title.Any(c => c >= 0x0600 && c <= 0x06FF);
        Assert.Equal(language == "fa", hasPersian);
    }

    [Fact]
    public void Build_CarriesMigrationGuidanceAndWarnings()
    {
        var item = TestFixtures.Folder(@"C:\Users\a\.nuget\packages", Gb(12));

        var recommendation = Assert.Single(TestFixtures.Builder().Build("session", [item], "en", Now));

        Assert.Equal(MigrationMethod.OfficialSetting, recommendation.OfficialMigrationMethod);
        Assert.Equal(MigrationMethod.Junction, recommendation.FallbackMigrationMethod);
        Assert.Contains("NUGET_PACKAGES", recommendation.OfficialMigrationHint);
        Assert.NotNull(recommendation.Warning);
    }

    [Fact]
    public void Build_ReportsReclaimableSpaceAsItemSize()
    {
        var item = TestFixtures.Folder(@"C:\Users\a\.gradle", Gb(7));

        var recommendation = Assert.Single(TestFixtures.Builder().Build("session", [item], "en", Now));

        Assert.Equal(Gb(7), recommendation.EstimatedSpace);
    }
}
