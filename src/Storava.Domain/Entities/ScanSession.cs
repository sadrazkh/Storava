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

    /// <summary>Whether this scan ran on this machine or arrived in a .storava file.</summary>
    public ScanOrigin Origin { get; set; } = ScanOrigin.Local;

    public DateTimeOffset? ImportedAt { get; set; }

    /// <summary>For imported scans, the file or machine it came from (display only).</summary>
    public string? SourceLabel { get; set; }

    /// <summary>
    /// Directories that were still pending when the scan stopped, serialized as JSON. Present
    /// only while a scan is resumable; cleared once it completes.
    /// </summary>
    public string? ResumeState { get; set; }

    public TimeSpan? Duration => CompletedAt is { } end ? end - StartedAt : null;

    public bool IsImported => Origin == ScanOrigin.Imported;

    /// <summary>A scan that stopped early and still has pending work recorded.</summary>
    public bool CanResume =>
        Origin == ScanOrigin.Local
        && Status is ScanStatus.Cancelled or ScanStatus.Failed or ScanStatus.Paused
        && !string.IsNullOrWhiteSpace(ResumeState);
}
