using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Application.Services;
using Storava.Domain.Enums;
using Storava.Rules;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// End-to-end coverage of the phase-3 pipeline: scanning classifies items as it persists them,
/// and analysis turns those rows into stored recommendations.
/// </summary>
public class ClassifiedScanIntegrationTests
{
    private static async Task<ScanResult> ScanAsync(TestHost host, string root)
    {
        var coordinator = host.Get<ScanCoordinator>();
        return await coordinator.RunAsync(
            new ScanRequest { RootPath = root },
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            CancellationToken.None);
    }

    [Fact]
    public async Task Scan_WithRules_PersistsClassification()
    {
        using var tree = new TestTree();
        // A recognisable developer cache inside the scanned tree.
        tree.AddFile(@"proj\node_modules\lib\index.js", 2048);
        tree.AddFile(@"proj\src\app.ts", 512);

        using var host = new TestHost(withRules: true);
        var result = await ScanAsync(host, tree.Root);

        var query = host.Get<IScanQueryService>();
        var items = await query.SearchAsync(result.SessionId, "node_modules", 10);

        var nodeModules = Assert.Single(items);
        Assert.Equal("npm.node-modules", nodeModules.KnownRuleId);
        Assert.Equal(StorageCategory.PackageCaches, nodeModules.Category);
        Assert.Equal("npm", nodeModules.DetectedTechnology);
        Assert.Equal(RiskLevel.Low, nodeModules.RiskLevel);
        Assert.True(nodeModules.CanDelete);
        Assert.True(nodeModules.CanRegenerate);
    }

    [Fact]
    public async Task Scan_WithoutRules_LeavesItemsUnclassified()
    {
        using var tree = new TestTree();
        tree.AddFile(@"proj\node_modules\lib\index.js", 2048);

        using var host = new TestHost(withRules: false);
        var result = await ScanAsync(host, tree.Root);

        var items = await host.Get<IScanQueryService>().SearchAsync(result.SessionId, "node_modules", 10);

        var nodeModules = Assert.Single(items);
        Assert.Null(nodeModules.KnownRuleId);
        Assert.Equal(StorageCategory.Unknown, nodeModules.Category);
    }

    [Fact]
    public async Task Scan_ClassifiesUnknownUserFoldersAsUnknown()
    {
        using var tree = new TestTree();
        tree.AddFile(@"ClientWork\contract.bin", 1024);

        using var host = new TestHost(withRules: true);
        var result = await ScanAsync(host, tree.Root);

        var items = await host.Get<IScanQueryService>().SearchAsync(result.SessionId, "ClientWork", 10);

        var folder = Assert.Single(items);
        Assert.Null(folder.KnownRuleId);
        Assert.False(folder.CanDelete);
        Assert.False(folder.CanMove);
    }

    [Fact]
    public async Task CategoryUsage_AttributesFilesToTheirOwnCategory()
    {
        using var tree = new TestTree();
        tree.AddFile(@"media\clip.mp4", 4096);
        tree.AddFile(@"docs\notes.pdf", 2048);

        using var host = new TestHost(withRules: true);
        var result = await ScanAsync(host, tree.Root);

        var usage = await host.Get<IScanQueryService>().GetCategoryUsageAsync(result.SessionId);

        Assert.Contains(usage, u => u.Category == StorageCategory.Media && u.TotalSize == 4096);
        Assert.Contains(usage, u => u.Category == StorageCategory.PersonalFiles && u.TotalSize == 2048);

        // Everything is accounted for exactly once.
        Assert.Equal(result.TotalSize, usage.Sum(u => u.TotalSize));
    }

    [Fact]
    public async Task CategoryUsage_CountsWholeSubtreeOfAClassifiedFolder()
    {
        using var tree = new TestTree();
        // The files inside node_modules are not individually recognisable, but the folder is:
        // the entire subtree must be reported as package cache.
        tree.AddFile(@"proj\node_modules\a\index.js", 3000);
        tree.AddFile(@"proj\node_modules\b\index.js", 1000);
        tree.AddFile(@"proj\src\app.ts", 500);

        using var host = new TestHost(withRules: true);
        var result = await ScanAsync(host, tree.Root);

        var usage = await host.Get<IScanQueryService>().GetCategoryUsageAsync(result.SessionId);

        var packages = Assert.Single(usage, u => u.Category == StorageCategory.PackageCaches);
        Assert.Equal(4000, packages.TotalSize);

        // The remaining source file is not classified.
        var unknown = Assert.Single(usage, u => u.Category == StorageCategory.Unknown);
        Assert.Equal(500, unknown.TotalSize);
        Assert.Equal(result.TotalSize, usage.Sum(u => u.TotalSize));
    }

