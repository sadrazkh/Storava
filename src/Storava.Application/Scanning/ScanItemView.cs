using Storava.Domain.Enums;

namespace Storava.Application.Scanning;

/// <summary>Read-projection of a scan item for the UI. Kept lean for large result sets.</summary>
public sealed record ScanItemView(
    string Id,
    string? ParentId,
    string Path,
    string Name,
    string? Extension,
    ItemType ItemType,
    long Size,
    long AllocatedSize,
    int FileCount,
    int FolderCount,
    int Depth,
    DateTimeOffset? CreationTime,
    DateTimeOffset? LastWriteTime,
    bool IsReparsePoint,
    bool IsProtected,
    bool IsHidden,
    bool IsSystem,
    RiskLevel RiskLevel,
    StorageCategory Category,
    string? DetectedTechnology,
    string? KnownRuleId,
    double Confidence,
    bool CanDelete,
    bool CanMove,
    bool CanRegenerate)
{
    public bool IsFolder => ItemType == ItemType.Folder;
    public bool HasChildren => IsFolder && (FileCount > 0 || FolderCount > 0);

    /// <summary>True when the rule engine recognised this item.</summary>
    public bool IsIdentified => KnownRuleId is { Length: > 0 };
}
