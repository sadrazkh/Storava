using Storava.Domain.Enums;

namespace Storava.Rules.Model;

/// <summary>
/// A detection rule for a known storage consumer. Rules are pure data plus matching
/// patterns, which keeps the catalog extensible and independent of any AI involvement.
/// </summary>
public sealed class StorageRule
{
    public required string Id { get; init; }

    /// <summary>Localized titles keyed by language, e.g. "en" and "fa".</summary>
    public required IReadOnlyDictionary<string, string> Titles { get; init; }

    /// <summary>Localized explanations of what the folder is and why it grows.</summary>
    public required IReadOnlyDictionary<string, string> Descriptions { get; init; }

    public required IReadOnlyList<RulePattern> Patterns { get; init; }

    public required StorageCategory Category { get; init; }

    /// <summary>Human-facing technology name, e.g. "NuGet", "npm", "Docker".</summary>
    public string? Technology { get; init; }

    public RiskLevel RiskLevel { get; init; } = RiskLevel.Medium;

    public bool CanDelete { get; init; }
    public bool CanMove { get; init; }

    /// <summary>True when the tool rebuilds the content automatically after removal.</summary>
    public bool CanRegenerate { get; init; }

    public MigrationMethod OfficialMigrationMethod { get; init; } = MigrationMethod.None;
    public MigrationMethod FallbackMigrationMethod { get; init; } = MigrationMethod.None;

    /// <summary>
    /// How to relocate it officially (setting name, environment variable, CLI command).
    /// Shown to the user as guidance; Storava never executes it on its own.
    /// </summary>
    public string? OfficialMigrationHint { get; init; }

    /// <summary>Localized warnings the user must see before acting.</summary>
    public IReadOnlyDictionary<string, string> Warnings { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Confidence that a pattern match really identifies this consumer.</summary>
    public double Confidence { get; init; } = 0.9;

    /// <summary>Highest specificity among this rule's patterns, used to break ties.</summary>
    public int Specificity => Patterns.Count == 0 ? 0 : Patterns.Max(p => p.Specificity);

    public string GetTitle(string language) => Lookup(Titles, language, Id);
    public string GetDescription(string language) => Lookup(Descriptions, language, string.Empty);
    public string? GetWarning(string language) =>
        Warnings.Count == 0 ? null : Lookup(Warnings, language, string.Empty);

    private static string Lookup(IReadOnlyDictionary<string, string> map, string language, string fallback)
    {
        if (map.TryGetValue(language, out var value))
            return value;
        return map.TryGetValue("en", out var english) ? english : fallback;
    }
}
