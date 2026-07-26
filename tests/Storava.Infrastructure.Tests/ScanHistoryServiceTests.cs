using Storava.Application.Abstractions;
using Storava.Application.History;
using Storava.Application.Scanning;
using Storava.Application.Services;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Rules;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// End-to-end coverage of the phase-6 history: two real scans of the same tree are compared
/// through SQLite, and deleting a stored scan takes its derived rows with it while leaving the
/// record of real disk changes alone.
/// </summary>
public class ScanHistoryServiceTests
{
    private static async Task<ScanResult> ScanAsync(TestHost host, string root) =>
        await host.Get<ScanCoordinator>().RunAsync(
            new ScanRequest { RootPath = root },
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            CancellationToken.None);

    [Fact]
    public async Task Compare_FindsTheFolderThatGrewBetweenTwoRealScans()
    {
        using var tree = new TestTree();
        tree.AddFile(@"proj\node_modules\lib\index.js", 4 * 1024 * 1024);
        tree.AddFile(@"proj\src\app.ts", 512);

        using var host = new TestHost(withRules: true);
        var first = await ScanAsync(host, tree.Root);

        // The cache doubles, exactly what the user wants explained on their next scan.
        tree.AddFile(@"proj\node_modules\lib\extra.js", 4 * 1024 * 1024);
        var second = await ScanAsync(host, tree.Root);

        var result = await host.Get<ScanHistoryService>().CompareAsync(first.SessionId, second.SessionId);

        Assert.True(result.IsSuccess);
        var change = result.Value.Changes.First(c => c.Name == "node_modules");
        Assert.Equal(FolderChangeKind.Grew, change.Kind);
        Assert.True(change.Delta >= 4 * 1024 * 1024);
    }

    [Fact]
    public async Task Compare_OrdersTheScansByWhenTheyRanNotByHowTheyWerePassed()
    {
        using var tree = new TestTree();
        tree.AddFile(@"proj\cache\a.bin", 4 * 1024 * 1024);

        using var host = new TestHost();
        var first = await ScanAsync(host, tree.Root);
        tree.AddFile(@"proj\cache\b.bin", 4 * 1024 * 1024);
        var second = await ScanAsync(host, tree.Root);

        // Passed newest-first on purpose: the result must still read as growth, not shrinkage.
        var result = await host.Get<ScanHistoryService>().CompareAsync(second.SessionId, first.SessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(first.SessionId, result.Value.Baseline.Id);
        Assert.True(result.Value.Delta > 0);
    }

    [Fact]
    public async Task Compare_RefusesTwoScansOfDifferentFolders()
    {
        using var one = new TestTree();
        using var two = new TestTree();
        one.AddFile(@"a\x.bin", 1024);
        two.AddFile(@"b\y.bin", 1024);

        using var host = new TestHost();
        var first = await ScanAsync(host, one.Root);
        var second = await ScanAsync(host, two.Root);

        var result = await host.Get<ScanHistoryService>().CompareAsync(first.SessionId, second.SessionId);

        Assert.True(result.IsFailure);
        Assert.Equal(ComparisonErrors.DifferentRoots, result.Error);
    }

    [Fact]
    public async Task Compare_RefusesAScanAgainstItself()
    {
        using var tree = new TestTree();
        tree.AddFile(@"a\x.bin", 1024);

        using var host = new TestHost();
        var scan = await ScanAsync(host, tree.Root);

        var result = await host.Get<ScanHistoryService>().CompareAsync(scan.SessionId, scan.SessionId);

        Assert.Equal(ComparisonErrors.SameSession, result.Error);
    }

    [Fact]
    public async Task Trend_ReturnsCompletedScansOfOneRootOldestFirst()
    {
        using var tree = new TestTree();
        tree.AddFile(@"a\x.bin", 1024);

        using var host = new TestHost();
        var first = await ScanAsync(host, tree.Root);
        var second = await ScanAsync(host, tree.Root);

        var trend = await host.Get<ScanHistoryService>().GetTrendAsync(tree.Root);

        Assert.Equal(2, trend.Count);
        Assert.Equal(first.SessionId, trend[0].Id);
        Assert.Equal(second.SessionId, trend[1].Id);
    }

    [Fact]
    public async Task DeleteSession_TakesTheItemsAndAdviceButKeepsTheRecordOfRealChanges()
    {
        using var tree = new TestTree();
        // Above the 50 MB recommendation threshold, so the scan actually produces advice to delete.
        tree.AddFile(@"proj\node_modules\big.bin", 60 * 1024 * 1024);

        using var host = new TestHost(withRules: true);
        var scan = await ScanAsync(host, tree.Root);
        await host.Get<AnalysisService>().AnalyzeAsync(scan.SessionId, "en");

        var recommendations = host.Get<IRecommendationRepository>();
        Assert.NotEmpty(await recommendations.GetBySessionAsync(scan.SessionId));

        // A run of the plan: this is the audit trail, and it must outlive the scan.
        var executions = host.Get<IPlanExecutionRepository>();
        var execution = new PlanExecution
        {
            Id = Guid.NewGuid().ToString("n"),
            PlanId = "plan",
            SessionId = scan.SessionId
        };
        execution.Add(new PlanExecutionStep
        {
            Id = Guid.NewGuid().ToString("n"),
            ExecutionId = execution.Id,
            PlanEntryId = "entry",
            ScanItemId = "item",
            SourcePath = @"D:\dev\node_modules",
            Title = "Node packages",
            Action = SuggestedAction.Delete,
            Status = ExecutionStatus.Completed,
            BytesFreed = 2048
        });
        await executions.SaveAsync(execution);

        await host.Get<ScanHistoryService>().DeleteSessionAsync(scan.SessionId);

        Assert.Empty(await host.Get<IScanQueryService>().GetRootsAsync(scan.SessionId));
        Assert.Empty(await recommendations.GetBySessionAsync(scan.SessionId));
        Assert.Null(await host.Get<IScanSessionRepository>().GetAsync(scan.SessionId));

        var kept = await executions.GetAsync(execution.Id);
        Assert.NotNull(kept);
        Assert.Equal(2048, kept!.TotalBytesFreed);
    }
}
