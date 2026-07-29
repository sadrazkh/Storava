using System.Security.Cryptography;
using System.Text;
using Storava.Domain.Entities;

namespace Storava.Migrations;

/// <summary>
/// Binds one approval to a whole plan.
/// <para>
/// A single step is approved by typing the folder's own name, which works because there is one
/// folder and its name is on screen. That gate does nothing for a plan: typing one name out of
/// twelve says nothing about the other eleven, and picking one of them to stand for the rest would
/// be arbitrary.
/// </para>
/// <para>
/// So the plan is approved by a short code derived from every step in it. It cannot be typed from
/// memory — it has to be read off the panel listing exactly what will happen — and it changes if
/// anything about the plan changes: a folder added or removed, a destination edited, a move
/// switched between a junction and a plain one. An approval therefore cannot be spent on a set
/// other than the one that was read.
/// </para>
/// </summary>
public static class PlanConfirmation
{
    /// <summary>
    /// Six characters, drawn from an alphabet with no pairs that look alike in a sans-serif font.
    /// <para>
    /// I, l and 1 are missing, as are O and 0, S and 5, Z and 2. Somebody copying a code by eye
    /// should not be able to fail at it, because a mistyped code reads as a refused approval and
    /// the natural response to that is to try harder rather than to look for the real problem.
    /// </para>
    /// </summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRTUVWXY346789";

    private const int Length = 6;

    /// <summary>
    /// A fingerprint of the whole plan: every step's own fingerprint, in order, hashed together.
    /// <para>
    /// Order is part of it. Two plans over the same folders in a different order are different
    /// plans — a move that frees a drive before another move fills it is not the same as the
    /// reverse — so an approval for one is not an approval for the other.
    /// </para>
    /// </summary>
    public static string ComputeFingerprint(IEnumerable<PlanExecutionStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var material = string.Join('\n', steps.Select(StepConfirmation.Compute));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>The code the user types, derived from the fingerprint so it moves with the plan.</summary>
    public static string ComputePhrase(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
        var phrase = new StringBuilder(Length);

        for (var index = 0; index < Length; index++)
            phrase.Append(Alphabet[digest[index] % Alphabet.Length]);

        return phrase.ToString();
    }

    /// <summary>
    /// Whether what the user typed approves this plan.
    /// <para>
    /// Case is ignored, because the code is read off a screen and retyped, and refusing it over a
    /// shift key would teach nothing. Surrounding space is ignored for the same reason.
    /// </para>
    /// </summary>
    public static bool Matches(string fingerprint, string? typed) =>
        !string.IsNullOrWhiteSpace(typed)
        && string.Equals(typed.Trim(), ComputePhrase(fingerprint), StringComparison.OrdinalIgnoreCase);
}
