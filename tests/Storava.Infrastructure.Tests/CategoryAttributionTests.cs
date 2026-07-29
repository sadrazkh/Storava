using System.Diagnostics;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// Attributing scanned bytes to the outermost classified folder.
/// <para>
/// The rule is that a folder inside an already counted folder must not be counted again, or the
/// chart adds up to more than the disk holds. Checking it used to mean comparing every classified
/// row against every folder claimed so far: on one real scan of C:\ that was 843,228 rows against
/// a list reaching 114,203 folders, and the Analysis page stopped responding rather than being
/// merely slow.
/// </para>
/// <para>
/// These cover both halves: that the answer is still right, and that it is still reached without
/// that quadratic scan.
/// </para>
/// </summary>
public class CategoryAttributionTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<string> SeedAsync(IEnumerable<ScanItem> items)
    {
        var sessions = _host.Get<IScanSessionRepository>();
        var session = new ScanSession
        {
            Id = Guid.NewGuid().ToString("n"),
            RootPath = @"C:\",
            Mode = ScanMode.Quick,
            Status = ScanStatus.Completed,
            StartedAt = DateTimeOffset.Now
        };

        await sessions.SaveAsync(session);

        var sink = _host.Get<IScanItemSinkFactory>().Create(session.Id);
        foreach (var item in items)
            await sink.AddAsync(item);
        await sink.CompleteAsync();

        return session.Id;
    }

    private static ScanItem Item(string path, long size, StorageCategory category, ItemType type, int depth) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Path = path,
        Name = System.IO.Path.GetFileName(path.TrimEnd('\\')),
        ItemType = type,
        Size = size,
        Depth = depth,
        Category = category
    };

    /// <summary>
    /// A file inside a classified folder is already counted by that folder. Counting it again is
    /// what would make the chart claim more than the drive holds.
    /// </summary>
    [Fact]
    public async Task ContentsOfAClassifiedFolderAreNotCountedTwice()
    {
        var sessionId = await SeedAsync(
        [
            Item(@"C:\proj\node_modules", 1000, StorageCategory.PackageCaches, ItemType.Folder, 2),
            Item(@"C:\proj\node_modules\a.js", 400, StorageCategory.PackageCaches, ItemType.File, 3),
            Item(@"C:\proj\node_modules\deep\b.js", 600, StorageCategory.PackageCaches, ItemType.File, 4),
        ]);

        var usage = await _host.Get<IScanQueryService>().GetCategoryUsageAsync(sessionId);
        var caches = usage.Single(u => u.Category == StorageCategory.PackageCaches);

        Assert.Equal(1000, caches.TotalSize);
        Assert.Equal(1, caches.ItemCount);
    }

    /// <summary>Siblings are not inside one another, however similar their names look.</summary>
    [Fact]
    public async Task AFolderWhoseNameIsAPrefixOfAnotherIsNotTreatedAsItsParent()
    {
        var sessionId = await SeedAsync(
        [
            Item(@"C:\data", 100, StorageCategory.Logs, ItemType.Folder, 1),
            Item(@"C:\database", 200, StorageCategory.Logs, ItemType.Folder, 1),
        ]);

        var usage = await _host.Get<IScanQueryService>().GetCategoryUsageAsync(sessionId);
        var logs = usage.Single(u => u.Category == StorageCategory.Logs);

        // "C:\database" starts with "C:\data" as a string but is not inside it.
        Assert.Equal(300, logs.TotalSize);
        Assert.Equal(2, logs.ItemCount);
    }

    [Fact]
    public async Task SeparateBranchesAreBothCounted()
    {
        var sessionId = await SeedAsync(
        [
            Item(@"C:\one\cache", 100, StorageCategory.PackageCaches, ItemType.Folder, 2),
            Item(@"C:\two\cache", 200, StorageCategory.PackageCaches, ItemType.Folder, 2),
            Item(@"C:\one\cache\x", 50, StorageCategory.PackageCaches, ItemType.File, 3),
        ]);

        var usage = await _host.Get<IScanQueryService>().GetCategoryUsageAsync(sessionId);
        var caches = usage.Single(u => u.Category == StorageCategory.PackageCaches);

        Assert.Equal(300, caches.TotalSize);
        Assert.Equal(2, caches.ItemCount);
    }

    /// <summary>An item directly under the drive root is not inside anything.</summary>
    [Fact]
    public async Task AnItemAtTheDriveRootIsCounted()
    {
        var sessionId = await SeedAsync(
        [
            Item(@"C:\pagefile.sys", 8000, StorageCategory.WindowsSystem, ItemType.File, 1),
        ]);

        var usage = await _host.Get<IScanQueryService>().GetCategoryUsageAsync(sessionId);

        Assert.Equal(8000, usage.Single(u => u.Category == StorageCategory.WindowsSystem).TotalSize);
    }

    /// <summary>
    /// The shape of the work, not a wall-clock promise. A tree this size took minutes under the
    /// old scan and is bounded by the depth of a path under this one; the ceiling is loose enough
    /// to survive a slow machine and tight enough that the quadratic version could never pass it.
    /// </summary>
    [Fact]
    public async Task AttributionStaysQuickOnALargeTree()
    {
        var items = new List<ScanItem>();

        // 400 claimed folders with 100 files each: 40,000 rows the old scan would have compared
        // against a list growing to 400, and this one resolves by ancestor lookup.
        for (int folder = 0; folder < 400; folder++)
        {
            items.Add(Item($@"C:\proj{folder}\node_modules", 1000, StorageCategory.PackageCaches, ItemType.Folder, 2));
            for (int file = 0; file < 100; file++)
            {
                items.Add(Item(
                    $@"C:\proj{folder}\node_modules\pkg{file}\index.js",
                    10, StorageCategory.PackageCaches, ItemType.File, 4));
            }
        }

        var sessionId = await SeedAsync(items);

        var stopwatch = Stopwatch.StartNew();
        var usage = await _host.Get<IScanQueryService>().GetCategoryUsageAsync(sessionId);
        stopwatch.Stop();

        var caches = usage.Single(u => u.Category == StorageCategory.PackageCaches);
        Assert.Equal(400 * 1000, caches.TotalSize);
        Assert.Equal(400, caches.ItemCount);

        Assert.True(
            stopwatch.ElapsedMilliseconds < 5000,
            $"Attribution took {stopwatch.ElapsedMilliseconds} ms, which suggests the per-row scan is back.");
    }
}
