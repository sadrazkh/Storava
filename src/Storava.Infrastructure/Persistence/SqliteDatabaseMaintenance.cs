using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;

namespace Storava.Infrastructure.Persistence;

/// <inheritdoc />
public sealed class SqliteDatabaseMaintenance : IDatabaseMaintenance
{
    private readonly StoravaDbOptions _options;
    private readonly ILogger<SqliteDatabaseMaintenance> _logger;

    public SqliteDatabaseMaintenance(StoravaDbOptions options, ILogger<SqliteDatabaseMaintenance> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<long> CompactAsync(CancellationToken cancellationToken = default)
    {
        var before = SizeOnDisk();

        try
        {
            // Pooled connections hold the file open, and VACUUM needs the database to itself.
            SqliteConnection.ClearAllPools();

            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Fold the write-ahead log back in first. Without this the pages the deletes freed are
            // still sitting in the -wal file and VACUUM has less to reclaim than it appears.
            await Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken).ConfigureAwait(false);
            await Execute(connection, "VACUUM;", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SqliteException or IOException)
        {
            // Most often there is not enough free disk to write the rewritten copy — which is a
            // real possibility in an application whose users are short of space. The rows are gone
            // either way; only the file size is left behind.
            _logger.LogWarning(ex, "Could not compact the database. The freed space stays reserved for reuse.");
            return 0;
        }

        var after = SizeOnDisk();
        var reclaimed = Math.Max(0, before - after);

        if (reclaimed > 0)
            _logger.LogInformation("Compacted the database, returning {Bytes} bytes to the disk.", reclaimed);

        return reclaimed;
    }

    public long SizeOnDisk()
    {
        long total = 0;

        // The -wal and -shm side files are part of what the database occupies, so a measurement
        // that ignored them could report space "reclaimed" that the checkpoint merely moved.
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var info = new FileInfo(_options.DatabasePath + suffix);
            if (info.Exists)
                total += info.Length;
        }

        return total;
    }

    private static async Task Execute(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 0; // VACUUM on a multi-gigabyte database is measured in minutes.
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
