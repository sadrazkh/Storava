using System.Globalization;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Domain.ValueObjects;
using Storava.Migrations.Preflight;

namespace Storava.App.Models;

/// <summary>
/// One line of the dry run. It shows what the step would do and, when it cannot run, exactly why —
/// a blocked step is displayed rather than filtered out, so the plan the user drafted still adds up.
/// </summary>
public sealed class MigrationPreflightModel
{
    public MigrationPreflightModel(
        StepPreflight preflight,
        CultureInfo culture,
        ILocalizationService localization,
        Func<string, string> describeCode)
    {
        var entry = preflight.Entry;

        Order = entry.Order;
        Title = entry.Title;
        Path = entry.Path;
        ActionText = localization[$"Str.Plan.Action.{entry.Action}"];
        IsDelete = entry.Action == SuggestedAction.Delete;
        RiskText = localization[$"Str.Risk.{entry.RiskLevel}"];
        Risk = entry.RiskLevel;

        CanRun = preflight.CanRun;
        // The measured size, not the one recorded by the scan — it is what the step would free now.
        SizeText = new ByteSize(preflight.MeasuredBytes).Humanize(culture);
        BlockerText = preflight.CanRun ? null : describeCode(preflight.Blocker.Code);
        Warnings = preflight.Warnings.Select(w => describeCode(w.Code)).ToList();
        HasWarnings = Warnings.Count > 0;
    }

    public int Order { get; }
    public string Title { get; }
    public string Path { get; }
    public string ActionText { get; }
    public bool IsDelete { get; }
    public string RiskText { get; }
    /// <summary>
    /// The level itself rather than a colour for it. A brush built here would be frozen at the
    /// theme in force when the row was created, and would keep those colours when the user
    /// switched; the tag style resolves the palette instead, and follows.
    /// </summary>
    public RiskLevel Risk { get; }
    public string SizeText { get; }
    public bool CanRun { get; }
    public string? BlockerText { get; }
    public IReadOnlyList<string> Warnings { get; }
    public bool HasWarnings { get; }
}

/// <summary>
/// A finished step in the run log. This is the user's record of a change to their disk, so it
/// carries where the original went and where a link was left, not just a pass/fail mark.
/// </summary>
public sealed class MigrationLogModel
{
    public MigrationLogModel(
        PlanExecutionStep step,
        CultureInfo culture,
        ILocalizationService localization,
        Func<string, string> describeCode)
    {
        Order = step.Order;
        Title = step.Title;
        Path = step.SourcePath;
        DestinationPath = step.DestinationPath;
        HasDestination = !string.IsNullOrWhiteSpace(step.DestinationPath);
        ActionText = localization[$"Str.Plan.Action.{step.Action}"];
        StatusText = localization[$"Str.Migration.Status.{step.Status}"];
        Risk = RiskForStatus(step.Status);
        FreedText = new ByteSize(step.BytesFreed).Humanize(culture);
        WasCompleted = step.Status == ExecutionStatus.Completed;

        // A completed step that still carries an error means the move worked but the link did not.
        DetailText = string.IsNullOrWhiteSpace(step.ErrorCode) ? null : describeCode(step.ErrorCode);
        HasDetail = DetailText is not null;

        RecycledPath = step.RecycledPath;
        WasRecycled = !string.IsNullOrWhiteSpace(step.RecycledPath) && step.Status == ExecutionStatus.Completed;
        LinkPath = step.LinkPath;
        HasLink = !string.IsNullOrWhiteSpace(step.LinkPath);
    }

    public int Order { get; }
    public string Title { get; }
    public string Path { get; }
    public string? DestinationPath { get; }
    public bool HasDestination { get; }
    public string ActionText { get; }
    public string StatusText { get; }
    /// <summary>
    /// The outcome as a risk level, so one tag style colours every state. Named Risk, not
    /// StatusRisk, because the tag style binds this name — under any other one the trigger simply
    /// never fires and a failed step renders identically to a completed one.
    /// </summary>
    public RiskLevel Risk { get; }
    public string FreedText { get; }
    public bool WasCompleted { get; }
    public string? DetailText { get; }
    public bool HasDetail { get; }
    public string? RecycledPath { get; }
    public bool WasRecycled { get; }
    public string? LinkPath { get; }
    public bool HasLink { get; }

    private static RiskLevel RiskForStatus(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Completed => RiskLevel.Low,
        ExecutionStatus.Skipped => RiskLevel.Unknown,
        ExecutionStatus.RolledBack => RiskLevel.Medium,
        _ => RiskLevel.High
    };
}
