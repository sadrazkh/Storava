using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Infrastructure.Persistence;

namespace Storava.Infrastructure.Workspace;

/// <summary>
/// Reads complete <see cref="ScanItem"/> rows for archive export. Reading streams row by row so a
/// scan of any size can be written without being held in memory.
/// </summary>
internal sealed class ScanItemStore
{
    private const string AllColumns =
        "Id, SessionId, ParentId, Path, SanitizedPath, Name, Extension, ItemType, Size, AllocatedSize, " +
        "FileCount, FolderCount, Depth, CreationTime, LastWriteTime, LastAccessTime, Attributes, " +
        "IsHidden, IsSystem, IsReparsePoint, IsProtected, Category, DetectedTechnology, KnownRuleId, " +
        "RiskLevel, Confidence, CanDelete, CanMove, CanRegenerate, SuggestedAction, Reason";

    private readonly StoravaDbOptions _options;

    internal ScanItemStore(StoravaDbOptions options) => _options = options;

    internal async IAsyncEnumerable<ScanItem> StreamAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        // Ordered by depth so parents precede children, which lets an import insert rows in
        // arrival order without deferring or re-sorting anything.
        command.CommandText = $"SELECT {AllColumns} FROM ScanItems WHERE SessionId = $s ORDER BY Depth ASC;";
        command.Parameters.AddWithValue("$s", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            yield return Map(reader);
    }

    internal async Task<int> CountAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ScanItems WHERE SessionId = $s;";
        command.Parameters.AddWithValue("$s", sessionId);

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is long count ? (int)count : 0;
    }

    private static ScanItem Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        SessionId = r.GetString(1),
        ParentId = r.IsDBNull(2) ? null : r.GetString(2),
        Path = r.GetString(3),
        SanitizedPath = r.IsDBNull(4) ? null : r.GetString(4),
        Name = r.GetString(5),
        Extension = r.IsDBNull(6) ? null : r.GetString(6),
        ItemType = (ItemType)r.GetInt32(7),
        Size = r.GetInt64(8),
        AllocatedSize = r.GetInt64(9),
        FileCount = r.GetInt32(10),
        FolderCount = r.GetInt32(11),
        Depth = r.GetInt32(12),
        CreationTime = r.IsDBNull(13) ? null : r.GetFieldValue<DateTimeOffset>(13),
        LastWriteTime = r.IsDBNull(14) ? null : r.GetFieldValue<DateTimeOffset>(14),
        LastAccessTime = r.IsDBNull(15) ? null : r.GetFieldValue<DateTimeOffset>(15),
        Attributes = (System.IO.FileAttributes)r.GetInt32(16),
        IsHidden = r.GetInt32(17) != 0,
        IsSystem = r.GetInt32(18) != 0,
        IsReparsePoint = r.GetInt32(19) != 0,
        IsProtected = r.GetInt32(20) != 0,
        Category = (StorageCategory)r.GetInt32(21),
        DetectedTechnology = r.IsDBNull(22) ? null : r.GetString(22),
        KnownRuleId = r.IsDBNull(23) ? null : r.GetString(23),
        RiskLevel = (RiskLevel)r.GetInt32(24),
        Confidence = r.GetDouble(25),
        CanDelete = r.GetInt32(26) != 0,
        CanMove = r.GetInt32(27) != 0,
        CanRegenerate = r.GetInt32(28) != 0,
        SuggestedAction = (SuggestedAction)r.GetInt32(29),
        Reason = r.IsDBNull(30) ? null : r.GetString(30)
    };
}
