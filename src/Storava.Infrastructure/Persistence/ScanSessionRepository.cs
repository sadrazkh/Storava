using Microsoft.Data.Sqlite;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Persistence;

public sealed class ScanSessionRepository : IScanSessionRepository
{
    private const string Columns =
        "Id, RootPath, Label, Mode, Status, StartedAt, CompletedAt, TotalSize, TotalFiles, TotalFolders, ErrorCount, " +
        "Origin, ImportedAt, SourceLabel, ResumeState";

    private readonly DatabaseGateway _db;

    public ScanSessionRepository(DatabaseGateway db) => _db = db;

    public Task SaveAsync(ScanSession session, CancellationToken cancellationToken = default) =>
        _db.RunAsync(async (connection, _) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO ScanSessions ({Columns})
                VALUES ($Id, $RootPath, $Label, $Mode, $Status, $StartedAt, $CompletedAt, $TotalSize, $TotalFiles,
                        $TotalFolders, $ErrorCount, $Origin, $ImportedAt, $SourceLabel, $ResumeState)
                ON CONFLICT(Id) DO UPDATE SET
                    RootPath=$RootPath, Label=$Label, Mode=$Mode, Status=$Status, StartedAt=$StartedAt,
                    CompletedAt=$CompletedAt, TotalSize=$TotalSize, TotalFiles=$TotalFiles,
                    TotalFolders=$TotalFolders, ErrorCount=$ErrorCount, Origin=$Origin,
                    ImportedAt=$ImportedAt, SourceLabel=$SourceLabel, ResumeState=$ResumeState;
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
            command.Parameters.AddWithValue("$Origin", (int)session.Origin);
            command.Parameters.AddWithValue("$ImportedAt", (object?)session.ImportedAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$SourceLabel", (object?)session.SourceLabel ?? DBNull.Value);
            command.Parameters.AddWithValue("$ResumeState", (object?)session.ResumeState ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task<ScanSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _db.RunAsync(async (connection, _) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM ScanSessions WHERE Id = $Id;";
            command.Parameters.AddWithValue("$Id", sessionId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
        }, cancellationToken);

    public Task<IReadOnlyList<ScanSession>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) =>
        _db.RunAsync<IReadOnlyList<ScanSession>>(async (connection, _) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM ScanSessions ORDER BY StartedAt DESC LIMIT $Limit;";
            command.Parameters.AddWithValue("$Limit", limit);

            var result = new List<ScanSession>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                result.Add(Map(reader));
            return result;
        }, cancellationToken);

    public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _db.RunAsync(async (connection, _) =>
        {
            await using var command = connection.CreateCommand();

            // Recommendations are derived from the items, so they have to go with them; leaving them
            // behind would let a deleted scan keep feeding advice into pages that read by session id.
            // The execution log is *not* touched here: it records real changes to the user's files and
            // outlives the scan that suggested them.
            command.CommandText = """
                DELETE FROM ScanItems       WHERE SessionId = $Id;
                DELETE FROM Recommendations WHERE SessionId = $Id;
                DELETE FROM ScanSessions    WHERE Id = $Id;
                """;
            command.Parameters.AddWithValue("$Id", sessionId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);


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
        ErrorCount = r.GetInt32(10),
        Origin = (ScanOrigin)r.GetInt32(11),
        ImportedAt = r.IsDBNull(12) ? null : r.GetFieldValue<DateTimeOffset>(12),
        SourceLabel = r.IsDBNull(13) ? null : r.GetString(13),
        ResumeState = r.IsDBNull(14) ? null : r.GetString(14)
    };
}
