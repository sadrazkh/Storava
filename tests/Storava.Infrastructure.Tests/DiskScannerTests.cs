using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Application.Services;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Tests;

public class DiskScannerTests
{
    [Fact]
    public async Task Scan_AggregatesSizesAndCounts()
    {
        using var tree = new TestTree();
        tree.AddFile("a.bin", 1000);
        tree.AddFile("sub/b.bin", 2000);
        tree.AddFile("sub/deep/c.bin", 3000);

        using var host = new TestHost();
        var result = await RunScanAsync(host, tree.Root);

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(3, result.TotalFolders); // root + sub + deep
        Assert.Equal(6000, result.TotalSize);
        Assert.Equal(0, result.ErrorCount);

        // The root row must carry the full recursive aggregation.
        var query = host.Get<IScanQueryService>();
        var roots = await query.GetRootsAsync(result.SessionId);
        var root = Assert.Single(roots);
        Assert.Equal(6000, root.Size);
        Assert.Equal(3, root.FileCount);
        Assert.Equal(2, root.FolderCount);
        Assert.Equal(ItemType.Folder, root.ItemType);
    }

    [Fact]
    public async Task Scan_PersistsChildrenUnderCorrectParent()
    {
        using var tree = new TestTree();
        tree.AddFile("top.bin", 500);
        tree.AddFile("sub/inner.bin", 700);

        using var host = new TestHost();
        var result = await RunScanAsync(host, tree.Root);

        var query = host.Get<IScanQueryService>();
        var root = (await query.GetRootsAsync(result.SessionId)).Single();
        var children = await query.GetChildrenAsync(result.SessionId, root.Id);

        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Name == "top.bin" && c.ItemType == ItemType.File && c.Size == 500);

        var sub = Assert.Single(children, c => c.Name == "sub");
        Assert.Equal(ItemType.Folder, sub.ItemType);
        Assert.Equal(700, sub.Size);

        var subChildren = await query.GetChildrenAsync(result.SessionId, sub.Id);
        Assert.Single(subChildren);
        Assert.Equal("inner.bin", subChildren[0].Name);
    }

    [Fact]
    public async Task Scan_RespectsExcludedExtensions()
    {
        using var tree = new TestTree();
        tree.AddFile("keep.bin", 100);
        tree.AddFile("skip.tmp", 900);

        using var host = new TestHost();
        var result = await RunScanAsync(host, tree.Root, request => request with { ExcludedExtensions = [".tmp"] });

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(100, result.TotalSize);
    }

    [Fact]
    public async Task Scan_RespectsExcludedPaths()
    {
        using var tree = new TestTree();
        tree.AddFile("keep/a.bin", 100);
        var skipped = tree.AddDirectory("skipme");
        tree.AddFile("skipme/big.bin", 5000);

        using var host = new TestHost();
        var result = await RunScanAsync(host, tree.Root, r => r with { ExcludedPaths = [skipped] });

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(100, result.TotalSize);
    }

    [Fact]
    public async Task Scan_EmptyFolder_YieldsSingleFolderRow()
    {
        using var tree = new TestTree();

        using var host = new TestHost();
        var result = await RunScanAsync(host, tree.Root);

        Assert.Equal(0, result.TotalFiles);
        Assert.Equal(1, result.TotalFolders);
        Assert.Equal(0, result.TotalSize);
    }

    [Fact]
    public async Task Scan_Cancelled_ReportsCancelledStatus()
    {
        using var tree = new TestTree();
        for (int i = 0; i < 50; i++)
            tree.AddFile($"f{i}.bin", 10);

        using var host = new TestHost();
        var coordinator = host.Get<ScanCoordinator>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await coordinator.RunAsync(
            new ScanRequest { RootPath = tree.Root },
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            cts.Token);

        Assert.Equal(ScanStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Scan_MissingRoot_ReportsFailed()
    {
        using var host = new TestHost();
        var coordinator = host.Get<ScanCoordinator>();

        var result = await coordinator.RunAsync(
            new ScanRequest { RootPath = Path.Combine(Path.GetTempPath(), "storava-does-not-exist-" + Guid.NewGuid().ToString("N")) },
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            CancellationToken.None);

        Assert.Equal(ScanStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Scan_ReportsProgress()
    {
        using var tree = new TestTree();
        for (int i = 0; i < 20; i++)
            tree.AddFile($"sub{i}/f.bin", 100);

        using var host = new TestHost();
        var coordinator = host.Get<ScanCoordinator>();
        var reports = new List<ScanProgress>();

        await coordinator.RunAsync(
            new ScanRequest { RootPath = tree.Root },
            new Progress<ScanProgress>(reports.Add),
            new PauseTokenSource().Token,
            CancellationToken.None);

        // Progress is throttled, but the final forced report must always arrive.
        Assert.NotEmpty(reports);
        Assert.Equal(20, reports[^1].FilesScanned);
    }

    [Fact]
    public void ProtectedPaths_CoverWindowsSystemLocations()
    {
        using var host = new TestHost();
        var protectedPaths = host.Get<IProtectedPathService>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.True(protectedPaths.IsProtected(windows));
        Assert.True(protectedPaths.IsProtected(Path.Combine(windows, "System32")));
        Assert.False(protectedPaths.IsProtected(Path.GetTempPath()));
    }

    [Fact]
    public async Task Scan_LargestQuery_OrdersBySizeDescending()
    {
        using var tree = new TestTree();
        tree.AddFile("small.bin", 100);
        tree.AddFile("big.bin", 9000);
        tree.AddFile("mid.bin", 4000);

        using var host = new TestHost();
        var result = await RunScanAsync(host, tree.Root);

        var query = host.Get<IScanQueryService>();
        var files = (await query.GetLargestAsync(result.SessionId, 10, foldersOnly: false))
            .Where(i => i.ItemType == ItemType.File)
            .ToList();

        Assert.Equal(["big.bin", "mid.bin", "small.bin"], files.Select(f => f.Name));
    }

    [Fact]
    public async Task Search_FindsItemsByPartialName()
    {
        using var tree = new TestTree();
        tree.AddFile("report-final.bin", 100);
        tree.AddFile("other.bin", 100);

        using var host = new TestHost();
        var result = await RunScanAsync(host, tree.Root);

        var query = host.Get<IScanQueryService>();
        var matches = await query.SearchAsync(result.SessionId, "report", 50);

        Assert.Single(matches);
        Assert.Equal("report-final.bin", matches[0].Name);
    }

    [Fact]
    public async Task Search_TreatsWildcardCharactersLiterally()
    {
        using var tree = new TestTree();
        tree.AddFile("plain.bin", 100);

        using var host = new TestHost();
        var result = await RunScanAsync(host, tree.Root);

        var query = host.Get<IScanQueryService>();
        var matches = await query.SearchAsync(result.SessionId, "%", 50);

        Assert.Empty(matches);
    }

    private static async Task<ScanResult> RunScanAsync(
        TestHost host, string root, Func<ScanRequest, ScanRequest>? customize = null)
    {
        var coordinator = host.Get<ScanCoordinator>();
        var request = new ScanRequest { RootPath = root };
        if (customize is not null)
            request = customize(request);

        return await coordinator.RunAsync(
            request,
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            CancellationToken.None);
    }
}
