using Storava.Application.Scanning;

namespace Storava.Application.Abstractions;

/// <summary>
/// Streams a directory tree to a sink, computing aggregate folder sizes. Never mutates the
/// file system. Continues past inaccessible paths, avoids reparse-point loops, and honors
/// pause/cancel cooperatively.
/// </summary>
public interface IDiskScanner
{
    Task<ScanOutcome> ScanAsync(
        ScanRequest request,
        string sessionId,
        IScanItemSink sink,
        IProgress<ScanProgress>? progress,
        PauseToken pauseToken,
        CancellationToken cancellationToken);
}
