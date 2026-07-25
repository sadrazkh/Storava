using Storava.Application.Scanning;

namespace Storava.Application.Abstractions;

/// <summary>Read-side queries over persisted scan items, backed by indexed SQLite lookups.</summary>
public interface IScanQueryService
{
    /// <summary>The root item(s) of a session (those without a parent).</summary>
    Task<IReadOnlyList<ScanItemView>> GetRootsAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Direct children of a folder, folders first then by size descending.</summary>
    Task<IReadOnlyList<ScanItemView>> GetChildrenAsync(string sessionId, string parentId, CancellationToken cancellationToken = default);

    /// <summary>The largest items in a session, optionally restricted to folders.</summary>
    Task<IReadOnlyList<ScanItemView>> GetLargestAsync(string sessionId, int limit, bool foldersOnly, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive name search, largest first.</summary>
    Task<IReadOnlyList<ScanItemView>> SearchAsync(string sessionId, string term, int limit, CancellationToken cancellationToken = default);

    /// <summary>Identified, actionable, unprotected items above a size threshold.</summary>
    Task<IReadOnlyList<ScanItemView>> GetRecommendationCandidatesAsync(
        string sessionId, long minimumSize, int limit, CancellationToken cancellationToken = default);

    /// <summary>Total bytes per category, largest first.</summary>
    Task<IReadOnlyList<CategoryUsage>> GetCategoryUsageAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Children for treemap rendering; pass null for the session roots.</summary>
    Task<IReadOnlyList<ScanItemView>> GetTreemapChildrenAsync(
        string sessionId, string? parentId, int limit, CancellationToken cancellationToken = default);

    Task<ScanItemView?> GetByIdAsync(string sessionId, string itemId, CancellationToken cancellationToken = default);
}
