using Storava.Domain.Entities;

namespace Storava.Application.Abstractions;

/// <summary>Stores and reads the recommendations produced for a scan.</summary>
public interface IRecommendationRepository
{
    /// <summary>Replaces the stored recommendations for a session.</summary>
    Task ReplaceForSessionAsync(
        string sessionId,
        IEnumerable<Recommendation> recommendations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces what the AI said about a session, leaving the rule catalog's own advice alone.
    /// <para>
    /// Separate from <see cref="ReplaceForSessionAsync"/> because the two have different lifetimes:
    /// the catalog's advice is rewritten whenever a scan is analysed, and the AI's whenever the
    /// user chooses to ask it. Either replacing both would silently discard the other.
    /// </para>
    /// </summary>
    Task ReplaceAiAdviceAsync(
        string sessionId,
        IEnumerable<Recommendation> recommendations,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Recommendation>> GetBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}
