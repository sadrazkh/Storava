using Storava.Application.Abstractions;
using Storava.Application.History;
using Storava.Application.Scanning;
using Storava.Domain.Enums;

namespace Storava.AI.Tests;

/// <summary>An in-memory scan store so validation can be tested without a database.</summary>
internal sealed class FakeScanQueryService : IScanQueryService
{
    private readonly Dictionary<string, ScanItemView> _items = new(StringComparer.Ordinal);

    public IReadOnlyList<CategoryUsage> CategoryUsage { get; set; } = [];

    public ScanItemView Add(
        string id,
        string path,
        long size = 5L * 1024 * 1024 * 1024,
        bool canDelete = true,
        bool canMove = true,
        bool isProtected = false,
        RiskLevel risk = RiskLevel.Low,
        StorageCategory category = StorageCategory.PackageCaches,
        string? ruleId = "nuget.global-packages")
    {
        var view = new ScanItemView(
            Id: id,
            ParentId: null,
            Path: path,
            Name: System.IO.Path.GetFileName(path.TrimEnd('\\')),
            Extension: null,
            ItemType: ItemType.Folder,
            Size: size,
            AllocatedSize: size,
            FileCount: 10,
            FolderCount: 2,
            Depth: 3,
            CreationTime: DateTimeOffset.UtcNow.AddYears(-1),
            LastWriteTime: DateTimeOffset.UtcNow.AddDays(-100),
            IsReparsePoint: false,
            IsProtected: isProtected,
            IsHidden: false,
            IsSystem: false,
            RiskLevel: risk,
            Category: category,
            DetectedTechnology: "NuGet",
            KnownRuleId: ruleId,
            Confidence: 0.95,
            CanDelete: canDelete,
            CanMove: canMove,
            CanRegenerate: true);

        _items[id] = view;
        return view;
    }

    public Task<ScanItemView?> GetByIdAsync(string sessionId, string itemId, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.TryGetValue(itemId, out var item) ? item : null);

    public Task<IReadOnlyList<ScanItemView>> GetRecommendationCandidatesAsync(
        string sessionId, long minimumSize, int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScanItemView>>(
            _items.Values.Where(i => i.Size >= minimumSize && !i.IsProtected).Take(limit).ToArray());

    public Task<IReadOnlyList<ScanItemView>> GetLargestAsync(
        string sessionId, int limit, bool foldersOnly, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScanItemView>>(
            _items.Values.OrderByDescending(i => i.Size).Take(limit).ToArray());

    public Task<IReadOnlyList<CategoryUsage>> GetCategoryUsageAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(CategoryUsage);

    public Task<IReadOnlyList<ScanItemView>> GetRootsAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScanItemView>>([]);

    // History comparison is not part of what the AI layer sees, so this stays empty.
    public Task<IReadOnlyList<FolderSize>> GetFolderSizesAsync(
        string sessionId, int maxDepth, int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FolderSize>>([]);

    public Task<IReadOnlyList<ScanItemView>> GetChildrenAsync(string sessionId, string parentId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScanItemView>>([]);

    public Task<IReadOnlyList<ScanItemView>> SearchAsync(string sessionId, string term, int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScanItemView>>([]);

    public Task<IReadOnlyList<ScanItemView>> GetTreemapChildrenAsync(
        string sessionId, string? parentId, int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScanItemView>>([]);
}

/// <summary>Treats anything under C:\Windows or C:\Program Files as protected.</summary>
internal sealed class FakeProtectedPaths : IProtectedPathService
{
    public IReadOnlyList<string> ProtectedRoots { get; } = [@"C:\Windows", @"C:\Program Files"];

    public bool IsProtected(string path) => MatchingRoot(path) is not null;

    public string? MatchingRoot(string path) =>
        ProtectedRoots.FirstOrDefault(root =>
            path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));
}
