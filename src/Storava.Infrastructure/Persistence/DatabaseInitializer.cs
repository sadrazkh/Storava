using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;

namespace Storava.Infrastructure.Persistence;

/// <summary>
/// Creates the full schema with CREATE TABLE IF NOT EXISTS so new tables can be added across
/// versions without EF migrations and without destroying existing data. Runs once per process.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS Settings (
            Key       TEXT NOT NULL PRIMARY KEY,
            Value     TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ScanSessions (
            Id           TEXT NOT NULL PRIMARY KEY,
            RootPath     TEXT NOT NULL,
            Label        TEXT NULL,
            Mode         INTEGER NOT NULL,
            Status       INTEGER NOT NULL,
            StartedAt    TEXT NOT NULL,
            CompletedAt  TEXT NULL,
            TotalSize    INTEGER NOT NULL,
            TotalFiles   INTEGER NOT NULL,
            TotalFolders INTEGER NOT NULL,
            ErrorCount   INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ScanItems (
            Id                 TEXT NOT NULL PRIMARY KEY,
            SessionId          TEXT NOT NULL,
            ParentId           TEXT NULL,
            Path               TEXT NOT NULL,
            SanitizedPath      TEXT NULL,
            Name               TEXT NOT NULL,
            Extension          TEXT NULL,
            ItemType           INTEGER NOT NULL,
            Size               INTEGER NOT NULL,
            AllocatedSize      INTEGER NOT NULL,
            FileCount          INTEGER NOT NULL,
            FolderCount        INTEGER NOT NULL,
            Depth              INTEGER NOT NULL,
            CreationTime       TEXT NULL,
            LastWriteTime      TEXT NULL,
            LastAccessTime     TEXT NULL,
            Attributes         INTEGER NOT NULL,
            IsHidden           INTEGER NOT NULL,
            IsSystem           INTEGER NOT NULL,
            IsReparsePoint     INTEGER NOT NULL,
            IsProtected        INTEGER NOT NULL,
            Category           INTEGER NOT NULL,
            DetectedTechnology TEXT NULL,
            KnownRuleId        TEXT NULL,
            RiskLevel          INTEGER NOT NULL,
            Confidence         REAL NOT NULL,
            CanDelete          INTEGER NOT NULL,
            CanMove            INTEGER NOT NULL,
            CanRegenerate      INTEGER NOT NULL,
            SuggestedAction    INTEGER NOT NULL,
            Reason             TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS Recommendations (
            Id                       TEXT NOT NULL PRIMARY KEY,
            SessionId                TEXT NOT NULL,
            ScanItemId               TEXT NOT NULL,
            Path                     TEXT NOT NULL,
            Title                    TEXT NOT NULL,
            Reason                   TEXT NOT NULL,
            SuggestedAction          INTEGER NOT NULL,
            RiskLevel                INTEGER NOT NULL,
            Category                 INTEGER NOT NULL,
            Technology               TEXT NULL,
            RuleId                   TEXT NULL,
            EstimatedSpace           INTEGER NOT NULL,
            Confidence               REAL NOT NULL,
            Score                    REAL NOT NULL,
            CanDelete                INTEGER NOT NULL,
            CanMove                  INTEGER NOT NULL,
            CanRegenerate            INTEGER NOT NULL,
            OfficialMigrationMethod  INTEGER NOT NULL,
            FallbackMigrationMethod  INTEGER NOT NULL,
            OfficialMigrationHint    TEXT NULL,
            Warning                  TEXT NULL,
            Source                   INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS StoragePlans (
            Id         TEXT NOT NULL PRIMARY KEY,
            SessionId  TEXT NOT NULL,
            Name       TEXT NULL,
            CreatedAt  TEXT NOT NULL,
            UpdatedAt  TEXT NOT NULL,
            GoalBytes  INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS StoragePlanEntries (
            Id               TEXT NOT NULL PRIMARY KEY,
            PlanId           TEXT NOT NULL,
            RecommendationId TEXT NOT NULL,
            ScanItemId       TEXT NOT NULL,
            Path             TEXT NOT NULL,
            Title            TEXT NOT NULL,
            Action           INTEGER NOT NULL,
            EstimatedSpace   INTEGER NOT NULL,
            RiskLevel        INTEGER NOT NULL,
            Category         INTEGER NOT NULL,
            Technology       TEXT NULL,
            Method           INTEGER NOT NULL,
            MethodHint       TEXT NULL,
            Warning          TEXT NULL,
            AddedAt          TEXT NOT NULL,
            SortOrder        INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Recommendations_Session ON Recommendations (SessionId, Score DESC);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_StoragePlans_Session ON StoragePlans (SessionId);
        CREATE INDEX IF NOT EXISTS IX_StoragePlanEntries_Plan   ON StoragePlanEntries (PlanId, SortOrder);

        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Parent ON ScanItems (SessionId, ParentId);
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Size   ON ScanItems (SessionId, Size DESC);
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Type   ON ScanItems (SessionId, ItemType);
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Name   ON ScanItems (SessionId, Name);
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Rule   ON ScanItems (SessionId, KnownRuleId);
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Cat    ON ScanItems (SessionId, Category);
        """;

    private readonly StoravaDbOptions _options;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public DatabaseInitializer(StoravaDbOptions options, ILogger<DatabaseInitializer> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=OFF;";
                await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = Schema;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
            _logger.LogInformation("Database schema ensured at {Path}.", _options.DatabasePath);
        }
        finally
        {
            _gate.Release();
        }
    }
}
