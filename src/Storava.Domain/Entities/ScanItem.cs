using System.IO;
using Storava.Domain.Enums;

namespace Storava.Domain.Entities;

/// <summary>
/// A single scanned file or folder. Scan-time fields (size, metadata, protection) are
/// populated by the scanner; classification fields (category, risk, actions) are filled
/// later by the Rule Engine and AI, and default to safe/unknown values until then.
/// </summary>
public sealed class ScanItem
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? ParentId { get; set; }

    public string Path { get; set; } = string.Empty;

    /// <summary>Privacy-preserving path for AI/report use. Filled in a later phase.</summary>
    public string? SanitizedPath { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Extension { get; set; }
    public ItemType ItemType { get; set; }

    /// <summary>Logical size in bytes (aggregated for folders).</summary>
    public long Size { get; set; }

    /// <summary>On-disk allocated size in bytes (aggregated for folders).</summary>
    public long AllocatedSize { get; set; }

    /// <summary>Number of files contained (recursively) for folders.</summary>
    public int FileCount { get; set; }

    /// <summary>Number of sub-folders contained (recursively) for folders.</summary>
    public int FolderCount { get; set; }

    public int Depth { get; set; }

    public DateTimeOffset? CreationTime { get; set; }
    public DateTimeOffset? LastWriteTime { get; set; }
    public DateTimeOffset? LastAccessTime { get; set; }

    public FileAttributes Attributes { get; set; }
    public bool IsHidden { get; set; }
    public bool IsSystem { get; set; }
    public bool IsReparsePoint { get; set; }

    /// <summary>True when the item lives under a protected, system-critical location.</summary>
    public bool IsProtected { get; set; }

    // --- Classification (populated in later phases) ---
    public StorageCategory Category { get; set; } = StorageCategory.Unknown;
    public string? DetectedTechnology { get; set; }
    public string? KnownRuleId { get; set; }
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Unknown;
    public double Confidence { get; set; }
    public bool CanDelete { get; set; }
    public bool CanMove { get; set; }
    public bool CanRegenerate { get; set; }
    public SuggestedAction SuggestedAction { get; set; } = SuggestedAction.NoAction;
    public string? Reason { get; set; }
}
