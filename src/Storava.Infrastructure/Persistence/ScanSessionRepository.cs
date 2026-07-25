using Microsoft.Data.Sqlite;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Persistence;

public sealed class ScanSessionRepository : IScanSessionRepository
{
    private const string Columns =
        "Id, RootPath, Label, Mode, Status, StartedAt, CompletedAt, TotalSize, TotalFiles, TotalFolders, ErrorCount";

    private readonly StoravaDbOptions _options;
    private readonly IDatabaseInitializer _initializer;

    public ScanSessionRepository(StoravaDbOptions options, IDatabaseInitializer initializer)
    {
        _options = options;
        _initializer = initializer;
    }

    public async Task SaveAsync(ScanSession session, CancellationToken cancellationToken = default)
    {
        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO ScanSessions ({Columns})
            VALUES ($Id, $RootPath, $Label, $Mode, $Status, $StartedAt, $CompletedAt, $TotalSize, $TotalFiles, $TotalFolders, $ErrorCount)
            ON CONFLICT(Id) DO UPDATE SET
                RootPath=$RootPath, Label=$Label, Mode=$Mode, Status=$Status, StartedAt=$StartedAt,
                CompletedAt=$CompletedAt, TotalSize=$TotalSize, TotalFiles=$TotalFiles,
                TotalFolders=$TotalFolders, ErrorCount=$ErrorCount;
            """;

        command.Parameters.AddWithValue("$Id", session.Id);
        command.Parameters.AddWithValue("$RootPath", session.RootPath);
        command.Parameters.AddWithValue("$Label", (object?)session.Label ?? DBNull.Value);
        command.Parameters.AddWithValue("$Mode", (int)session.Mode);
        command.Parameters.AddWithValue("$Status", (int)session.Status);
        command.Parameters.AddWithValue("$StartedAt", session.StartedAt);
        command.Parameters.AddWithValue("$CompletedAt", (object?)session.CompletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$TotalSize", session.TotalSize);
        command.Parameters.AddWithValue("$TotalFiles", session.TotalFiles);
        command.Parameters.AddWithValue("$TotalFolders", session.TotalFolders);
        command.Parameters.AddWithValue("$ErrorCount", session.ErrorCount);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScanSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM ScanSessions WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<ScanSession>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM ScanSessions ORDER BY StartedAt DESC LIMIT $Limit;";
        command.Parameters.AddWithValue("$Limit", limit);

        var result = new List<ScanSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(Map(reader));
        return result;
    }

    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ScanItems WHERE SessionId = $Id; DELETE FROM ScanSessions WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static ScanSession Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        RootPath = r.GetString(1),
        Label = r.IsDBNull(2) ? null : r.GetString(2),
        Mode = (ScanMode)r.GetInt32(3),
        Status = (ScanStatus)r.GetInt32(4),
        StartedAt = r.GetFieldValue<DateTimeOffset>(5),
        CompletedAt = r.IsDBNull(6) ? null : r.GetFieldValue<DateTimeOffset>(6),
        TotalSize = r.GetInt64(7),
        TotalFiles = r.GetInt32(8),
        TotalFolders = r.GetInt32(9),
        ErrorCount = r.GetInt32(10)
    };
}
