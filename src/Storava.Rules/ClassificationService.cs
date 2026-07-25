using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Rules;

/// <summary>
/// Assigns a category, technology, risk level and permitted actions to each scanned item.
/// Protection is checked before anything else, so a system-critical path can never come out
/// of classification as deletable or movable.
/// </summary>
public sealed class ClassificationService
{
    private readonly RuleEngine _engine;
    private readonly IProtectedPathService _protectedPaths;

    public ClassificationService(RuleEngine engine, IProtectedPathService protectedPaths)
    {
        _engine = engine;
        _protectedPaths = protectedPaths;
    }

    public ClassificationResult Classify(ScanItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Protection wins over every rule, including anything a later AI phase might suggest.
        if (item.IsProtected || _protectedPaths.IsProtected(item.Path))
            return ClassificationResult.Protected;

        var match = _engine.Match(item);
        if (match is null)
            return InferWithoutRule(item);

        var rule = match.Rule;
        return new ClassificationResult(
            rule.Category,
            rule.Technology,
            rule.Id,
            rule.RiskLevel,
            match.Confidence,
            rule.CanDelete,
            rule.CanMove,
            rule.CanRegenerate,
            rule.OfficialMigrationMethod,
            rule.FallbackMigrationMethod);
    }

    /// <summary>Applies the classification onto the item itself, for persistence.</summary>
    public ClassificationResult Apply(ScanItem item)
    {
        var result = Classify(item);

        item.Category = result.Category;
        item.DetectedTechnology = result.Technology;
        item.KnownRuleId = result.RuleId;
        item.RiskLevel = result.RiskLevel;
        item.Confidence = result.Confidence;
        item.CanDelete = result.CanDelete;
        item.CanMove = result.CanMove;
        item.CanRegenerate = result.CanRegenerate;

        return result;
    }

    /// <summary>
    /// Best-effort categorisation for items no rule matched, based only on obvious signals.
    /// These stay non-actionable: an unrecognised item is never marked deletable or movable.
    /// </summary>
    private static ClassificationResult InferWithoutRule(ScanItem item)
    {
        if (item.ItemType == ItemType.File && item.Extension is { Length: > 0 } extension)
        {
            var category = extension.ToLowerInvariant() switch
            {
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".mp3" or ".flac" or ".wav"
                    => StorageCategory.Media,
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".heic" or ".raw" or ".psd"
                    => StorageCategory.Media,
                ".bak" or ".backup" or ".old"
                    => StorageCategory.Backups,
                ".log"
                    => StorageCategory.Logs,
                ".exe" or ".msi" or ".appx"
                    => StorageCategory.Applications,
                ".docx" or ".xlsx" or ".pptx" or ".pdf" or ".txt" or ".md"
                    => StorageCategory.PersonalFiles,
                _ => StorageCategory.Unknown
            };

            if (category != StorageCategory.Unknown)
            {
                return ClassificationResult.Unknown with
                {
                    Category = category,
                    RiskLevel = RiskLevel.Medium,
                    Confidence = 0.5
                };
            }
        }

        return ClassificationResult.Unknown;
    }
}
