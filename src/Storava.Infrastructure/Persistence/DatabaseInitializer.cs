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
            -- Empty for a step the user picked out of the scan themselves, which has no
            -- recommendation behind it. Left NOT NULL rather than relaxed, because
            -- CREATE TABLE IF NOT EXISTS cannot relax a column on a database that already
            -- exists — the constraint would differ between an upgraded install and a fresh
            -- one, and code would have to cope with both shapes forever. HasNoRule is the
            -- flag that actually answers the question.
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

        CREATE TABLE IF NOT EXISTS PlanExecutions (
            Id          TEXT NOT NULL PRIMARY KEY,
            PlanId      TEXT NOT NULL,
            SessionId   TEXT NOT NULL,
            StartedAt   TEXT NOT NULL,
            CompletedAt TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS PlanExecutionSteps (
            Id              TEXT NOT NULL PRIMARY KEY,
            ExecutionId     TEXT NOT NULL,
            PlanEntryId     TEXT NOT NULL,
            ScanItemId      TEXT NOT NULL,
            SourcePath      TEXT NOT NULL,
            Title           TEXT NOT NULL,
            Action          INTEGER NOT NULL,
            Method          INTEGER NOT NULL,
            SortOrder       INTEGER NOT NULL,
            DestinationPath TEXT NULL,
            Status          INTEGER NOT NULL,
            MeasuredBytes   INTEGER NOT NULL,
            BytesFreed      INTEGER NOT NULL,
            StartedAt       TEXT NULL,
            CompletedAt     TEXT NULL,
            RecycledPath    TEXT NULL,
            LinkPath        TEXT NULL,
            ErrorCode       TEXT NULL,
            ErrorMessage    TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_PlanExecutions_Session     ON PlanExecutions (SessionId, StartedAt DESC);
        CREATE INDEX IF NOT EXISTS IX_PlanExecutionSteps_Run     ON PlanExecutionSteps (ExecutionId, SortOrder);

        CREATE INDEX IF NOT EXISTS IX_Recommendations_Session ON Recommendations (SessionId, Score DESC);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_StoragePlans_Session ON StoragePlans (SessionId);
        CREATE INDEX IF NOT EXISTS IX_StoragePlanEntries_Plan   ON StoragePlanEntries (PlanId, SortOrder);

        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Parent ON ScanItems (SessionId, ParentId);
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Size   ON ScanItems (SessionId, Size DESC);
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Type   ON ScanItems (SessionId, ItemType);
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Name   ON ScanItems (SessionId, Name);
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Rule   ON ScanItems (SessionId, KnownRuleId);
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Cat    ON ScanItems (SessionId, Category);

        -- Resuming a scan looks items up by exact path to skip what is already recorded.
        CREATE INDEX IF NOT EXISTS IX_ScanItems_Session_Path   ON ScanItems (SessionId, Path);
        """;

    /// <summary>
    /// Columns introduced after the first release. CREATE TABLE IF NOT EXISTS cannot add a column
    /// to a table that already exists, so these are applied separately and only when missing,
    /// which keeps existing databases upgradeable without losing their scans.
    /// </summary>
    private static readonly (string Table, string Column, string Definition)[] AddedColumns =
    [
        // Where a session came from: scanned on this machine, or imported from a .storava file.
        ("ScanSessions", "Origin", "INTEGER NOT NULL DEFAULT 0"),
        ("ScanSessions", "ImportedAt", "TEXT NULL"),
        ("ScanSessions", "SourceLabel", "TEXT NULL"),
        // Directories still pending when a scan was interrupted, so it can be continued later.
        ("ScanSessions", "ResumeState", "TEXT NULL"),
        // Whether a step is a folder or a single file. Defaulting to 1 is right for every row
        // written before this existed: only folders could be planned at all back then.
        ("StoragePlanEntries", "IsFolder", "INTEGER NOT NULL DEFAULT 1"),
        ("PlanExecutionSteps", "IsFolder", "INTEGER NOT NULL DEFAULT 1"),
        // Whether the user chose this without a rule behind it. Old rows all came from the rule
        // catalog, because nothing else could reach a plan, so 0 is correct for them.
        ("StoragePlanEntries", "HasNoRule", "INTEGER NOT NULL DEFAULT 0"),
        ("PlanExecutionSteps", "HasNoRule", "INTEGER NOT NULL DEFAULT 0")
    ];

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

            await ApplyAddedColumnsAsync(connection, cancellationToken).ConfigureAwait(false);

            _initialized = true;
            _logger.LogInformation("Database schema ensured at {Path}.", _options.DatabasePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyAddedColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var (table, column, definition) in AddedColumns)
        {
            if (await ColumnExistsAsync(connection, table, column, cancellationToken).ConfigureAwait(false))
                continue;

            await using var alter = connection.CreateCommand();
            // Table and column names come from the constant list above, never from user input.
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Added column {Table}.{Column} to the local database.", table, column);
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column;";
        command.Parameters.AddWithValue("$column", column);

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is long count && count > 0;
    }
}
