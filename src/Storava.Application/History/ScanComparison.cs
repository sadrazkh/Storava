using Storava.Domain.Common;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Application.History;

/// <summary>A folder's size within one scan, keyed by path because ids differ between scans.</summary>
public sealed record FolderSize(string Path, string Name, long Size, int Depth, StorageCategory Category);

public enum FolderChangeKind
{
    /// <summary>Present in both scans, same size.</summary>
    Unchanged = 0,

    Grew = 1,
    Shrank = 2,

    /// <summary>Only in the newer scan.</summary>
    Added = 3,

    /// <summary>Only in the older scan.</summary>
    Removed = 4
}

/// <summary>What happened to one folder between two scans of the same root.</summary>
public sealed record FolderChange(
    string Path,
    string Name,
    long BaselineBytes,
    long CurrentBytes,
    FolderChangeKind Kind,
    int Depth,
    StorageCategory Category)
{
    public long Delta => CurrentBytes - BaselineBytes;

    /// <summary>
    /// Set when a folder above this one also changed. Such a row repeats space already reported by
    /// its ancestor, so it is marked rather than counted again — the same rule the storage plan
    /// applies to nested steps.
    /// </summary>
    public bool HasChangedAncestor { get; init; }
}

/// <summary>Category totals across the two scans.</summary>
public sealed record CategoryChange(StorageCategory Category, long BaselineBytes, long CurrentBytes)
{
    public long Delta => CurrentBytes - BaselineBytes;
}

/// <summary>
/// The difference between two scans of the same root, ordered by how much each folder moved.
/// </summary>
public sealed record ScanComparison
{
    public required ScanSession Baseline { get; init; }
    public required ScanSession Current { get; init; }

    public required IReadOnlyList<FolderChange> Changes { get; init; }
    public required IReadOnlyList<CategoryChange> CategoryChanges { get; init; }

    public long BaselineBytes => Baseline.TotalSize;
    public long CurrentBytes => Current.TotalSize;
    public long Delta => CurrentBytes - BaselineBytes;

    public TimeSpan Elapsed => Current.StartedAt - Baseline.StartedAt;

    /// <summary>Top-level movers only — nested repeats of an ancestor's change are left out.</summary>
    public IEnumerable<FolderChange> TopLevelChanges => Changes.Where(c => !c.HasChangedAncestor);

    public IEnumerable<FolderChange> Growth => TopLevelChanges.Where(c => c.Delta > 0);

    public IEnumerable<FolderChange> Shrinkage => TopLevelChanges.Where(c => c.Delta < 0);

    public bool HasChanges => Changes.Count > 0;
}

/// <summary>Why two scans could not be compared.</summary>
public static class ComparisonErrors
{
    public static readonly Error SameSession =
        new("compare.same_session", "Pick two different scans.");

    public static readonly Error DifferentRoots =
        new("compare.different_roots", "Those two scans covered different folders, so they cannot be compared.");

    public static readonly Error SessionMissing =
        new("compare.session_missing", "That scan is no longer stored.");
}
