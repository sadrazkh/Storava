using System.Globalization;
using Storava.App.Models;
using Storava.App.Services;
using Storava.Application.Abstractions;
using Storava.Application.Common;
using Storava.Application.Scanning;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.App.Tests;

/// <summary>
/// Narrowing the cleanup list.
/// <para>
/// Three filters that each remove rows, and the failure they can produce together is a page that
/// silently shows nothing. The case that matters most is the one nobody sets on purpose: an empty
/// risk selection has to mean "no opinion", not "match nothing".
/// </para>
/// </summary>
public class CleanupFilterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly StubLocalization Localization = new();

    private static CleanupItemModel Chosen(string name, string path, RiskLevel risk) =>
        CleanupItemModel.FromItem(
            new ScanItemView(
                Id: name, ParentId: null, Path: path, Name: name, Extension: null,
                ItemType: ItemType.Folder, Size: 1024, AllocatedSize: 1024,
                FileCount: 1, FolderCount: 0, Depth: 1,
                CreationTime: null, LastWriteTime: null,
                IsReparsePoint: false, IsProtected: false, IsHidden: false, IsSystem: false,
                RiskLevel: risk, Category: StorageCategory.Unknown,
                DetectedTechnology: null, KnownRuleId: null, Confidence: 0,
                CanDelete: false, CanMove: false, CanRegenerate: false),
            Culture, Localization);

    private static CleanupItemModel Suggested(string name, string path, RiskLevel risk) =>
        CleanupItemModel.FromAdvice(
            new Recommendation
            {
                Id = $"rec-{name}",
                SessionId = "session-1",
                ScanItemId = name,
                Path = path,
                Title = name,
                Reason = "It is rebuilt on demand.",
                EstimatedSpace = 2048,
                RiskLevel = risk,
                CanDelete = true,
                CanMove = true
            },
            Culture, Localization);

    private static CleanupItemModel[] Sample() =>
    [
        Suggested("nuget", @"C:\Users\me\.nuget", RiskLevel.Low),
        Suggested("docker", @"C:\ProgramData\Docker", RiskLevel.Medium),
        Chosen("SomeGame", @"D:\Games\SomeGame", RiskLevel.Unknown),
        Chosen("Windows.old", @"C:\Windows.old", RiskLevel.High),
    ];

    private static IReadOnlySet<RiskLevel> Risks(params RiskLevel[] levels) => levels.ToHashSet();

    /// <summary>
    /// Narrowing to what is ticked.
    /// <para>
    /// The selection is what the page acts on and it was the one thing the list could not be
    /// narrowed to. It matters most when a run refuses one item: this is how it is found again
    /// among thousands in order to be taken back out.
    /// </para>
    /// </summary>
    [Fact]
    public void SelectedOnly_KeepsWhatIsTicked()
    {
        var items = Sample();
        items[1].IsSelected = true;
        items[3].IsSelected = true;

        var kept = CleanupFilter.Apply(items, suggestedOnly: false, search: null, Risks(), selectedOnly: true)
            .Select(item => item.Title)
            .ToList();

        Assert.Equal(["docker", "Windows.old"], kept);
    }

    /// <summary>Off, it must not narrow anything — a filter nobody set never empties a page.</summary>
    [Fact]
    public void SelectedOnly_Off_KeepsEverything()
    {
        var items = Sample();
        items[0].IsSelected = true;

        var kept = CleanupFilter.Apply(items, suggestedOnly: false, search: null, Risks(), selectedOnly: false);

        Assert.Equal(items.Length, kept.Count());
    }

    /// <summary>It narrows alongside the others rather than replacing them.</summary>
    [Fact]
    public void SelectedOnly_CombinesWithTheOtherFilters()
    {
        var items = Sample();
        foreach (var item in items)
            item.IsSelected = true;

        var kept = CleanupFilter.Apply(items, suggestedOnly: true, search: "docker", Risks(), selectedOnly: true)
            .Select(item => item.Title)
            .ToList();

        Assert.Equal(["docker"], kept);
    }

    /// <summary>Nothing ticked and the switch on shows nothing, which is the honest answer.</summary>
    [Fact]
    public void SelectedOnly_WithNothingTicked_ShowsNothing()
    {
        var kept = CleanupFilter.Apply(Sample(), suggestedOnly: false, search: null, Risks(), selectedOnly: true);

        Assert.Empty(kept);
    }

    /// <summary>The case nobody sets on purpose, and the one that would empty the page.</summary>
    [Fact]
    public void NoRiskChosen_KeepsEverything()
    {
        var kept = CleanupFilter.Apply(Sample(), suggestedOnly: false, search: null, Risks()).ToList();

        Assert.Equal(4, kept.Count);
    }

    [Fact]
    public void OneRiskChosen_KeepsOnlyThatRisk()
    {
        var kept = CleanupFilter.Apply(Sample(), suggestedOnly: false, search: null, Risks(RiskLevel.Low)).ToList();

        Assert.Single(kept);
        Assert.Equal("nuget", kept[0].Title);
    }

    /// <summary>Asking for low and medium means either, not both at once.</summary>
    [Fact]
    public void SeveralRisksChosen_KeepAnyOfThem()
    {
        var kept = CleanupFilter
            .Apply(Sample(), suggestedOnly: false, search: null, Risks(RiskLevel.Low, RiskLevel.Medium))
            .ToList();

        Assert.Equal(2, kept.Count);
        Assert.Contains(kept, item => item.Risk == RiskLevel.Low);
        Assert.Contains(kept, item => item.Risk == RiskLevel.Medium);
        Assert.DoesNotContain(kept, item => item.Risk == RiskLevel.High);
    }

    [Fact]
    public void SuggestedOnly_DropsWhatNoRuleRecognised()
    {
        var kept = CleanupFilter.Apply(Sample(), suggestedOnly: true, search: null, Risks()).ToList();

        Assert.Equal(2, kept.Count);
        Assert.All(kept, item => Assert.True(item.IsSuggested));
    }

    [Fact]
    public void Search_MatchesTheNameOrThePath()
    {
        var byName = CleanupFilter.Apply(Sample(), suggestedOnly: false, "docker", Risks()).ToList();
        var byPath = CleanupFilter.Apply(Sample(), suggestedOnly: false, @"D:\Games", Risks()).ToList();

        Assert.Single(byName);
        Assert.Single(byPath);
        Assert.Equal("SomeGame", byPath[0].Title);
    }

    [Fact]
    public void Search_IgnoresCaseAndSurroundingSpace()
    {
        var kept = CleanupFilter.Apply(Sample(), suggestedOnly: false, "  DOCKER  ", Risks()).ToList();

        Assert.Single(kept);
    }

    /// <summary>Each filter narrows what the previous one left, rather than replacing it.</summary>
    [Fact]
    public void TheFiltersCompose()
    {
        var kept = CleanupFilter
            .Apply(Sample(), suggestedOnly: true, search: "C:", Risks(RiskLevel.Low, RiskLevel.Medium))
            .ToList();

        Assert.Equal(2, kept.Count);

        // Adding a risk the remaining rows do not carry leaves nothing, which is the honest answer
        // to a question with no matches — as opposed to an empty risk set, which asks nothing.
        var none = CleanupFilter
            .Apply(Sample(), suggestedOnly: true, search: "C:", Risks(RiskLevel.High))
            .ToList();

        Assert.Empty(none);
    }

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
