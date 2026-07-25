namespace Storava.Infrastructure.Persistence;

/// <summary>Shared database location used by both EF Core and the raw SQLite components.</summary>
public sealed class StoravaDbOptions
{
    public required string DatabasePath { get; init; }
    public required string ConnectionString { get; init; }
}
