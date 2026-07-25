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

    Task<IReadOnlyList<Recommendation>> GetBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}
