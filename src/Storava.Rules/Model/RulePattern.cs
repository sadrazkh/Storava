using Storava.Domain.Enums;

namespace Storava.Rules.Model;

/// <summary>
/// A single detection pattern. Matching is plain string comparison (no regex) so the
/// catalog stays fast enough to run over millions of items.
/// </summary>
public sealed record RulePattern(
    string Value,
    RuleMatchTarget Target = RuleMatchTarget.Name,
    ItemType AppliesTo = ItemType.Folder)
{
    /// <summary>
    /// Longer, more qualified patterns win over generic ones so that, for example,
    /// "AppData\Local\pip\Cache" beats a bare "Cache".
    /// </summary>
    public int Specificity => Value.Count(c => c is '\\' or '/') * 100 + Value.Length;
}
