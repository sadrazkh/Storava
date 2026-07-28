using System.Globalization;
using System.Windows.Media;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.History;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Domain.ValueObjects;

namespace Storava.App.Models;

/// <summary>One past scan in the history list, and the unit the user picks when comparing.</summary>
public sealed class ScanHistoryModel
{
    public ScanHistoryModel(ScanSession session, CultureInfo culture, ILocalizationService localization)
    {
        Session = session;
        Id = session.Id;
        RootPath = session.RootPath;
        Label = string.IsNullOrWhiteSpace(session.Label) ? session.RootPath : session.Label!;

        // Explicit culture, never the ambient thread culture: the Persian calendar and digits have
        // to follow the app's language setting rather than the OS.
        StartedText = session.StartedAt.ToString("g", culture);
        SizeText = new ByteSize(session.TotalSize).Humanize(culture);
        FilesText = session.TotalFiles.ToString("N0", culture);
        FoldersText = session.TotalFolders.ToString("N0", culture);
        ModeText = localization[$"Str.ScanMode.{session.Mode}"];
        StatusText = localization[$"Str.ScanStatus.{session.Status}"];
        IsCompleted = session.Status == ScanStatus.Completed;

        DurationText = session.Duration is { } duration
            ? duration.ToString(duration.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture)
            : "—";

        HasErrors = session.ErrorCount > 0;
        ErrorsText = session.ErrorCount.ToString("N0", culture);

        // A scan that stopped early and recorded where it got to can be carried on.
        CanResume = session.CanResume;

        // An imported scan describes a disk that may not be this one, so the list says where it
        // came from rather than presenting it as something Storava measured here.
        IsImported = session.IsImported;
        SourceText = session.IsImported
            ? string.Format(
                culture,
                localization["Str.Archive.From"],
                session.SourceLabel ?? localization["Str.Archive.UnknownSource"])
            : string.Empty;
    }

    public ScanSession Session { get; }

    public string Id { get; }
    public string RootPath { get; }
    public string Label { get; }
    public string StartedText { get; }
    public string SizeText { get; }
    public string FilesText { get; }
    public string FoldersText { get; }
    public string ModeText { get; }
    public string StatusText { get; }
    public bool IsCompleted { get; }
    public string DurationText { get; }
    public bool HasErrors { get; }
    public string ErrorsText { get; }

    /// <summary>This scan stopped early and recorded enough to be carried on.</summary>
    public bool CanResume { get; }

    /// <summary>This scan arrived in a <c>.storava</c> file rather than being measured here.</summary>
    public bool IsImported { get; }

    /// <summary>The archive an imported scan came from; empty for a local scan.</summary>
    public string SourceText { get; }

    /// <summary>Shown in the two comparison pickers, where the date is what tells them apart.</summary>
    public string PickerText => $"{StartedText} — {SizeText}";

    /// <summary>
    /// This scan's size as a fraction of the largest in the trend, so the bars are comparable.
    /// Assigned by the view model once the whole series is known.
    /// </summary>
    public double TrendFraction { get; set; }
}

/// <summary>One folder's movement between two scans.</summary>
public sealed class FolderChangeModel
{
    public FolderChangeModel(FolderChange change, CultureInfo culture, ILocalizationService localization)
    {
        Path = change.Path;
        Name = change.Name;
        KindText = localization[$"Str.History.Change.{change.Kind}"];
        CategoryText = localization[$"Str.Category.{change.Category}"];
        Grew = change.Delta > 0;
        IsNested = change.HasChangedAncestor;

        BeforeText = new ByteSize(change.BaselineBytes).Humanize(culture);
        AfterText = new ByteSize(change.CurrentBytes).Humanize(culture);

        // The sign is carried by the label, not by a negative byte count, which would read oddly.
        DeltaText = (change.Delta > 0 ? "+" : "−") + new ByteSize(Math.Abs(change.Delta)).Humanize(culture);
        Risk = change.Delta > 0 ? RiskLevel.High : RiskLevel.Low;
    }

    public string Path { get; }
    public string Name { get; }
    public string KindText { get; }
    public string CategoryText { get; }
    public bool Grew { get; }
    public bool IsNested { get; }
    public string BeforeText { get; }
    public string AfterText { get; }
    public string DeltaText { get; }
    /// <summary>Growth reads as high risk and shrinkage as low, so the tag colours itself.</summary>
    public RiskLevel Risk { get; }
}

/// <summary>A category's movement between two scans.</summary>
public sealed class CategoryChangeModel
{
    public CategoryChangeModel(CategoryChange change, CultureInfo culture, ILocalizationService localization)
    {
        CategoryText = localization[$"Str.Category.{change.Category}"];
        CategoryBrush = new SolidColorBrush(CategoryPalette.ForCategory(change.Category));
        ((SolidColorBrush)CategoryBrush).Freeze();

        DeltaText = (change.Delta > 0 ? "+" : "−") + new ByteSize(Math.Abs(change.Delta)).Humanize(culture);
        AfterText = new ByteSize(change.CurrentBytes).Humanize(culture);
        Grew = change.Delta > 0;
    }

    public string CategoryText { get; }
    public Brush CategoryBrush { get; }
    public string DeltaText { get; }
    public string AfterText { get; }
    public bool Grew { get; }
}

/// <summary>One past run of a plan, summarised for the audit list.</summary>
public sealed class ExecutionHistoryModel
{
    public ExecutionHistoryModel(PlanExecution execution, CultureInfo culture, ILocalizationService localization)
    {
        StartedText = execution.StartedAt.ToString("g", culture);
        FreedText = new ByteSize(execution.TotalBytesFreed).Humanize(culture);
        CompletedText = execution.CompletedCount.ToString(culture);
        FailedText = execution.FailedCount.ToString(culture);
        SkippedText = execution.SkippedCount.ToString(culture);
        IsFinished = execution.IsFinished;
        StatusText = localization[execution.IsFinished ? "Str.History.Run.Finished" : "Str.History.Run.Unfinished"];

        Steps = execution.Steps
            .OrderBy(s => s.Order)
            .Select(s => new ExecutionStepSummary(
                s.Title,
                s.SourcePath,
                localization[$"Str.Plan.Action.{s.Action}"],
                localization[$"Str.Migration.Status.{s.Status}"],
                s.Status == ExecutionStatus.Completed))
            .ToList();
    }

    public string StartedText { get; }
    public string FreedText { get; }
    public string CompletedText { get; }
    public string FailedText { get; }
    public string SkippedText { get; }
    public bool IsFinished { get; }
    public string StatusText { get; }
    public IReadOnlyList<ExecutionStepSummary> Steps { get; }
}

public sealed record ExecutionStepSummary(
    string Title,
    string Path,
    string ActionText,
    string StatusText,
    bool WasCompleted);
