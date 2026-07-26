using Storava.Application.History;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Application.Tests;

/// <summary>
/// The comparison is what tells the user *why* their disk filled up since last time, so the rules
/// about what counts as a change — and what is merely the same change reported twice — are pinned
/// down here rather than left to the UI.
/// </summary>
public class ScanComparerTests
{
    private const long Mb = 1024 * 1024;

    [Fact]
    public void Compare_ReportsAFolderThatGrew()
    {
        var comparison = Compare(
            baseline: [Folder(@"D:\dev\node_modules", 100 * Mb)],
            current: [Folder(@"D:\dev\node_modules", 400 * Mb)]);

        var change = Assert.Single(comparison.Changes);
        Assert.Equal(FolderChangeKind.Grew, change.Kind);
        Assert.Equal(300 * Mb, change.Delta);
    }

    [Fact]
    public void Compare_ReportsAFolderThatShrank()
    {
        var comparison = Compare(
            baseline: [Folder(@"D:\dev\cache", 500 * Mb)],
            current: [Folder(@"D:\dev\cache", 100 * Mb)]);

        var change = Assert.Single(comparison.Changes);
        Assert.Equal(FolderChangeKind.Shrank, change.Kind);
        Assert.Equal(-400 * Mb, change.Delta);
    }

    [Fact]
    public void Compare_ReportsFoldersThatAppearedAndDisappeared()
    {
        var comparison = Compare(
            baseline: [Folder(@"D:\old", 50 * Mb)],
            current: [Folder(@"D:\new", 70 * Mb)]);

        Assert.Contains(comparison.Changes, c => c.Path == @"D:\new" && c.Kind == FolderChangeKind.Added);
        Assert.Contains(comparison.Changes, c => c.Path == @"D:\old" && c.Kind == FolderChangeKind.Removed);
    }

    [Fact]
    public void Compare_IgnoresMovementBelowTheThreshold()
    {
        // A few hundred kilobytes of log churn is not a finding.
        var comparison = Compare(
            baseline: [Folder(@"D:\dev\logs", 10 * Mb)],
            current: [Folder(@"D:\dev\logs", 10 * Mb + 400_000)]);

        Assert.Empty(comparison.Changes);
        Assert.False(comparison.HasChanges);
    }

    [Fact]
    public void Compare_MarksAChangeThatSitsInsideAnotherChange()
    {
        // One cache grew by 300 MB. Without the flag the list claims 600 MB appeared in two places.
        var comparison = Compare(
            baseline:
            [
                Folder(@"D:\dev", 500 * Mb),
                Folder(@"D:\dev\node_modules", 100 * Mb, depth: 2)
            ],
            current:
            [
                Folder(@"D:\dev", 800 * Mb),
                Folder(@"D:\dev\node_modules", 400 * Mb, depth: 2)
            ]);

        var parent = comparison.Changes.Single(c => c.Path == @"D:\dev");
        var child = comparison.Changes.Single(c => c.Path == @"D:\dev\node_modules");

        Assert.False(parent.HasChangedAncestor);
        Assert.True(child.HasChangedAncestor);
        Assert.Single(comparison.TopLevelChanges);
    }

    [Fact]
    public void Compare_TreatsASiblingWithASharedPrefixAsIndependent()
    {
        // "D:\database" merely starts with "D:\data".
        var comparison = Compare(
            baseline: [Folder(@"D:\data", 100 * Mb), Folder(@"D:\database", 100 * Mb)],
            current: [Folder(@"D:\data", 300 * Mb), Folder(@"D:\database", 300 * Mb)]);

        Assert.Equal(2, comparison.TopLevelChanges.Count());
    }

    [Fact]
    public void Compare_OrdersByHowFarEachFolderMovedInEitherDirection()
    {
        var comparison = Compare(
            baseline: [Folder(@"D:\a", 100 * Mb), Folder(@"D:\b", 900 * Mb), Folder(@"D:\c", 100 * Mb)],
            current: [Folder(@"D:\a", 200 * Mb), Folder(@"D:\b", 100 * Mb), Folder(@"D:\c", 400 * Mb)]);

        // A 800 MB drop outranks a 300 MB rise, which outranks a 100 MB rise.
        Assert.Equal([@"D:\b", @"D:\c", @"D:\a"], comparison.Changes.Select(c => c.Path));
    }

    [Fact]
    public void Compare_MatchesPathsRegardlessOfCase()
    {
        var comparison = Compare(
            baseline: [Folder(@"D:\Dev\Node_Modules", 100 * Mb)],
            current: [Folder(@"d:\dev\node_modules", 400 * Mb)]);

        var change = Assert.Single(comparison.Changes);
        // Matched as the same folder, not reported as one removed and one added.
        Assert.Equal(FolderChangeKind.Grew, change.Kind);
    }

    [Fact]
    public void Compare_SummarisesCategoryMovementAndDropsCategoriesThatDidNotMove()
    {
        var comparison = ScanComparer.Compare(
            Session("older", DateTimeOffset.Now.AddDays(-7)),
            Session("newer", DateTimeOffset.Now),
            [],
            [],
            [
                new CategoryUsageSnapshot(StorageCategory.PackageCaches, 100 * Mb),
                new CategoryUsageSnapshot(StorageCategory.Media, 500 * Mb)
            ],
            [
                new CategoryUsageSnapshot(StorageCategory.PackageCaches, 400 * Mb),
                new CategoryUsageSnapshot(StorageCategory.Media, 500 * Mb)
            ]);

        var change = Assert.Single(comparison.CategoryChanges);
        Assert.Equal(StorageCategory.PackageCaches, change.Category);
        Assert.Equal(300 * Mb, change.Delta);
    }

    [Fact]
    public void Compare_ReportsTheOverallDeltaFromTheSessionTotals()
    {
        var baseline = Session("older", DateTimeOffset.Now.AddDays(-30), totalSize: 200L * 1024 * Mb);
        var current = Session("newer", DateTimeOffset.Now, totalSize: 260L * 1024 * Mb);

        var comparison = ScanComparer.Compare(baseline, current, [], [], [], []);

        Assert.Equal(60L * 1024 * Mb, comparison.Delta);
        Assert.Equal(30, (int)comparison.Elapsed.TotalDays);
    }

    private static ScanComparison Compare(FolderSize[] baseline, FolderSize[] current) =>
        ScanComparer.Compare(
            Session("older", DateTimeOffset.Now.AddDays(-7)),
            Session("newer", DateTimeOffset.Now),
            baseline,
            current,
            [],
            []);

    private static FolderSize Folder(string path, long size, int depth = 1) =>
        new(path, path[(path.LastIndexOf('\\') + 1)..], size, depth, StorageCategory.PackageCaches);

    private static ScanSession Session(string id, DateTimeOffset startedAt, long totalSize = 0) => new()
    {
        Id = id,
        RootPath = @"D:\",
        Mode = ScanMode.Quick,
        Status = ScanStatus.Completed,
        StartedAt = startedAt,
        CompletedAt = startedAt.AddMinutes(5),
        TotalSize = totalSize
    };
}
