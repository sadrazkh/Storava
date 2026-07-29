using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// Keeping what the AI said, without losing what the rules said.
/// <para>
/// The two have different lifetimes: the rule catalog's advice is rewritten whenever a scan is
/// analysed, the AI's whenever the user chooses to ask it. Storing them through one replace would
/// mean each rewrite silently discarded the other, and the page that shows both would show
/// whichever ran last.
/// </para>
/// </summary>
public class AiAdviceTests : IDisposable
{
    private const string SessionId = "session-advice";

    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static Recommendation Advice(string id, string scanItemId, RecommendationSource source) => new()
    {
        Id = id,
        SessionId = SessionId,
        ScanItemId = scanItemId,
        Path = $@"C:\{scanItemId}",
        Title = id,
        Reason = source == RecommendationSource.Ai ? "The model suggested this." : "A rule matched this.",
        EstimatedSpace = 1024,
        RiskLevel = RiskLevel.Low,
        Source = source
    };

    [Fact]
    public async Task SavingWhatTheAiSaid_LeavesTheRuleAdviceAlone()
    {
        var repository = _host.Get<IRecommendationRepository>();

        await repository.ReplaceForSessionAsync(SessionId,
        [
            Advice("rule-1", "item-1", RecommendationSource.RuleEngine),
            Advice("rule-2", "item-2", RecommendationSource.RuleEngine),
        ]);

        await repository.ReplaceAiAdviceAsync(SessionId,
        [
            Advice("ai-1", "item-1", RecommendationSource.Ai),
        ]);

        var all = await repository.GetBySessionAsync(SessionId);

        Assert.Equal(2, all.Count(item => item.Source == RecommendationSource.RuleEngine));
        Assert.Equal(1, all.Count(item => item.Source == RecommendationSource.Ai));
    }

    /// <summary>Asking the AI again replaces what it said before, rather than accumulating.</summary>
    [Fact]
    public async Task AskingTheAiAgain_ReplacesOnlyItsOwnAdvice()
    {
        var repository = _host.Get<IRecommendationRepository>();

        await repository.ReplaceForSessionAsync(SessionId,
            [Advice("rule-1", "item-1", RecommendationSource.RuleEngine)]);

        await repository.ReplaceAiAdviceAsync(SessionId, [Advice("ai-1", "item-1", RecommendationSource.Ai)]);
        await repository.ReplaceAiAdviceAsync(SessionId, [Advice("ai-2", "item-2", RecommendationSource.Ai)]);

        var all = await repository.GetBySessionAsync(SessionId);

        Assert.Single(all.Where(item => item.Source == RecommendationSource.Ai));
        Assert.Equal("ai-2", all.Single(item => item.Source == RecommendationSource.Ai).Id);
        Assert.Single(all.Where(item => item.Source == RecommendationSource.RuleEngine));
    }

    /// <summary>
    /// Re-analysing a scan rewrites the catalog's advice, and that is a full replace by design —
    /// the AI's advice going with it is the behaviour the separate method exists to make explicit
    /// rather than accidental.
    /// </summary>
    [Fact]
    public async Task RewritingTheRuleAdvice_ClearsEverythingForThatSession()
    {
        var repository = _host.Get<IRecommendationRepository>();

        await repository.ReplaceForSessionAsync(SessionId,
            [Advice("rule-1", "item-1", RecommendationSource.RuleEngine)]);
        await repository.ReplaceAiAdviceAsync(SessionId, [Advice("ai-1", "item-1", RecommendationSource.Ai)]);

        await repository.ReplaceForSessionAsync(SessionId,
            [Advice("rule-2", "item-2", RecommendationSource.RuleEngine)]);

        var all = await repository.GetBySessionAsync(SessionId);

        Assert.Single(all);
        Assert.Equal(RecommendationSource.RuleEngine, all[0].Source);
    }

    [Fact]
    public async Task TheAisReasonSurvivesTheRoundTrip()
    {
        var repository = _host.Get<IRecommendationRepository>();

        await repository.ReplaceAiAdviceAsync(SessionId, [Advice("ai-1", "item-1", RecommendationSource.Ai)]);

        var stored = (await repository.GetBySessionAsync(SessionId)).Single();

        Assert.Equal("The model suggested this.", stored.Reason);
        Assert.Equal("item-1", stored.ScanItemId);
        Assert.Equal(RecommendationSource.Ai, stored.Source);
    }
}
