using System.Globalization;
using Storava.App.Models;
using Storava.Application.Abstractions;
using Storava.Application.Common;
using Storava.Application.Scanning;
using Storava.Domain.Enums;

namespace Storava.App.Tests;

/// <summary>
/// The row the cleanup list is built from. What matters here is the action picker: it is the only
/// place the user says what should happen, and a row that offers nothing is a row that cannot be
/// acted on however many times it is ticked.
/// </summary>
public class CleanupItemModelTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static ScanItemView Item(bool isFolder = true) => new(
        Id: "item-1",
        ParentId: null,
        Path: @"D:\Games\SomeGame",
        Name: "SomeGame",
        Extension: null,
        ItemType: isFolder ? ItemType.Folder : ItemType.File,
        Size: 40_000_000_000,
        AllocatedSize: 40_000_000_000,
        FileCount: 100,
        FolderCount: 10,
        Depth: 2,
        CreationTime: null,
        LastWriteTime: null,
        IsReparsePoint: false,
        IsProtected: false,
        IsHidden: false,
        IsSystem: false,
        RiskLevel: RiskLevel.Unknown,
        Category: StorageCategory.Unknown,
        DetectedTechnology: null,
        KnownRuleId: null,
        Confidence: 0,
        CanDelete: false,
        CanMove: false,
        CanRegenerate: false);

    [Fact]
    public void AnUnrecognisedItem_OffersEveryActionWithReadableLabels()
    {
        var model = CleanupItemModel.FromItem(Item(), Culture, new StubLocalization());

        Assert.All(model.AvailableActions, option => Assert.False(string.IsNullOrWhiteSpace(option.Label)));

        // Moving is offered first because it is the one that keeps the data.
        Assert.Equal(SuggestedAction.Move, model.AvailableActions[0].Action);
        Assert.Contains(SuggestedAction.Delete, model.AvailableActions.Select(option => option.Action));
    }

    /// <summary>
    /// The two ways to move are different outcomes, not a wording preference: one leaves a junction
    /// at the old path and one leaves nothing there. A picker that offered only one of them would
    /// be deciding that for the user.
    /// </summary>
    [Fact]
    public void AMovableItem_OffersBothWithAndWithoutAJunction()
    {
        var model = CleanupItemModel.FromItem(Item(), Culture, new StubLocalization());

        var moves = model.AvailableActions.Where(option => option.Action == SuggestedAction.Move).ToList();

        Assert.Equal(2, moves.Count);
        Assert.Contains(moves, option => option.Method == MigrationMethod.Junction);
        Assert.Contains(moves, option => option.Method == MigrationMethod.None);

        // Distinct labels, or the two are indistinguishable in the list.
        Assert.Equal(2, moves.Select(option => option.Label).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The junction comes first: it is the one that keeps everything working.</summary>
    [Fact]
    public void TheJunctionIsWhatARowStartsOn()
    {
        var model = CleanupItemModel.FromItem(Item(), Culture, new StubLocalization());

        Assert.Equal(MigrationMethod.Junction, model.Method);
    }

    /// <summary>A delete carries no mechanism, because there is nothing left to point at.</summary>
    [Fact]
    public void ChoosingDelete_CarriesNoMechanism()
    {
        var model = CleanupItemModel.FromItem(Item(), Culture, new StubLocalization());

        model.SelectedAction = model.AvailableActions
            .First(option => option.Action == SuggestedAction.Delete);

        Assert.Null(model.Method);
    }

    /// <summary>
    /// Selected in the constructor, not left null. A picker that starts empty makes every row need
    /// two interactions instead of one, and a tick with no action behind it plans nothing.
    /// </summary>
    [Fact]
    public void ARow_StartsWithAnActionAlreadyChosen()
    {
        var model = CleanupItemModel.FromItem(Item(), Culture, new StubLocalization());

        Assert.NotNull(model.SelectedAction);
        Assert.Equal(SuggestedAction.Move, model.Action);
    }

    [Fact]
    public void AFile_IsCarriedThroughAsAFile()
    {
        var model = CleanupItemModel.FromItem(Item(isFolder: false), Culture, new StubLocalization());

        Assert.False(model.IsFolder);
    }

    [Fact]
    public void AnUnrecognisedItem_SaysSoRatherThanShowingAnEmptyReason()
    {
        var model = CleanupItemModel.FromItem(Item(), Culture, new StubLocalization());

        Assert.False(model.IsSuggested);
        Assert.False(model.HasReason);
        Assert.False(string.IsNullOrWhiteSpace(model.UnknownNote));
    }

    /// <summary>Returns the key, exactly as the real service does when a resource is missing.</summary>
    private sealed class StubLocalization : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

        public bool IsRightToLeft => false;

        public event EventHandler? LanguageChanged;

        public string this[string key] => key;

        public void SetLanguage(AppLanguage language) => LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
