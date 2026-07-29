using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Domain.ValueObjects;

namespace Storava.App.Models;

/// <summary>
/// One thing the user can act on, whether the rule catalog proposed it or they found it in the
/// scan themselves.
/// <para>
/// Both arrive here as the same row on purpose. The old design gave advice its own page and left
/// everything else unreachable, which meant the answer to "what about this 40 GB folder the
/// catalog has never heard of" was nothing at all. A row knows which it is — <see cref="IsSuggested"/>
/// — and says so, but the two are not separate features.
/// </para>
/// </summary>
public sealed partial class CleanupItemModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Only the actions permitted for this item are offered, so the picker cannot express
    /// something the plan would then refuse. For a row nothing recognised, both are offered —
    /// there is no rule to narrow them and the choice is the user's.
    /// </summary>
    [ObservableProperty] private PlanActionOption? _selectedAction;

    /// <summary>Set while the list is being rebuilt, so restoring state does not read as a choice.</summary>
    public bool SuppressNotifications { get; set; }

    private CleanupItemModel(
        string scanItemId,
        string path,
        string title,
        long size,
        RiskLevel risk,
        StorageCategory category,
        bool isFolder,
        bool canDelete,
        bool canMove,
        CultureInfo culture,
        ILocalizationService localization)
    {
        _localization = localization;

        ScanItemId = scanItemId;
        Path = path;
        Title = title;
        Size = size;
        Risk = risk;
        IsFolder = isFolder;
        CanDelete = canDelete;
        CanMove = canMove;

        SizeText = new ByteSize(size).Humanize(culture);
        CategoryText = localization[$"Str.Category.{category}"];
        RiskText = localization[$"Str.Risk.{risk}"];

        var options = new List<PlanActionOption>();
        if (canMove)
            options.Add(new PlanActionOption(SuggestedAction.Move, localization["Str.Plan.Action.Move"]));
        if (canDelete)
            options.Add(new PlanActionOption(SuggestedAction.Delete, localization["Str.Plan.Action.Delete"]));

        AvailableActions = options;
        // Moving first when both are possible: it is the one that keeps the data.
        _selectedAction = options.FirstOrDefault();
    }

    public string ScanItemId { get; }
    public string Path { get; }
    public string Title { get; }
    public long Size { get; }
    public RiskLevel Risk { get; }
    public bool IsFolder { get; }

    public string SizeText { get; }
    public string CategoryText { get; }
    public string RiskText { get; }

    /// <summary>The advice behind this row, when a rule produced it.</summary>
    public Recommendation? Advice { get; private init; }

    /// <summary>The scanned item behind this row, when the user found it themselves.</summary>
    public ScanItemView? Item { get; private init; }

    public bool IsSuggested => Advice is not null;

    /// <summary>Why the catalog proposed this. Empty for a row the user found on their own.</summary>
    public string Reason { get; private init; } = string.Empty;

    public bool HasReason => Reason.Length > 0;

    public bool CanDelete { get; }
    public bool CanMove { get; }

    public IReadOnlyList<PlanActionOption> AvailableActions { get; }

    /// <summary>The action itself, for the caller that has to build a plan step from this row.</summary>
    public SuggestedAction Action => SelectedAction?.Action ?? SuggestedAction.Delete;

    /// <summary>
    /// What this row says when there is no rule behind it. Shown instead of a reason, because the
    /// honest thing to tell someone about an unrecognised folder is that Storava knows its size
    /// and nothing else.
    /// </summary>
    public string UnknownNote => _localization["Str.Cleanup.NoRuleNote"];

    public static CleanupItemModel FromAdvice(
        Recommendation advice,
        CultureInfo culture,
        ILocalizationService localization) =>
        new(advice.ScanItemId, advice.Path, advice.Title, advice.EstimatedSpace,
            advice.RiskLevel, advice.Category, isFolder: true,
            advice.CanDelete, advice.CanMove, culture, localization)
        {
            Advice = advice,
            Reason = advice.Reason
        };

    public static CleanupItemModel FromItem(
        ScanItemView item,
        CultureInfo culture,
        ILocalizationService localization) =>
        // Both actions offered: nothing recognised this, so there is no rule to narrow it and the
        // choice is the user's. The guards that still apply live in the plan, not here.
        new(item.Id, item.Path, item.Name, item.Size,
            item.RiskLevel, item.Category, item.IsFolder,
            canDelete: true, canMove: true, culture, localization)
        {
            Item = item
        };
}

/// <summary>
/// One scan the cleanup page can be pointed at.
/// <para>
/// Imported scans are never offered. Their paths were measured on another machine, and one that
/// happens to exist here too would name a folder this scan never looked at — the one way a
/// confirmed step could act on something the user never saw.
/// </para>
/// </summary>
public sealed record CleanupScanOption(string SessionId, string Label)
{
    /// <summary>The label, because this is what a ComboBox falls back to.</summary>
    public override string ToString() => Label;
}

/// <summary>A step in the run, as the confirmation panel needs it.</summary>
public sealed record CleanupStepModel(
    string Title,
    string Path,
    string ActionText,
    string SizeText,
    string MethodText,
    bool IsMove,
    bool HasNoRule,
    string PositionText,
    string RequiredName);
