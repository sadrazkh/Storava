using Storava.Domain.Entities;

namespace Storava.Application.Abstractions;

/// <summary>Persists and retrieves scan sessions (low volume).</summary>
public interface IScanSessionRepository
{
    Task SaveAsync(ScanSession session, CancellationToken cancellationToken = default);

    Task<ScanSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScanSession>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}
