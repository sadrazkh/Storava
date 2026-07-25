using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;

namespace Storava.Rules;

/// <summary>
/// Produces and persists the local (non-AI) analysis for a completed scan: ranked
/// recommendations derived from the classification stored during scanning.
/// </summary>
public sealed class AnalysisService
{
    private const int CandidateLimit = 400;
    private const int RecommendationLimit = 40;

    private readonly IScanQueryService _query;
    private readonly IRecommendationRepository _repository;
    private readonly RecommendationBuilder _builder;
    private readonly ILogger<AnalysisService> _logger;

    public AnalysisService(
        IScanQueryService query,
        IRecommendationRepository repository,
        RecommendationBuilder builder,
        ILogger<AnalysisService> logger)
    {
        _query = query;
        _repository = repository;
        _builder = builder;
        _logger = logger;
    }

    /// <summary>
    /// Regenerates recommendations for a session in the given language and stores them.
    /// Safe to call repeatedly — the previous set for the session is replaced.
    /// </summary>
    public async Task<IReadOnlyList<Recommendation>> AnalyzeAsync(
        string sessionId,
        string language,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var candidates = await _query
            .GetRecommendationCandidatesAsync(sessionId, RecommendationBuilder.MinimumCandidateSize, CandidateLimit, cancellationToken)
            .ConfigureAwait(false);

        var recommendations = _builder.BuildFromPersisted(
            sessionId, candidates, language, DateTimeOffset.UtcNow, RecommendationLimit);

        await _repository.ReplaceForSessionAsync(sessionId, recommendations, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Analysis for {SessionId}: {Candidates} candidates -> {Count} recommendations.",
            sessionId, candidates.Count, recommendations.Count);

        return recommendations;
    }
}
