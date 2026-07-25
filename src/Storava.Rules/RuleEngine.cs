using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Rules.Catalog;
using Storava.Rules.Model;
using System.IO;

namespace Storava.Rules;

/// <summary>
/// Matches scanned items against the rule catalog. Entirely local and deterministic — no AI
/// is involved in identifying what a folder is.
/// </summary>
public sealed class RuleEngine
{
    private readonly List<(StorageRule Rule, RulePattern Pattern)> _namePatterns = [];
    private readonly List<(StorageRule Rule, RulePattern Pattern)> _pathPatterns = [];

    public RuleEngine(IEnumerable<IRuleProvider> providers)
    {
        Rules = providers.SelectMany(p => p.GetRules()).ToArray();

        foreach (var rule in Rules)
        {
            foreach (var pattern in rule.Patterns)
            {
                if (pattern.Target == RuleMatchTarget.Name)
                    _namePatterns.Add((rule, pattern));
                else
                    _pathPatterns.Add((rule, pattern));
            }
        }

        // Most specific first, so a qualified path pattern always beats a bare folder name.
        _namePatterns.Sort((a, b) => b.Pattern.Specificity.CompareTo(a.Pattern.Specificity));
        _pathPatterns.Sort((a, b) => b.Pattern.Specificity.CompareTo(a.Pattern.Specificity));
    }

    public IReadOnlyList<StorageRule> Rules { get; }

    /// <summary>
    /// Finds the best matching rule for an item, or null when nothing in the catalog applies.
    /// Path patterns are considered before name patterns because they carry more context.
    /// </summary>
    public RuleMatch? Match(ScanItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        foreach (var (rule, pattern) in _pathPatterns)
        {
            if (!AppliesToType(pattern, item))
                continue;

            if (MatchesPath(pattern, item.Path))
                return new RuleMatch(rule, pattern);
        }

        foreach (var (rule, pattern) in _namePatterns)
        {
            if (!AppliesToType(pattern, item))
                continue;

            if (MatchesName(pattern, item))
                return new RuleMatch(rule, pattern);
        }

        return null;
    }

    private static bool AppliesToType(RulePattern pattern, ScanItem item) =>
        pattern.AppliesTo == item.ItemType;

    private static bool MatchesPath(RulePattern pattern, string path)
    {
        string needle = pattern.Value.Replace('/', '\\');

        if (pattern.Target == RuleMatchTarget.PathContains)
            return path.Contains(needle, StringComparison.OrdinalIgnoreCase);

        // PathSuffix: the path must end with the segment sequence, on a segment boundary.
        if (!path.EndsWith(needle, StringComparison.OrdinalIgnoreCase))
            return false;

        int start = path.Length - needle.Length;
        return start == 0 || path[start - 1] is '\\' or '/';
    }

    private static bool MatchesName(RulePattern pattern, ScanItem item)
    {
        // File patterns beginning with '.' are treated as extension matches.
        if (item.ItemType == ItemType.File && pattern.Value.StartsWith('.'))
        {
            return string.Equals(item.Extension, pattern.Value, StringComparison.OrdinalIgnoreCase)
                || item.Name.EndsWith(pattern.Value, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(item.Name, pattern.Value, StringComparison.OrdinalIgnoreCase);
    }
}
