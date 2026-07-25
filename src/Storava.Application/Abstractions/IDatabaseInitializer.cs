namespace Storava.Application.Abstractions;

/// <summary>
/// Ensures the local database schema exists. Idempotent and safe to call repeatedly;
/// it creates any missing tables and indexes without destroying existing data.
/// </summary>
public interface IDatabaseInitializer
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);
}
