using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Domain.ValueObjects;

namespace Storava.App.Models;

/// <summary>One choosable action, with the label shown in the picker.</summary>
public sealed record PlanActionOption(SuggestedAction Action, string Label);

/// <summary>
/// A recommendation offered for inclusion in the plan. Only the actions the local rules already
/// permit for the item appear in <see cref="AvailableActions"/>, so the UI cannot even express
/// something the domain would reject.
/// </summary>
public sealed partial class PlanCandidateModel : ObservableObject
{
    public PlanCandidateModel(
        Recommendation recommendation,
        CultureInfo culture,
        ILocalizationService localization)
    {
        Source = recommendation;

        Title = recommendation.Title;
        Path = recommendation.Path;
        Reason = recommendation.Reason;
        Warning = recommendation.Warning;
        OfficialMigrationHint = recommendation.OfficialMigrationHint;
        HasWarning = !string.IsNullOrWhiteSpace(recommendation.Warning);

        SizeText = new ByteSize(recommendation.EstimatedSpace).Humanize(culture);
        CategoryText = localization[$"Str.Category.{recommendation.Category}"];
        RiskText = localization[$"Str.Risk.{recommendation.RiskLevel}"];
        RiskBrush = CategoryPalette.BrushForRisk(recommendation.RiskLevel);

        var options = new List<PlanActionOption>();
        if (recommendation.CanMove)
            options.Add(new PlanActionOption(SuggestedAction.Move, localization["Str.Plan.Action.Move"]));
        if (recommendation.CanDelete)
            options.Add(new PlanActionOption(SuggestedAction.Delete, localization["Str.Plan.Action.Delete"]));

        AvailableActions = options;
        // Moving is preferred when both are possible: it is the reversible one.
        _selectedAction = options.FirstOrDefault();
        CanBePlanned = options.Count > 0;
    }

    public Recommendation Source { get; }

    public string ScanItemId => Source.ScanItemId;

    public string Title { get; }
    public string Path { get; }
    public string Reason { get; }
    public string? Warning { get; }
    public string? OfficialMigrationHint { get; }
    public bool HasWarning { get; }

    public string SizeText { get; }
    public string CategoryText { get; }
    public string RiskText { get; }
    public Brush RiskBrush { get; }

    public IReadOnlyList<PlanActionOption> AvailableActions { get; }

    /// <summary>False when the rules permit neither moving nor deleting; the row is read-only then.</summary>
    public bool CanBePlanned { get; }

    [ObservableProperty] private bool _isIncluded;
    [ObservableProperty] private PlanActionOption? _selectedAction;

    /// <summary>Set without notifying the plan, when the view model is syncing from a saved plan.</summary>
    public bool SuppressNotifications { get; set; }
}

/// <summary>One step of the ordered plan preview.</summary>
public sealed class PlanStepModel
{
    public PlanStepModel(
        StoragePlanEntry entry,
        CultureInfo culture,
        ILocalizationService localization)
    {
        Order = entry.Order;
        Title = entry.Title;
        Path = entry.Path;
        ActionText = localization[$"Str.Plan.Action.{entry.Action}"];
        IsDelete = entry.Action == SuggestedAction.Delete;
        SizeText = new ByteSize(entry.EstimatedSpace).Humanize(culture);
        RiskText = localization[$"Str.Risk.{entry.RiskLevel}"];
        RiskBrush = CategoryPalette.BrushForRisk(entry.RiskLevel);
        MethodHint = entry.MethodHint;
        Warning = entry.Warning;
        HasWarning = !string.IsNullOrWhiteSpace(entry.Warning);

        IsCovered = entry.IsCovered;
        HasOfficialMethod = entry.Method == MigrationMethod.OfficialSetting;
        // A junction or symlink is a fallback, and the user should know before committing to it.
        NeedsLink = entry.Method is MigrationMethod.Junction or MigrationMethod.SymbolicLink;
    }

    public int Order { get; }
    public string Title { get; }
    public string Path { get; }
    public string ActionText { get; }
    public bool IsDelete { get; }
    public string SizeText { get; }
    public string RiskText { get; }
    public Brush RiskBrush { get; }
    public string? MethodHint { get; }
    public string? Warning { get; }
    public bool HasWarning { get; }
    public bool IsCovered { get; }
    public bool HasOfficialMethod { get; }
    public bool NeedsLink { get; }
}
