using Storava.Rules.Model;

namespace Storava.Rules.Catalog;

/// <summary>Supplies detection rules. Additional providers can be plugged in later.</summary>
public interface IRuleProvider
{
    IReadOnlyList<StorageRule> GetRules();
}

/// <summary>
/// The built-in rule catalog. Rules are grouped by domain across several files to keep each
/// list readable, and validated for duplicate ids on first use.
/// </summary>
public sealed class BuiltInRuleProvider : IRuleProvider
{
    private readonly Lazy<IReadOnlyList<StorageRule>> _rules = new(Build);

    public IReadOnlyList<StorageRule> GetRules() => _rules.Value;

    private static IReadOnlyList<StorageRule> Build()
    {
        var rules = DeveloperCacheRules.All()
            .Concat(PlatformRules.All())
            .Concat(SystemRules.All())
            .ToArray();

        var duplicate = rules.GroupBy(r => r.Id, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate rule id in catalog: {duplicate.Key}");

        return rules;
    }
}
