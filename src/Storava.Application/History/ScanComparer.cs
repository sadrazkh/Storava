using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Application.History;

/// <summary>
/// Turns two sets of folder sizes into a list of changes. Pure and side-effect free, so the rules
/// that decide what counts as "grew" are testable without a database.
/// </summary>
public static class ScanComparer
{
    /// <summary>
    /// Movement smaller than this is noise on a developer machine — a log file, a lock file — and
    /// listing it would bury the changes that matter.
    /// </summary>
    public const long DefaultThresholdBytes = 1024 * 1024;

    public static ScanComparison Compare(
        ScanSession baseline,
        ScanSession current,
        IReadOnlyList<FolderSize> baselineFolders,
        IReadOnlyList<FolderSize> currentFolders,
        IReadOnlyList<CategoryUsageSnapshot> baselineCategories,
        IReadOnlyList<CategoryUsageSnapshot> currentCategories,
        long thresholdBytes = DefaultThresholdBytes)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var before = baselineFolders.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var after = currentFolders.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);

        var changes = new List<FolderChange>();

        foreach (var (path, now) in after)
        {
            if (before.TryGetValue(path, out var then))
            {
                long delta = now.Size - then.Size;
                if (Math.Abs(delta) < thresholdBytes)
                    continue;

                changes.Add(new FolderChange(
                    path, now.Name, then.Size, now.Size,
                    delta > 0 ? FolderChangeKind.Grew : FolderChangeKind.Shrank,
                    now.Depth, now.Category));
            }
            else if (now.Size >= thresholdBytes)
            {
                changes.Add(new FolderChange(
                    path, now.Name, 0, now.Size, FolderChangeKind.Added, now.Depth, now.Category));
            }
        }

        foreach (var (path, then) in before)
        {
            if (after.ContainsKey(path) || then.Size < thresholdBytes)
                continue;

            changes.Add(new FolderChange(
                path, then.Name, then.Size, 0, FolderChangeKind.Removed, then.Depth, then.Category));
        }

        var flagged = MarkNested(changes);

        return new ScanComparison
        {
            Baseline = baseline,
            Current = current,
            // Biggest movers first, in either direction: a 5 GB drop is as interesting as a 5 GB rise.
            Changes = flagged.OrderByDescending(c => Math.Abs(c.Delta)).ToList(),
            CategoryChanges = CompareCategories(baselineCategories, currentCategories)
        };
    }

    /// <summary>
    /// Flags a change that sits under another change. Without this, one cache growing by 3 GB is
    /// reported once for itself and again for every folder above it, and the list reads as though
    /// several gigabytes appeared in several places.
    /// </summary>
    private static List<FolderChange> MarkNested(List<FolderChange> changes)
    {
        // Shortest path first, so an ancestor is always considered before anything beneath it.
        var byDepth = changes.OrderBy(c => c.Path.Length).ToList();
        var result = new List<FolderChange>(changes.Count);
        var ancestors = new List<string>();

        foreach (var change in byDepth)
        {
            bool nested = ancestors.Any(a => IsUnder(change.Path, a));
            result.Add(nested ? change with { HasChangedAncestor = true } : change);

            if (!nested)
                ancestors.Add(change.Path);
        }

        return result;
    }

    private static List<CategoryChange> CompareCategories(
        IReadOnlyList<CategoryUsageSnapshot> baseline,
        IReadOnlyList<CategoryUsageSnapshot> current)
    {
        var before = baseline.ToDictionary(c => c.Category, c => c.TotalSize);
        var after = current.ToDictionary(c => c.Category, c => c.TotalSize);

        return before.Keys
            .Union(after.Keys)
            .Select(category => new CategoryChange(
                category,
                before.GetValueOrDefault(category),
                after.GetValueOrDefault(category)))
            .Where(c => c.Delta != 0)
            .OrderByDescending(c => Math.Abs(c.Delta))
            .ToList();
    }

    private static bool IsUnder(string path, string ancestor)
    {
        if (path.Length <= ancestor.Length)
            return false;

        if (!path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase))
            return false;

        // "D:\data" must not count as an ancestor of "D:\database".
        return path[ancestor.Length] is '\\' or '/';
    }
}

/// <summary>Category totals for one scan, as read back from storage.</summary>
public sealed record CategoryUsageSnapshot(StorageCategory Category, long TotalSize);
