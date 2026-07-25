using Storava.Rules.Model;

namespace Storava.Rules;

/// <summary>The rule that best identifies an item, with the pattern that matched it.</summary>
public sealed record RuleMatch(StorageRule Rule, RulePattern Pattern)
{
    /// <summary>
    /// Confidence in this identification. Each rule states its own confidence, which already
    /// accounts for how ambiguous its patterns are (a generic "bin" is far less certain than
    /// a qualified ".nuget\packages"), so no extra adjustment is applied here.
    /// </summary>
    public double Confidence => Rule.Confidence;
}