    [Fact]
    public async Task CategoryUsage_DoesNotDoubleCountNestedClassifiedFolders()
    {
        using var tree = new TestTree();
        // A bin folder inside node_modules must not be counted twice.
        tree.AddFile(@"proj\node_modules\pkg\bin\tool.exe", 2000);
        tree.AddFile(@"proj\node_modules\pkg\index.js", 1000);

        using var host = new TestHost(withRules: true);
        var result = await ScanAsync(host, tree.Root);

        var usage = await host.Get<IScanQueryService>().GetCategoryUsageAsync(result.SessionId);

        var packages = Assert.Single(usage, u => u.Category == StorageCategory.PackageCaches);
        Assert.Equal(3000, packages.TotalSize);
        Assert.DoesNotContain(usage, u => u.Category == StorageCategory.BuildArtifacts);
        Assert.Equal(result.TotalSize, usage.Sum(u => u.TotalSize));
    }

    [Fact]
    public async Task Analyze_StoresRecommendationsForLargeKnownCache()
    {
        using var tree = new TestTree();
        // Above the 50 MB recommendation threshold.
        tree.AddFile(@"proj\node_modules\big.bin", 60 * 1024 * 1024);

        using var host = new TestHost(withRules: true);
        var result = await ScanAsync(host, tree.Root);

        var analysis = host.Get<AnalysisService>();
        var recommendations = await analysis.AnalyzeAsync(result.SessionId, "en");

        var recommendation = Assert.Single(recommendations);
        Assert.Equal("npm.node-modules", recommendation.RuleId);
        Assert.Equal(SuggestedAction.NoAction, recommendation.SuggestedAction);

        // And they are readable back from storage.
        var stored = await host.Get<IRecommendationRepository>().GetBySessionAsync(result.SessionId);
        var storedItem = Assert.Single(stored);
        Assert.Equal(recommendation.ScanItemId, storedItem.ScanItemId);
        Assert.Equal(SuggestedAction.NoAction, storedItem.SuggestedAction);
    }

    [Fact]
    public async Task Analyze_IsIdempotent()
    {
        using var tree = new TestTree();
        tree.AddFile(@"proj\node_modules\big.bin", 60 * 1024 * 1024);

        using var host = new TestHost(withRules: true);
        var result = await ScanAsync(host, tree.Root);
        var analysis = host.Get<AnalysisService>();

        await analysis.AnalyzeAsync(result.SessionId, "en");
        await analysis.AnalyzeAsync(result.SessionId, "en");

        var stored = await host.Get<IRecommendationRepository>().GetBySessionAsync(result.SessionId);
        Assert.Single(stored);
    }

    [Fact]
    public async Task Analyze_ProducesNothingWhenOnlySmallOrUnknownItemsExist()
    {
        using var tree = new TestTree();
        tree.AddFile(@"ClientWork\contract.bin", 1024);
        tree.AddFile(@"proj\node_modules\tiny.bin", 1024);

        using var host = new TestHost(withRules: true);
        var result = await ScanAsync(host, tree.Root);

        var recommendations = await host.Get<AnalysisService>().AnalyzeAsync(result.SessionId, "en");

        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task Analyze_RegeneratesTextInRequestedLanguage()
    {
        using var tree = new TestTree();
        tree.AddFile(@"proj\node_modules\big.bin", 60 * 1024 * 1024);

        using var host = new TestHost(withRules: true);
        var result = await ScanAsync(host, tree.Root);
        var analysis = host.Get<AnalysisService>();

        var english = Assert.Single(await analysis.AnalyzeAsync(result.SessionId, "en"));
        var persian = Assert.Single(await analysis.AnalyzeAsync(result.SessionId, "fa"));

        Assert.DoesNotContain(english.Title, c => c >= 0x0600 && c <= 0x06FF);
        Assert.Contains(persian.Title, c => c >= 0x0600 && c <= 0x06FF);
    }

    [Fact]
    public async Task TreemapChildren_ReturnsLargestFirstAndSkipsEmpty()
    {
        using var tree = new TestTree();
        tree.AddFile(@"big\a.bin", 8192);
        tree.AddFile(@"small\b.bin", 1024);
        tree.AddDirectory("empty");

        using var host = new TestHost(withRules: true);
        var result = await ScanAsync(host, tree.Root);

        var query = host.Get<IScanQueryService>();
        var roots = await query.GetRootsAsync(result.SessionId);
        var children = await query.GetTreemapChildrenAsync(result.SessionId, roots[0].Id, 50);

        Assert.Equal(["big", "small"], children.Select(c => c.Name));
    }
}
