using Storava.Domain.Entities;

namespace Storava.Application.Abstractions;

/// <summary>
/// Write side for scan items. Implementations buffer and batch-insert so millions of
/// items can be persisted without holding the tree in memory.
/// </summary>
public interface IScanItemSink : IAsyncDisposable
{
    ValueTask AddAsync(ScanItem item, CancellationToken cancellationToken = default);

    /// <summary>Flushes any buffered rows and commits.</summary>
    Task CompleteAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates a sink bound to a specific scan session.</summary>
public interface IScanItemSinkFactory
{
    IScanItemSink Create(string sessionId);
}
