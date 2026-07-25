using Storava.Application.Scanning;
using Storava.Domain.Entities;

namespace Storava.Rules.Scoring;

/// <summary>
/// The signals scoring needs, decoupled from where they came from — a freshly scanned entity
/// or a row already persisted and read back.
/// </summary>
public sealed record ScoringInput(
    long Size,
    DateTimeOffset? CreationTime,
    DateTimeOffset? LastWriteTime,
    bool IsSystem)
{
    public static ScoringInput From(ScanItem item) =>
        new(item.Size, item.CreationTime, item.LastWriteTime, item.IsSystem);

    public static ScoringInput From(ScanItemView view) =>
        new(view.Size, view.CreationTime, view.LastWriteTime, view.IsSystem);
}
