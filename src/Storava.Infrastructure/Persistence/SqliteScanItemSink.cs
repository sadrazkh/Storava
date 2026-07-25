using Microsoft.Data.Sqlite;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;

namespace Storava.Infrastructure.Persistence;

/// <summary>
/// Batched writer for scan items. Uses a single prepared INSERT and commits every
/// <see cref="BatchSize"/> rows, so large trees are persisted with steady, bounded memory.
/// </summary>
public sealed class SqliteScanItemSink : IScanItemSink
{
    private const int BatchSize = 5000;

    private readonly string _sessionId;
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _command;
    private readonly SqliteParameter[] _p;
    private SqliteTransaction? _transaction;
    private int _pending;
    private bool _disposed;

    public SqliteScanItemSink(string sessionId, string connectionString)
    {
        _sessionId = sessionId;
        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;";
            pragma.ExecuteNonQuery();
        }

        _command = _connection.CreateCommand();
        _command.CommandText = """
            INSERT INTO ScanItems
                (Id, SessionId, ParentId, Path, SanitizedPath, Name, Extension, ItemType, Size, AllocatedSize,
                 FileCount, FolderCount, Depth, CreationTime, LastWriteTime, LastAccessTime, Attributes,
                 IsHidden, IsSystem, IsReparsePoint, IsProtected, Category, DetectedTechnology, KnownRuleId,
                 RiskLevel, Confidence, CanDelete, CanMove, CanRegenerate, SuggestedAction, Reason)
            VALUES
                ($Id, $SessionId, $ParentId, $Path, $SanitizedPath, $Name, $Extension, $ItemType, $Size, $AllocatedSize,
                 $FileCount, $FolderCount, $Depth, $CreationTime, $LastWriteTime, $LastAccessTime, $Attributes,
                 $IsHidden, $IsSystem, $IsReparsePoint, $IsProtected, $Category, $DetectedTechnology, $KnownRuleId,
                 $RiskLevel, $Confidence, $CanDelete, $CanMove, $CanRegenerate, $SuggestedAction, $Reason);
            """;

        _p = new SqliteParameter[31];
        string[] names =
        [
            "$Id", "$SessionId", "$ParentId", "$Path", "$SanitizedPath", "$Name", "$Extension", "$ItemType", "$Size",
            "$AllocatedSize", "$FileCount", "$FolderCount", "$Depth", "$CreationTime", "$LastWriteTime", "$LastAccessTime",
            "$Attributes", "$IsHidden", "$IsSystem", "$IsReparsePoint", "$IsProtected", "$Category", "$DetectedTechnology",
            "$KnownRuleId", "$RiskLevel", "$Confidence", "$CanDelete", "$CanMove", "$CanRegenerate", "$SuggestedAction", "$Reason"
        ];
        for (int i = 0; i < names.Length; i++)
        {
            _p[i] = _command.CreateParameter();
            _p[i].ParameterName = names[i];
            _command.Parameters.Add(_p[i]);
        }

        _transaction = _connection.BeginTransaction();
        _command.Transaction = _transaction;
        _command.Prepare();
    }

    public ValueTask AddAsync(ScanItem item, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _p[0].Value = item.Id;
        _p[1].Value = _sessionId;
        _p[2].Value = (object?)item.ParentId ?? DBNull.Value;
        _p[3].Value = item.Path;
        _p[4].Value = (object?)item.SanitizedPath ?? DBNull.Value;
        _p[5].Value = item.Name;
        _p[6].Value = (object?)item.Extension ?? DBNull.Value;
        _p[7].Value = (int)item.ItemType;
        _p[8].Value = item.Size;
        _p[9].Value = item.AllocatedSize;
        _p[10].Value = item.FileCount;
        _p[11].Value = item.FolderCount;
        _p[12].Value = item.Depth;
        _p[13].Value = (object?)item.CreationTime ?? DBNull.Value;
        _p[14].Value = (object?)item.LastWriteTime ?? DBNull.Value;
        _p[15].Value = (object?)item.LastAccessTime ?? DBNull.Value;
        _p[16].Value = (int)item.Attributes;
        _p[17].Value = item.IsHidden ? 1 : 0;
        _p[18].Value = item.IsSystem ? 1 : 0;
        _p[19].Value = item.IsReparsePoint ? 1 : 0;
        _p[20].Value = item.IsProtected ? 1 : 0;
        _p[21].Value = (int)item.Category;
        _p[22].Value = (object?)item.DetectedTechnology ?? DBNull.Value;
        _p[23].Value = (object?)item.KnownRuleId ?? DBNull.Value;
        _p[24].Value = (int)item.RiskLevel;
        _p[25].Value = item.Confidence;
        _p[26].Value = item.CanDelete ? 1 : 0;
        _p[27].Value = item.CanMove ? 1 : 0;
        _p[28].Value = item.CanRegenerate ? 1 : 0;
        _p[29].Value = (int)item.SuggestedAction;
        _p[30].Value = (object?)item.Reason ?? DBNull.Value;

        _command.ExecuteNonQuery();

        if (++_pending >= BatchSize)
        {
            _transaction!.Commit();
            _transaction.Dispose();
            _transaction = _connection.BeginTransaction();
            _command.Transaction = _transaction;
            _pending = 0;
        }

        return ValueTask.CompletedTask;
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Task.CompletedTask;

        _transaction?.Commit();
        _transaction?.Dispose();
        _transaction = null;
        _pending = 0;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_transaction is not null)
        {
            try { _transaction.Rollback(); } catch { /* already committed */ }
            _transaction.Dispose();
        }

        _command.Dispose();
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
