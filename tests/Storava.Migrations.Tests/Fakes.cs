using Storava.Application.Abstractions;
using Storava.Application.Migration;
using Storava.Domain.Common;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Migrations.Tests;

/// <summary>
/// An in-memory stand-in for the disk. Every operation records what it was asked to do, so a test
/// can assert on the *order* of operations — which is the property that actually keeps user data
/// safe here, not the individual calls.
/// </summary>
internal sealed class FakeFileSystem : IFileSystemActions
{
    public Dictionary<string, DirectoryFacts> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ReparsePoints { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, long> FreeSpaceByRoot { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Operations { get; } = [];

    public List<string> Recycled { get; } = [];

    public bool FailCopy { get; set; }

    /// <summary>Simulates the user stopping a copy that has already written part of the tree.</summary>
    public bool CancelDuringCopy { get; set; }

    public bool FailRecycleFor(string path) => RecycleFailures.Contains(path);

    public HashSet<string> RecycleFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FailLink { get; set; }

    /// <summary>Lets a test simulate a copy that lands wrong without any real IO.</summary>
    public DirectoryFacts? CopyResultOverride { get; set; }

    public void AddDirectory(string path, long bytes, long files = 1, long links = 0) =>
        Directories[path] = new DirectoryFacts(bytes, files, 0, links);

    /// <summary>Files this fake disk holds, by path and size. A folder lives in Directories.</summary>
    public Dictionary<string, long> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, long bytes) => Files[path] = bytes;

    public bool DirectoryExists(string path) => Directories.ContainsKey(path);

    public bool Exists(string path) => Directories.ContainsKey(path) || Files.ContainsKey(path);

    public bool IsReparsePoint(string path) => ReparsePoints.Contains(path);

    public bool IsEmptyDirectory(string path) =>
        Directories.TryGetValue(path, out var facts) && facts.FileCount == 0 && facts.FolderCount == 0;

    public Result<long> GetAvailableFreeSpace(string path)
    {
        var root = RootOf(path);
        return FreeSpaceByRoot.TryGetValue(root, out long free)
            ? Result.Success(free)
            : Result.Success(long.MaxValue);
    }

    public Result<string> GetVolumeRoot(string path) => Result.Success(RootOf(path));

    public Task<Result<DirectoryFacts>> MeasureAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Directories.TryGetValue(path, out var facts))
            return Task.FromResult(Result.Success(facts));

        // One file counts as one file, mirroring what the real implementation reports, so a test
        // that verifies a copied file compares the same shape the production code compares.
        if (Files.TryGetValue(path, out long bytes))
            return Task.FromResult(Result.Success(new DirectoryFacts(bytes, 1, 0, 0)));

        return Task.FromResult(Result.Failure<DirectoryFacts>(ExecutionErrors.SourceMissing));
    }

    public Task<Result> CopyAsync(
        string sourcePath,
        string destinationPath,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Operations.Add($"copy:{sourcePath}->{destinationPath}");

        if (Files.TryGetValue(sourcePath, out long fileBytes))
        {
            if (FailCopy)
                return Task.FromResult(Result.Failure(ExecutionErrors.CopyFailed));

            Files[destinationPath] = CopyResultOverride?.Bytes ?? fileBytes;
            progress?.Report(new CopyProgress(fileBytes, fileBytes, destinationPath));
            return Task.FromResult(Result.Success());
        }

        if (CancelDuringCopy)
        {
            // Half a tree on disk, then the throw — the real shape of an interrupted copy.
            Directories[destinationPath] = new DirectoryFacts(Directories[sourcePath].Bytes / 2, 1, 0);
            throw new OperationCanceledException();
        }

        if (FailCopy)
            return Task.FromResult(Result.Failure(ExecutionErrors.CopyFailed));

        var landed = CopyResultOverride ?? Directories[sourcePath];
        Directories[destinationPath] = landed;
        progress?.Report(new CopyProgress(landed.Bytes, landed.Bytes, destinationPath));
        return Task.FromResult(Result.Success());
    }

    public Task<Result> MoveToRecycleBinAsync(string path, CancellationToken cancellationToken = default)
    {
        Operations.Add($"recycle:{path}");

        if (FailRecycleFor(path))
            return Task.FromResult(Result.Failure(ExecutionErrors.RecycleFailed));

        Directories.Remove(path);
        Files.Remove(path);
        Recycled.Add(path);
        return Task.FromResult(Result.Success());
    }

    public Result CreateLink(string linkPath, string targetPath, MigrationMethod method, bool isFolder = true)
    {
        Operations.Add($"link:{linkPath}->{targetPath}:{(isFolder ? "folder" : "file")}");
        return FailLink ? Result.Failure(ExecutionErrors.LinkFailed) : Result.Success();
    }

    private static string RootOf(string path) =>
        path.Length >= 2 && path[1] == ':' ? path[..3] : "\\\\";
}

internal sealed class FakeProtectedPaths : IProtectedPathService
{
    public HashSet<string> Roots { get; } = new(StringComparer.OrdinalIgnoreCase) { @"C:\Windows" };

    public IReadOnlyList<string> ProtectedRoots => Roots.ToList();

    public bool IsProtected(string path) => MatchingRoot(path) is not null;

    public string? MatchingRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return Roots.FirstOrDefault(root =>
            path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root + '\\', StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class InMemoryExecutionRepository : IPlanExecutionRepository
{
    private readonly Dictionary<string, PlanExecution> _executions = new(StringComparer.Ordinal);

    /// <summary>Every step write in order, so a test can prove the row was persisted before the IO.</summary>
    public List<(string StepId, ExecutionStatus Status)> StepWrites { get; } = [];

    public Task SaveAsync(PlanExecution execution, CancellationToken cancellationToken = default)
    {
        _executions[execution.Id] = execution;
        return Task.CompletedTask;
    }

    public Task SaveStepAsync(PlanExecutionStep step, CancellationToken cancellationToken = default)
    {
        StepWrites.Add((step.Id, step.Status));
        return Task.CompletedTask;
    }

    public Task<PlanExecution?> GetAsync(string executionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_executions.GetValueOrDefault(executionId));

    public Task<PlanExecution?> GetLatestForSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_executions.Values.LastOrDefault(e => e.SessionId == sessionId));

    public Task<IReadOnlyList<PlanExecution>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlanExecution>>(_executions.Values.Take(limit).ToList());
}
