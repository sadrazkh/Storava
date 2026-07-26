using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Reporting.Model;

/// <summary>
/// Everything a report can show, assembled locally. Reports never contain the API key or any
/// setting that could identify the machine beyond what the user already sees on screen.
/// </summary>
public sealed record StorageReport
{
    public required string SessionId { get; init; }
    public required string RootPath { get; init; }
    public required string Language { get; init; }

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset ScanStartedAt { get; init; }
    public TimeSpan ScanDuration { get; init; }

    public long TotalSize { get; init; }
    public int FileCount { get; init; }
    public int FolderCount { get; init; }
    public int ErrorCount { get; init; }

    public long DriveCapacity { get; init; }
    public long DriveFreeSpace { get; init; }

    public IReadOnlyList<ReportCategory> Categories { get; init; } = [];
    public IReadOnlyList<ReportItem> LargestItems { get; init; } = [];
    public IReadOnlyList<ReportRecommendation> Recommendations { get; init; } = [];

    /// <summary>Present only when the user ran an AI analysis for this scan.</summary>
    public ReportAiSection? Ai { get; init; }

    public long TotalReclaimable => Recommendations.Sum(r => r.EstimatedSpace);
}

public sealed record ReportCategory(StorageCategory Category, string Label, long TotalSize, double Share);

public sealed record ReportItem(string Path, string Name, long Size, StorageCategory Category, string CategoryLabel, bool IsProtected);

public sealed record ReportRecommendation(
    string Title,
    string Path,
    string Reason,
    long EstimatedSpace,
    RiskLevel RiskLevel,
    string RiskLabel,
    StorageCategory Category,
    string CategoryLabel,
    string? Technology,
    string? OfficialMigrationHint,
    string? Warning,
    double Confidence,
    bool CanDelete,
    bool CanMove,
    bool CanRegenerate,
    RecommendationSource Source);

public sealed record ReportAiSection(
    string ModelName,
    DateTimeOffset GeneratedAt,
    string? Summary,
    string? MainCause,
    string? Overview,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> NextSteps,
    int AcceptedCount,
    int RejectedCount);
