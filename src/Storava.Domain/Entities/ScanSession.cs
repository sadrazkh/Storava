using Storava.Domain.Enums;

namespace Storava.Domain.Entities;

/// <summary>A single scan run over a chosen root path, with aggregate results.</summary>
public sealed class ScanSession
{
    public string Id { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string? Label { get; set; }

    public ScanMode Mode { get; set; }
    public ScanStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public long TotalSize { get; set; }
    public int TotalFiles { get; set; }
    public int TotalFolders { get; set; }
    public int ErrorCount { get; set; }

    public TimeSpan? Duration => CompletedAt is { } end ? end - StartedAt : null;
}
