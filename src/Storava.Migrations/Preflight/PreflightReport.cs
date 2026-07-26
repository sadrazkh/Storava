using Storava.Domain.Common;
using Storava.Domain.Entities;

namespace Storava.Migrations.Preflight;

/// <summary>
/// The verdict on one step, produced without touching anything. A step that fails preflight is
/// never offered for confirmation, so the user cannot approve something that was already known to
/// be impossible.
/// </summary>
public sealed record StepPreflight
{
    public required StoragePlanEntry Entry { get; init; }

    /// <summary>Why the step cannot run. <see cref="Error.None"/> when it can.</summary>
    public Error Blocker { get; init; } = Error.None;

    /// <summary>
    /// Things the user should know that do not stop the step — an item that grew since the scan,
    /// a move with no official mechanism behind it.
    /// </summary>
    public IReadOnlyList<Error> Warnings { get; init; } = [];

    /// <summary>What the folder measures now, which may differ from what the scan recorded.</summary>
    public long MeasuredBytes { get; init; }

    public bool CanRun => Blocker == Error.None;

    public bool HasWarnings => Warnings.Count > 0;
}

/// <summary>Preflight across a whole plan, in plan order.</summary>
public sealed record PreflightReport
{
    public required IReadOnlyList<StepPreflight> Steps { get; init; }

    public int RunnableCount => Steps.Count(s => s.CanRun);

    public int BlockedCount => Steps.Count(s => !s.CanRun);

    /// <summary>Space the runnable steps would free, measured now rather than at scan time.</summary>
    public long ReclaimableBytes => Steps.Where(s => s.CanRun).Sum(s => s.MeasuredBytes);

    public bool HasAnythingToDo => RunnableCount > 0;
}

/// <summary>Non-blocking findings raised by preflight.</summary>
public static class PreflightWarnings
{
    public static readonly Error GrewSinceScan =
        new("preflight.grew", "This folder is larger now than when it was scanned.");

    public static readonly Error ShrankSinceScan =
        new("preflight.shrank", "This folder is smaller now than when it was scanned.");

    public static readonly Error NoOfficialMethod =
        new("preflight.no_official_method", "No official relocation setting exists, so a link will be left behind.");

    public static readonly Error HighRisk =
        new("preflight.high_risk", "This item is marked high risk. Make sure nothing is using it.");

    public static readonly Error CoveredByAnotherStep =
        new("preflight.covered", "A folder above this one is also in the plan, so this step frees nothing extra.");
}
