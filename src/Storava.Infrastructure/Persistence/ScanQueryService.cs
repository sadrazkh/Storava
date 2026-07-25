using Microsoft.Data.Sqlite;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Persistence;

public sealed class ScanQueryService : IScanQueryService
{
    private const string Columns =
        "Id, ParentId, Path, Name, Extension, ItemType, Size, AllocatedSize, FileCount, FolderCount, " +
        "Depth, CreationTime, LastWriteTime, IsReparsePoint, IsProtected, IsHidden, IsSystem, " +
        "RiskLevel, Category, DetectedTechnology, KnownRuleId, Confidence, CanDelete, CanMove, CanRegenerate";

    private readonly StoravaDbOptions _options;
    private readonly IDatabaseInitializer _initializer;

    public ScanQueryService(StoravaDbOptions options, IDatabaseInitializer initializer)
    {
        _options = options;
        _initializer = initializer;
    }

    public Task<IReadOnlyList<ScanItemView>> GetRootsAsync(string sessionId, CancellationToken cancellationToken = default)
        => QueryAsync(
            $"SELECT {Columns} FROM ScanItems WHERE SessionId = $s AND ParentId IS NULL ORDER BY Size DESC;",
            cmd => cmd.Parameters.AddWithValue("$s", sessionId),
            cancellationToken);

    public Task<IReadOnlyList<ScanItemView>> GetChildrenAsync(string sessionId, string parentId, CancellationToken cancellationToken = default)
        => QueryAsync(
            $"SELECT {Columns} FROM ScanItems WHERE SessionId = $s AND ParentId = $p ORDER BY ItemType DESC, Size DESC;",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$s", sessionId);
                cmd.Parameters.AddWithValue("$p", parentId);
            },
            cancellationToken);

    public Task<IReadOnlyList<ScanItemView>> GetLargestAsync(string sessionId, int limit, bool foldersOnly, CancellationToken cancellationToken = default)
    {
        string filter = foldersOnly ? $" AND ItemType = {(int)ItemType.Folder}" : string.Empty;
        return QueryAsync(
            $"SELECT {Columns} FROM ScanItems WHERE SessionId = $s{filter} ORDER BY Size DESC LIMIT $n;",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$s", sessionId);
                cmd.Parameters.AddWithValue("$n", limit);
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<ScanItemView>> SearchAsync(string sessionId, string term, int limit, CancellationToken cancellationToken = default)
    {
        string escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return QueryAsync(
            $"SELECT {Columns} FROM ScanItems WHERE SessionId = $s AND Name LIKE $t ESCAPE '\\' ORDER BY Size DESC LIMIT $n;",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$s", sessionId);
                cmd.Parameters.AddWithValue("$t", $"%{escaped}%");
                cmd.Parameters.AddWithValue("$n", limit);
            },
            cancellationToken);
    }

    /// <summary>
    /// Candidates for recommendations: identified, actionable folders above a size threshold.
    /// Nested matches of the same rule are filtered out by the caller's ranking.
    /// </summary>
    public Task<IReadOnlyList<ScanItemView>> GetRecommendationCandidatesAsync(
        string sessionId, long minimumSize, int limit, CancellationToken cancellationToken = default)
        => QueryAsync(
            $"""
             SELECT {Columns} FROM ScanItems
             WHERE SessionId = $s
               AND KnownRuleId IS NOT NULL
               AND IsProtected = 0
               AND Size >= $min
               AND (CanDelete = 1 OR CanMove = 1)
             ORDER BY Size DESC LIMIT $n;
             """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("$s", sessionId);
                cmd.Parameters.AddWithValue("$min", minimumSize);
                cmd.Parameters.AddWithValue("$n", limit);
            },
            cancellationToken);

    /// <summary>Total size per category across the session, largest first.</summary>
    public async Task<IReadOnlyList<CategoryUsage>> GetCategoryUsageAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Only files carry non-overlapping bytes: summing folders as well would count twice.
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Category, SUM(Size) AS Total, COUNT(*) AS Items
            FROM ScanItems
            WHERE SessionId = $s AND ItemType = {(int)ItemType.File}
            GROUP BY Category
            ORDER BY Total DESC;
            """;
        command.Parameters.AddWithValue("$s", sessionId);

        var result = new List<CategoryUsage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new CategoryUsage(
                (StorageCategory)reader.GetInt32(0),
                reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                reader.GetInt32(2)));
        }

        return result;
    }

    /// <summary>Direct children of a folder for treemap rendering, largest first.</summary>
    public Task<IReadOnlyList<ScanItemView>> GetTreemapChildrenAsync(
        string sessionId, string? parentId, int limit, CancellationToken cancellationToken = default)
    {
        string parentFilter = parentId is null ? "ParentId IS NULL" : "ParentId = $p";
        return QueryAsync(
            $"SELECT {Columns} FROM ScanItems WHERE SessionId = $s AND {parentFilter} AND Size > 0 ORDER BY Size DESC LIMIT $n;",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$s", sessionId);
                if (parentId is not null)
                    cmd.Parameters.AddWithValue("$p", parentId);
                cmd.Parameters.AddWithValue("$n", limit);
            },
            cancellationToken);
    }

    public async Task<ScanItemView?> GetByIdAsync(string sessionId, string itemId, CancellationToken cancellationToken = default)
    {
        var items = await QueryAsync(
            $"SELECT {Columns} FROM ScanItems WHERE SessionId = $s AND Id = $i LIMIT 1;",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$s", sessionId);
                cmd.Parameters.AddWithValue("$i", itemId);
            },
            cancellationToken).ConfigureAwait(false);

        return items.Count > 0 ? items[0] : null;
    }

    private async Task<IReadOnlyList<ScanItemView>> QueryAsync(
        string sql, Action<SqliteCommand> bind, CancellationToken cancellationToken)
    {
        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        bind(command);

        var result = new List<ScanItemView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(Map(reader));
        return result;
    }

    private static ScanItemView Map(SqliteDataReader r) => new(
        Id: r.GetString(0),
        ParentId: r.IsDBNull(1) ? null : r.GetString(1),
        Path: r.GetString(2),
        Name: r.GetString(3),
        Extension: r.IsDBNull(4) ? null : r.GetString(4),
        ItemType: (ItemType)r.GetInt32(5),
        Size: r.GetInt64(6),
        AllocatedSize: r.GetInt64(7),
        FileCount: r.GetInt32(8),
        FolderCount: r.GetInt32(9),
        Depth: r.GetInt32(10),
        CreationTime: r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11),
        LastWriteTime: r.IsDBNull(12) ? null : r.GetFieldValue<DateTimeOffset>(12),
        IsReparsePoint: r.GetInt32(13) != 0,
        IsProtected: r.GetInt32(14) != 0,
        IsHidden: r.GetInt32(15) != 0,
        IsSystem: r.GetInt32(16) != 0,
        RiskLevel: (RiskLevel)r.GetInt32(17),
        Category: (StorageCategory)r.GetInt32(18),
        DetectedTechnology: r.IsDBNull(19) ? null : r.GetString(19),
        KnownRuleId: r.IsDBNull(20) ? null : r.GetString(20),
        Confidence: r.GetDouble(21),
        CanDelete: r.GetInt32(22) != 0,
        CanMove: r.GetInt32(23) != 0,
        CanRegenerate: r.GetInt32(24) != 0);
}
