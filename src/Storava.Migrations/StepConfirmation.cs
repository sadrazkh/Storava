using System.Security.Cryptography;
using System.Text;
using Storava.Domain.Entities;

namespace Storava.Migrations;

/// <summary>
/// Proof that the user approved one specific step, exactly as it stood when they read it.
/// <para>
/// The same idea as the AI consent token: the approval is bound to a fingerprint of what was on
/// screen, so changing the destination — or the folder changing underneath — invalidates it rather
/// than silently carrying over. An approval can therefore never be reused for a different act.
/// </para>
/// </summary>
public sealed record StepConfirmation
{
    public required string StepId { get; init; }

    /// <summary>Fingerprint of the step as the user saw it. See <see cref="Compute"/>.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>What the user typed to confirm. Must be the folder's own name.</summary>
    public required string TypedName { get; init; }

    public DateTimeOffset GrantedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// Everything about a step that changes what it would do to the disk. Nothing cosmetic goes in
    /// here, so a language switch or a re-render does not invalidate a confirmation the user gave.
    /// </summary>
    public static string Compute(PlanExecutionStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        var material = string.Join(
            '\n',
            step.Id,
            step.ScanItemId,
            Normalize(step.SourcePath),
            Normalize(step.DestinationPath),
            ((int)step.Action).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)step.Method).ToString(System.Globalization.CultureInfo.InvariantCulture));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static string Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().TrimEnd('\\', '/').ToUpperInvariant();
}
