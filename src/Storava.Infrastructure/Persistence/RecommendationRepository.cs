using Microsoft.Data.Sqlite;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Persistence;

public sealed class RecommendationRepository : IRecommendationRepository
{
    private const string Columns =
        "Id, SessionId, ScanItemId, Path, Title, Reason, SuggestedAction, RiskLevel, Category, " +
        "Technology, RuleId, EstimatedSpace, Confidence, Score, CanDelete, CanMove, CanRegenerate, " +
        "OfficialMigrationMethod, FallbackMigrationMethod, OfficialMigrationHint, Warning, Source";

    private readonly StoravaDbOptions _options;
    private readonly IDatabaseInitializer _initializer;

    public RecommendationRepository(StoravaDbOptions options, IDatabaseInitializer initializer)
    {
        _options = options;
        _initializer = initializer;
    }

    public Task ReplaceForSessionAsync(
        string sessionId,
        IEnumerable<Recommendation> recommendations,
        CancellationToken cancellationToken = default) =>
        ReplaceAsync(sessionId, recommendations, onlyAi: false, cancellationToken);

    public Task ReplaceAiAdviceAsync(
        string sessionId,
        IEnumerable<Recommendation> recommendations,
        CancellationToken cancellationToken = default) =>
        ReplaceAsync(sessionId, recommendations, onlyAi: true, cancellationToken);

    /// <param name="onlyAi">
    /// Narrows the delete to rows the AI produced. Without it, saving what the AI said would take
    /// the rule catalog's advice with it.
    /// </param>
    private async Task ReplaceAsync(
        string sessionId,
        IEnumerable<Recommendation> recommendations,
        bool onlyAi,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(recommendations);

        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = onlyAi
                ? $"DELETE FROM Recommendations WHERE SessionId = $s AND Source = {(int)RecommendationSource.Ai};"
                : "DELETE FROM Recommendations WHERE SessionId = $s;";
            delete.Parameters.AddWithValue("$s", sessionId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO Recommendations ({Columns}) VALUES (
                    $Id, $SessionId, $ScanItemId, $Path, $Title, $Reason, $SuggestedAction, $RiskLevel,
                    $Category, $Technology, $RuleId, $EstimatedSpace, $Confidence, $Score, $CanDelete,
                    $CanMove, $CanRegenerate, $OfficialMigrationMethod, $FallbackMigrationMethod,
                    $OfficialMigrationHint, $Warning, $Source);
                """;

            var p = insert.Parameters;
            var id = p.Add("$Id", SqliteType.Text);
            var session = p.Add("$SessionId", SqliteType.Text);
            var itemId = p.Add("$ScanItemId", SqliteType.Text);
            var path = p.Add("$Path", SqliteType.Text);
            var title = p.Add("$Title", SqliteType.Text);
            var reason = p.Add("$Reason", SqliteType.Text);
            var action = p.Add("$SuggestedAction", SqliteType.Integer);
            var risk = p.Add("$RiskLevel", SqliteType.Integer);
            var category = p.Add("$Category", SqliteType.Integer);
            var technology = p.Add("$Technology", SqliteType.Text);
            var ruleId = p.Add("$RuleId", SqliteType.Text);
            var space = p.Add("$EstimatedSpace", SqliteType.Integer);
            var confidence = p.Add("$Confidence", SqliteType.Real);
            var score = p.Add("$Score", SqliteType.Real);
            var canDelete = p.Add("$CanDelete", SqliteType.Integer);
            var canMove = p.Add("$CanMove", SqliteType.Integer);
            var canRegenerate = p.Add("$CanRegenerate", SqliteType.Integer);
            var official = p.Add("$OfficialMigrationMethod", SqliteType.Integer);
            var fallback = p.Add("$FallbackMigrationMethod", SqliteType.Integer);
            var hint = p.Add("$OfficialMigrationHint", SqliteType.Text);
            var warning = p.Add("$Warning", SqliteType.Text);
            var source = p.Add("$Source", SqliteType.Integer);

            foreach (var r in recommendations)
            {
                id.Value = r.Id;
                session.Value = r.SessionId;
                itemId.Value = r.ScanItemId;
                path.Value = r.Path;
                title.Value = r.Title;
                reason.Value = r.Reason;
                action.Value = (int)r.SuggestedAction;
                risk.Value = (int)r.RiskLevel;
                category.Value = (int)r.Category;
                technology.Value = (object?)r.Technology ?? DBNull.Value;
                ruleId.Value = (object?)r.RuleId ?? DBNull.Value;
                space.Value = r.EstimatedSpace;
                confidence.Value = r.Confidence;
                score.Value = r.Score;
                canDelete.Value = r.CanDelete ? 1 : 0;
                canMove.Value = r.CanMove ? 1 : 0;
                canRegenerate.Value = r.CanRegenerate ? 1 : 0;
                official.Value = (int)r.OfficialMigrationMethod;
                fallback.Value = (int)r.FallbackMigrationMethod;
                hint.Value = (object?)r.OfficialMigrationHint ?? DBNull.Value;
                warning.Value = (object?)r.Warning ?? DBNull.Value;
                source.Value = (int)r.Source;

                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Recommendation>> GetBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        await _initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM Recommendations WHERE SessionId = $s ORDER BY Score DESC;";
        command.Parameters.AddWithValue("$s", sessionId);

        var result = new List<Recommendation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(Map(reader));
        return result;
    }

    private static Recommendation Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        SessionId = r.GetString(1),
        ScanItemId = r.GetString(2),
        Path = r.GetString(3),
        Title = r.GetString(4),
        Reason = r.GetString(5),
        SuggestedAction = (SuggestedAction)r.GetInt32(6),
        RiskLevel = (RiskLevel)r.GetInt32(7),
        Category = (StorageCategory)r.GetInt32(8),
        Technology = r.IsDBNull(9) ? null : r.GetString(9),
        RuleId = r.IsDBNull(10) ? null : r.GetString(10),
        EstimatedSpace = r.GetInt64(11),
        Confidence = r.GetDouble(12),
        Score = r.GetDouble(13),
        CanDelete = r.GetInt32(14) != 0,
        CanMove = r.GetInt32(15) != 0,
        CanRegenerate = r.GetInt32(16) != 0,
        OfficialMigrationMethod = (MigrationMethod)r.GetInt32(17),
        FallbackMigrationMethod = (MigrationMethod)r.GetInt32(18),
        OfficialMigrationHint = r.IsDBNull(19) ? null : r.GetString(19),
        Warning = r.IsDBNull(20) ? null : r.GetString(20),
        Source = (RecommendationSource)r.GetInt32(21)
    };
}
