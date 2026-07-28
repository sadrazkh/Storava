using Microsoft.Data.Sqlite;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Persistence;

/// <summary>
/// Stores what Storava did to the disk.
/// <para>
/// <see cref="SaveStepAsync"/> is an upsert of a single row rather than a rewrite of the run,
/// because it is called in the middle of a file operation: writing one row keeps the window in
/// which a crash could lose the record down to a single statement.
/// </para>
/// </summary>
public sealed class PlanExecutionRepository : IPlanExecutionRepository
{
    private const string ExecutionColumns = "Id, PlanId, SessionId, StartedAt, CompletedAt";

    private const string StepColumns =
        "Id, ExecutionId, PlanEntryId, ScanItemId, SourcePath, Title, Action, Method, SortOrder, " +
        "DestinationPath, Status, MeasuredBytes, BytesFreed, StartedAt, CompletedAt, RecycledPath, " +
        "LinkPath, ErrorCode, ErrorMessage, IsFolder, HasNoRule";

    private readonly StoravaDbOptions _options;
    private readonly IDatabaseInitializer _initializer;

    public PlanExecutionRepository(StoravaDbOptions options, IDatabaseInitializer initializer)
    {
        _options = options;
        _initializer = initializer;
    }

    public async Task SaveAsync(PlanExecution execution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = $"""
                INSERT INTO PlanExecutions ({ExecutionColumns})
                VALUES ($Id, $PlanId, $SessionId, $StartedAt, $CompletedAt)
                ON CONFLICT(Id) DO UPDATE SET CompletedAt = excluded.CompletedAt;
                """;
            upsert.Parameters.AddWithValue("$Id", execution.Id);
            upsert.Parameters.AddWithValue("$PlanId", execution.PlanId);
            upsert.Parameters.AddWithValue("$SessionId", execution.SessionId);
            upsert.Parameters.AddWithValue("$StartedAt", execution.StartedAt.ToString("O"));
            upsert.Parameters.AddWithValue("$CompletedAt", (object?)execution.CompletedAt?.ToString("O") ?? DBNull.Value);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var step in execution.Steps)
            await WriteStepAsync(connection, transaction, step, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveStepAsync(PlanExecutionStep step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await WriteStepAsync(connection, transaction: null, step, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteStepAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        PlanExecutionStep step,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO PlanExecutionSteps ({StepColumns}) VALUES (
                $Id, $ExecutionId, $PlanEntryId, $ScanItemId, $SourcePath, $Title, $Action, $Method,
                $SortOrder, $DestinationPath, $Status, $MeasuredBytes, $BytesFreed, $StartedAt,
                $CompletedAt, $RecycledPath, $LinkPath, $ErrorCode, $ErrorMessage, $IsFolder,
                $HasNoRule)
            ON CONFLICT(Id) DO UPDATE SET
                DestinationPath = excluded.DestinationPath,
                Status          = excluded.Status,
                MeasuredBytes   = excluded.MeasuredBytes,
                BytesFreed      = excluded.BytesFreed,
                StartedAt       = excluded.StartedAt,
                CompletedAt     = excluded.CompletedAt,
                RecycledPath    = excluded.RecycledPath,
                LinkPath        = excluded.LinkPath,
                ErrorCode       = excluded.ErrorCode,
                ErrorMessage    = excluded.ErrorMessage;
            """;

        var p = command.Parameters;
        p.AddWithValue("$Id", step.Id);
        p.AddWithValue("$ExecutionId", step.ExecutionId);
        p.AddWithValue("$PlanEntryId", step.PlanEntryId);
        p.AddWithValue("$ScanItemId", step.ScanItemId);
        p.AddWithValue("$SourcePath", step.SourcePath);
        p.AddWithValue("$Title", step.Title);
        p.AddWithValue("$Action", (int)step.Action);
        p.AddWithValue("$Method", (int)step.Method);
        // Carried rather than re-probed: a folder that became a file between planning and running
        // is exactly the substitution the confirmation exists to refuse.
        p.AddWithValue("$IsFolder", step.IsFolder ? 1 : 0);
        p.AddWithValue("$HasNoRule", step.HasNoRule ? 1 : 0);
        p.AddWithValue("$SortOrder", step.Order);
        p.AddWithValue("$DestinationPath", (object?)step.DestinationPath ?? DBNull.Value);
        p.AddWithValue("$Status", (int)step.Status);
        p.AddWithValue("$MeasuredBytes", step.MeasuredBytes);
        p.AddWithValue("$BytesFreed", step.BytesFreed);
        p.AddWithValue("$StartedAt", (object?)step.StartedAt?.ToString("O") ?? DBNull.Value);
        p.AddWithValue("$CompletedAt", (object?)step.CompletedAt?.ToString("O") ?? DBNull.Value);
        p.AddWithValue("$RecycledPath", (object?)step.RecycledPath ?? DBNull.Value);
        p.AddWithValue("$LinkPath", (object?)step.LinkPath ?? DBNull.Value);
        p.AddWithValue("$ErrorCode", (object?)step.ErrorCode ?? DBNull.Value);
        p.AddWithValue("$ErrorMessage", (object?)step.ErrorMessage ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlanExecution?> GetAsync(string executionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadOneAsync(
            connection,
            $"SELECT {ExecutionColumns} FROM PlanExecutions WHERE Id = $v;",
            executionId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlanExecution?> GetLatestForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadOneAsync(
            connection,
            $"SELECT {ExecutionColumns} FROM PlanExecutions WHERE SessionId = $v ORDER BY StartedAt DESC LIMIT 1;",
            sessionId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlanExecution>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        var executions = new List<PlanExecution>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT {ExecutionColumns} FROM PlanExecutions ORDER BY StartedAt DESC LIMIT $n;";
            command.Parameters.AddWithValue("$n", Math.Max(1, limit));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                executions.Add(MapExecution(reader));
        }

        foreach (var execution in executions)
            execution.Load(await LoadStepsAsync(connection, execution.Id, cancellationToken).ConfigureAwait(false));

        return executions;
    }

    private static async Task<PlanExecution?> ReadOneAsync(
        SqliteConnection connection,
        string sql,
        string parameter,
        CancellationToken cancellationToken)
    {
        PlanExecution execution;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            command.Parameters.AddWithValue("$v", parameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            execution = MapExecution(reader);
        }

        execution.Load(await LoadStepsAsync(connection, execution.Id, cancellationToken).ConfigureAwait(false));
        return execution;
    }

    private static async Task<List<PlanExecutionStep>> LoadStepsAsync(
        SqliteConnection connection,
        string executionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {StepColumns} FROM PlanExecutionSteps WHERE ExecutionId = $e ORDER BY SortOrder;";
        command.Parameters.AddWithValue("$e", executionId);

        var steps = new List<PlanExecutionStep>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            steps.Add(MapStep(reader));

        return steps;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static PlanExecution MapExecution(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        PlanId = r.GetString(1),
        SessionId = r.GetString(2),
        StartedAt = r.GetFieldValue<DateTimeOffset>(3),
        CompletedAt = r.IsDBNull(4) ? null : r.GetFieldValue<DateTimeOffset>(4)
    };

    private static PlanExecutionStep MapStep(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        ExecutionId = r.GetString(1),
        PlanEntryId = r.GetString(2),
        ScanItemId = r.GetString(3),
        SourcePath = r.GetString(4),
        Title = r.GetString(5),
        Action = (SuggestedAction)r.GetInt32(6),
        Method = (MigrationMethod)r.GetInt32(7),
        Order = r.GetInt32(8),
        DestinationPath = r.IsDBNull(9) ? null : r.GetString(9),
        Status = (ExecutionStatus)r.GetInt32(10),
        MeasuredBytes = r.GetInt64(11),
        BytesFreed = r.GetInt64(12),
        StartedAt = r.IsDBNull(13) ? null : r.GetFieldValue<DateTimeOffset>(13),
        CompletedAt = r.IsDBNull(14) ? null : r.GetFieldValue<DateTimeOffset>(14),
        RecycledPath = r.IsDBNull(15) ? null : r.GetString(15),
        LinkPath = r.IsDBNull(16) ? null : r.GetString(16),
        ErrorCode = r.IsDBNull(17) ? null : r.GetString(17),
        ErrorMessage = r.IsDBNull(18) ? null : r.GetString(18),
        IsFolder = r.GetInt32(19) != 0,
        HasNoRule = r.GetInt32(20) != 0
    };
}
