using Storava.Domain.Enums;

namespace Storava.Application.Scanning;

/// <summary>Final outcome of a scan run.</summary>
public sealed record ScanResult(
    string SessionId,
    ScanStatus Status,
    long TotalSize,
    long TotalFiles,
    long TotalFolders,
    int ErrorCount,
    TimeSpan Duration);

/// <summary>Aggregate totals returned by the low-level scanner (root aggregation).</summary>
public readonly record struct ScanOutcome(
    long TotalSize,
    long TotalFiles,
    long TotalFolders,
    int ErrorCount);
