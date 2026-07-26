using Microsoft.Extensions.Logging.Abstractions;
using Storava.Application.Migration;
using Storava.Domain.Enums;
using Storava.Platform.Storage;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// Exercises the real Win32 calls against a throwaway tree. The service-level tests run against a
/// fake disk, so this is the only place that proves <c>SHFileOperation</c> actually recycles, that
/// a junction is really created, and that reparse points are stepped over rather than followed —
/// the three behaviours the safety argument rests on.
/// </summary>
public class WindowsFileActionsTests : IDisposable
{
    private readonly WindowsFileActions _actions = new(NullLogger<WindowsFileActions>.Instance);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"storava-fs-{Guid.NewGuid():N}");

    public WindowsFileActionsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Measure_CountsFilesAndBytesAcrossTheTree()
    {
        var source = MakeTree("source", ("a.bin", 1024), ("nested/b.bin", 2048));

        var result = await _actions.MeasureAsync(source);

        Assert.True(result.IsSuccess);
        Assert.Equal(3072, result.Value.Bytes);
        Assert.Equal(2, result.Value.FileCount);
        Assert.Equal(1, result.Value.FolderCount);
    }

    [Fact]
    public async Task Measure_CountsAJunctionWithoutWalkingIntoIt()
    {
        var target = MakeTree("target", ("big.bin", 4096));
        var source = MakeTree("source", ("a.bin", 512));
        var link = Path.Combine(source, "link");

        Assert.True(_actions.CreateDirectoryLink(link, target, MigrationMethod.Junction).IsSuccess);

        var result = await _actions.MeasureAsync(source);

        // If the link were followed, this would report 4608 bytes of someone else's data.
        Assert.Equal(512, result.Value.Bytes);
        Assert.Equal(1, result.Value.FileCount);
        Assert.Equal(1, result.Value.LinkCount);
    }

    [Fact]
    public void CreateDirectoryLink_MakesARealJunctionThatResolves()
    {
        var target = MakeTree("target", ("payload.bin", 64));
        var link = Path.Combine(_root, "junction");

        var result = _actions.CreateDirectoryLink(link, target, MigrationMethod.Junction);

        Assert.True(result.IsSuccess);
        Assert.True(_actions.IsReparsePoint(link));
        // The point of the junction: the old path keeps working for whatever hard-codes it.
        Assert.True(File.Exists(Path.Combine(link, "payload.bin")));
    }

    [Fact]
    public async Task CopyDirectory_ReproducesTheTreeExactly()
    {
        var source = MakeTree("source", ("a.bin", 1000), ("nested/b.bin", 2000), ("nested/deep/c.bin", 3000));
        var destination = Path.Combine(_root, "copied");

        var copied = await _actions.CopyDirectoryAsync(source, destination);
        Assert.True(copied.IsSuccess);

        var sourceFacts = (await _actions.MeasureAsync(source)).Value;
        var destinationFacts = (await _actions.MeasureAsync(destination)).Value;

        // This is the comparison the executor uses to decide a move is safe to finish.
        Assert.True(destinationFacts.Matches(sourceFacts));
        Assert.Equal(3000, new FileInfo(Path.Combine(destination, "nested", "deep", "c.bin")).Length);
    }

    [Fact]
    public async Task CopyDirectory_ReportsProgressThatEndsAtTheTotal()
    {
        var source = MakeTree("source", ("a.bin", 5000));
        var destination = Path.Combine(_root, "copied");

        var reports = new List<CopyProgress>();
        await _actions.CopyDirectoryAsync(source, destination, new Progress<CopyProgress>(reports.Add));

        // Progress is throttled, so only the final report is guaranteed — and it must be honest.
        await WaitForAsync(() => reports.Count > 0);
        Assert.Equal(5000, reports[^1].TotalBytes);
    }

    [Fact]
    public async Task MoveToRecycleBin_RemovesTheFolderFromWhereItWas()
    {
        var doomed = MakeTree("recyclable", ("a.bin", 16));

        var result = await _actions.MoveToRecycleBinAsync(doomed);

        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(doomed));
    }

    [Fact]
    public async Task MoveToRecycleBin_FailsRatherThanClaimingSuccessForAMissingFolder()
    {
        var result = await _actions.MoveToRecycleBinAsync(Path.Combine(_root, "never-existed"));
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void GetVolumeRoot_DistinguishesDrives()
    {
        var root = _actions.GetVolumeRoot(_root);
        Assert.True(root.IsSuccess);
        Assert.Equal(Path.GetPathRoot(_root), root.Value);
    }

    [Fact]
    public void IsEmptyDirectory_SeparatesAnEmptyFolderFromOneWithContent()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        var full = MakeTree("full", ("a.bin", 1));

        Assert.True(_actions.IsEmptyDirectory(empty));
        Assert.False(_actions.IsEmptyDirectory(full));
    }

    private string MakeTree(string name, params (string RelativePath, int Size)[] files)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);

        foreach (var (relativePath, size) in files)
        {
            var path = Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[size]);
        }

        return folder;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 20 && !condition(); i++)
            await Task.Delay(25);
    }

    public void Dispose()
    {
        try
        {
            // Junctions are removed as links; Directory.Delete never follows them into the target.
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
