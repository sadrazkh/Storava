using Microsoft.Data.Sqlite;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Persistence;

/// <summary>
/// Persists a plan and its steps. `CoveredByEntryId` and `Order` are derived, not stored: the
/// plan recomputes them on load, so a hand-edited database cannot make the totals lie.
/// </summary>
public sealed class StoragePlanRepository : IStoragePlanRepository
{
    private const string PlanColumns = "Id, SessionId, Name, CreatedAt, UpdatedAt, GoalBytes";

    private const string EntryColumns =
        "Id, PlanId, RecommendationId, ScanItemId, Path, Title, Action, EstimatedSpace, RiskLevel, " +
        "Category, Technology, Method, MethodHint, Warning, AddedAt, SortOrder, IsFolder, HasNoRule";

    private readonly StoravaDbOptions _options;
    private readonly IDatabaseInitializer _initializer;

    public StoragePlanRepository(StoravaDbOptions options, IDatabaseInitializer initializer)
    {
        _options = options;
        _initializer = initializer;
    }

    public async Task<StoragePlan?> GetForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        StoragePlan plan;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT {PlanColumns} FROM StoragePlans WHERE SessionId = $s;";
            command.Parameters.AddWithValue("$s", sessionId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            plan = new StoragePlan
            {
                Id = reader.GetString(0),
                SessionId = reader.GetString(1),
                Name = reader.IsDBNull(2) ? null : reader.GetString(2),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(3),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(4),
                GoalBytes = reader.GetInt64(5)
            };
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT {EntryColumns} FROM StoragePlanEntries WHERE PlanId = $p ORDER BY SortOrder;";
            command.Parameters.AddWithValue("$p", plan.Id);

            var entries = new List<StoragePlanEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                entries.Add(MapEntry(reader));

            // Load() re-derives ordering and nesting, so the totals are always freshly computed.
            plan.Load(entries);
        }

        return plan;
    }

    public async Task SaveAsync(StoragePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // One plan per session: clear whatever was there, including a plan with a different id.
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM StoragePlanEntries WHERE PlanId IN (SELECT Id FROM StoragePlans WHERE SessionId = $s);
                DELETE FROM StoragePlans WHERE SessionId = $s;
                """;
            delete.Parameters.AddWithValue("$s", plan.SessionId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO StoragePlans ({PlanColumns})
                VALUES ($Id, $SessionId, $Name, $CreatedAt, $UpdatedAt, $GoalBytes);
                """;
            insert.Parameters.AddWithValue("$Id", plan.Id);
            insert.Parameters.AddWithValue("$SessionId", plan.SessionId);
            insert.Parameters.AddWithValue("$Name", (object?)plan.Name ?? DBNull.Value);
            insert.Parameters.AddWithValue("$CreatedAt", plan.CreatedAt);
            insert.Parameters.AddWithValue("$UpdatedAt", plan.UpdatedAt);
            insert.Parameters.AddWithValue("$GoalBytes", plan.GoalBytes);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (plan.Entries.Count > 0)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO StoragePlanEntries ({EntryColumns}) VALUES (
                    $Id, $PlanId, $RecommendationId, $ScanItemId, $Path, $Title, $Action,
                    $EstimatedSpace, $RiskLevel, $Category, $Technology, $Method, $MethodHint,
                    $Warning, $AddedAt, $SortOrder, $IsFolder, $HasNoRule);
                """;

            var p = insert.Parameters;
            var id = p.Add("$Id", SqliteType.Text);
            var planId = p.Add("$PlanId", SqliteType.Text);
            var recommendationId = p.Add("$RecommendationId", SqliteType.Text);
            var scanItemId = p.Add("$ScanItemId", SqliteType.Text);
            var path = p.Add("$Path", SqliteType.Text);
            var title = p.Add("$Title", SqliteType.Text);
            var action = p.Add("$Action", SqliteType.Integer);
            var space = p.Add("$EstimatedSpace", SqliteType.Integer);
            var risk = p.Add("$RiskLevel", SqliteType.Integer);
            var category = p.Add("$Category", SqliteType.Integer);
            var technology = p.Add("$Technology", SqliteType.Text);
            var isFolder = p.Add("$IsFolder", SqliteType.Integer);
            var hasNoRule = p.Add("$HasNoRule", SqliteType.Integer);
            var method = p.Add("$Method", SqliteType.Integer);
            var methodHint = p.Add("$MethodHint", SqliteType.Text);
            var warning = p.Add("$Warning", SqliteType.Text);
            var addedAt = p.Add("$AddedAt", SqliteType.Text);
            var order = p.Add("$SortOrder", SqliteType.Integer);

            foreach (var entry in plan.Entries)
            {
                id.Value = entry.Id;
                planId.Value = plan.Id;
                // Empty rather than null: the column is NOT NULL on every database already in
                // existence, and HasNoRule is what actually says whether there is one.
                recommendationId.Value = entry.RecommendationId ?? string.Empty;
                isFolder.Value = entry.IsFolder ? 1 : 0;
                hasNoRule.Value = entry.HasNoRule ? 1 : 0;
                scanItemId.Value = entry.ScanItemId;
                path.Value = entry.Path;
                title.Value = entry.Title;
                action.Value = (int)entry.Action;
                space.Value = entry.EstimatedSpace;
                risk.Value = (int)entry.RiskLevel;
                category.Value = (int)entry.Category;
                technology.Value = (object?)entry.Technology ?? DBNull.Value;
                method.Value = (int)entry.Method;
                methodHint.Value = (object?)entry.MethodHint ?? DBNull.Value;
                warning.Value = (object?)entry.Warning ?? DBNull.Value;
                addedAt.Value = entry.AddedAt.ToString("O");
                order.Value = entry.Order;

                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteForSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM StoragePlanEntries WHERE PlanId IN (SELECT Id FROM StoragePlans WHERE SessionId = $s);
            DELETE FROM StoragePlans WHERE SessionId = $s;
            """;
        command.Parameters.AddWithValue("$s", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static StoragePlanEntry MapEntry(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        PlanId = r.GetString(1),
        RecommendationId = r.IsDBNull(2) || r.GetString(2).Length == 0 ? null : r.GetString(2),
        ScanItemId = r.GetString(3),
        Path = r.GetString(4),
        Title = r.GetString(5),
        Action = (SuggestedAction)r.GetInt32(6),
        EstimatedSpace = r.GetInt64(7),
        RiskLevel = (RiskLevel)r.GetInt32(8),
        Category = (StorageCategory)r.GetInt32(9),
        Technology = r.IsDBNull(10) ? null : r.GetString(10),
        Method = (MigrationMethod)r.GetInt32(11),
        MethodHint = r.IsDBNull(12) ? null : r.GetString(12),
        Warning = r.IsDBNull(13) ? null : r.GetString(13),
        AddedAt = r.GetFieldValue<DateTimeOffset>(14),
        Order = r.GetInt32(15),
        IsFolder = r.GetInt32(16) != 0,
        HasNoRule = r.GetInt32(17) != 0
    };
}
