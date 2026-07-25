using Storava.Application.Scanning;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Rules.Model;
using Storava.Rules.Scoring;

namespace Storava.Rules;

/// <summary>
/// Turns classified items into ranked recommendations. Everything it produces is advice bound
/// to a real scan item, and every suggestion starts at NoAction until the user chooses.
/// </summary>
public sealed class RecommendationBuilder
{
    /// <summary>Below this, an item is not worth surfacing as a recommendation.</summary>
    public const long MinimumCandidateSize = 50L * 1024 * 1024;

    private readonly ClassificationService _classification;
    private readonly RecommendationScoreCalculator _scoreCalculator;
    private readonly RuleEngine _engine;

    public RecommendationBuilder(
        ClassificationService classification,
        RecommendationScoreCalculator scoreCalculator,
        RuleEngine engine)
    {
        _classification = classification;
        _scoreCalculator = scoreCalculator;
        _engine = engine;
    }

    /// <summary>
    /// Builds recommendations from freshly scanned entities (classifying them on the way),
    /// highest ranked first.
    /// </summary>
    /// <param name="language">Language code ("en"/"fa") for the generated text.</param>
    public IReadOnlyList<Recommendation> Build(
        string sessionId,
        IEnumerable<ScanItem> items,
        string language,
        DateTimeOffset now,
        int limit = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(items);

        var candidates = new List<Recommendation>();
        foreach (var item in items)
        {
            var built = TryBuild(sessionId, item, language, now);
            if (built is not null)
                candidates.Add(built);
        }

        return Rank(candidates, limit);
    }

    /// <summary>
    /// Builds recommendations from persisted rows that were already classified during the scan.
    /// </summary>
    public IReadOnlyList<Recommendation> BuildFromPersisted(
        string sessionId,
        IEnumerable<ScanItemView> items,
        string language,
        DateTimeOffset now,
        int limit = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(items);

        var candidates = new List<Recommendation>();
        foreach (var view in items)
        {
            if (view.IsProtected || view.RiskLevel == RiskLevel.Protected)
                continue;
            if (!view.IsIdentified)
                continue;
            if (!view.CanDelete && !view.CanMove)
                continue;
            if (view.Size < MinimumCandidateSize)
                continue;

            var rule = FindRule(view.KnownRuleId!);
            if (rule is null)
                continue;

            var classification = new ClassificationResult(
                view.Category,
                view.DetectedTechnology,
                view.KnownRuleId,
                view.RiskLevel,
                view.Confidence,
                view.CanDelete,
                view.CanMove,
                view.CanRegenerate,
                rule.OfficialMigrationMethod,
                rule.FallbackMigrationMethod);

            var score = _scoreCalculator.Calculate(ScoringInput.From(view), classification, now);
            candidates.Add(Compose(sessionId, view.Id, view.Path, rule, classification, view.Size, score.Total, language));
        }

        return Rank(candidates, limit);
    }

    /// <summary>Builds a single recommendation, or null when the item is not a candidate.</summary>
    public Recommendation? TryBuild(string sessionId, ScanItem item, string language, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        var classification = _classification.Classify(item);

        // Never advise on protected locations, or on items the catalog cannot identify.
        if (classification.RiskLevel == RiskLevel.Protected)
            return null;
        if (classification.RuleId is null)
            return null;
        if (!classification.CanDelete && !classification.CanMove)
            return null;
        if (item.Size < MinimumCandidateSize)
            return null;

        var rule = FindRule(classification.RuleId);
        if (rule is null)
            return null;

        var score = _scoreCalculator.Calculate(item, classification, now);
        return Compose(sessionId, item.Id, item.Path, rule, classification, item.Size, score.Total, language);
    }

    private StorageRule? FindRule(string ruleId) =>
        _engine.Rules.FirstOrDefault(r => r.Id == ruleId);

    private static Recommendation Compose(
        string sessionId,
        string scanItemId,
        string path,
        StorageRule rule,
        ClassificationResult classification,
        long size,
        double score,
        string language) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            ScanItemId = scanItemId,
            Path = path,
            Title = rule.GetTitle(language),
            Reason = rule.GetDescription(language),
            // Advice only. The user picks the actual action in the Storage Plan phase.
            SuggestedAction = SuggestedAction.NoAction,
            RiskLevel = classification.RiskLevel,
            Category = classification.Category,
            Technology = classification.Technology,
            RuleId = rule.Id,
            EstimatedSpace = size,
            Confidence = classification.Confidence,
            Score = score,
            CanDelete = classification.CanDelete,
            CanMove = classification.CanMove,
            CanRegenerate = classification.CanRegenerate,
            OfficialMigrationMethod = classification.OfficialMigrationMethod,
            FallbackMigrationMethod = classification.FallbackMigrationMethod,
            OfficialMigrationHint = rule.OfficialMigrationHint,
            Warning = rule.GetWarning(language),
            Source = RecommendationSource.RuleEngine
        };

    /// <summary>
    /// Orders by score and drops nested duplicates: when an ancestor already covers the same
    /// rule (e.g. an outer bin\ containing another), the inner one adds nothing but noise.
    /// </summary>
    private static IReadOnlyList<Recommendation> Rank(List<Recommendation> candidates, int limit)
    {
        var ordered = candidates
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.EstimatedSpace)
            .ToList();

        var kept = new List<Recommendation>();
        foreach (var candidate in ordered)
        {
            bool coveredByAncestor = kept.Any(existing =>
                existing.RuleId == candidate.RuleId &&
                candidate.Path.StartsWith(existing.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

            if (!coveredByAncestor)
                kept.Add(candidate);

            if (kept.Count >= limit)
                break;
        }

        return kept;
    }
}
