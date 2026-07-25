namespace Storava.Application.Scanning;

/// <summary>A throttled progress snapshot emitted while scanning.</summary>
public readonly record struct ScanProgress(
    string CurrentPath,
    long FilesScanned,
    long FoldersScanned,
    long BytesProcessed,
    int ErrorCount,
    TimeSpan Elapsed);
