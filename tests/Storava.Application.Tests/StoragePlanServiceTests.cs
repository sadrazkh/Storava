using Microsoft.Extensions.Logging.Abstractions;
using Storava.Application.Abstractions;
using Storava.Application.Planning;
using Storava.Application.Scanning;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Application.Tests;

/// <summary>
/// Planning a step for something the user picked out of the scan themselves, rather than something
/// the rule catalog proposed.
/// <para>
/// This is the path that makes the feature reach a real disk. The catalog knows about three dozen
/// things; a developer's drive is mostly game installs, virtual machine images and video files it
/// has never heard of, and until now none of that could be planned at all. What it must not do is
/// become a way around the checks that do not depend on recognising the item.
/// </para>
/// </summary>
public class StoragePlanServiceTests
{
    private const string SessionId = "session-1";

    private readonly FakeProtectedPaths _protectedPaths = new();
    private readonly StoragePlanService _service;

    public StoragePlanServiceTests() =>
        _service = new StoragePlanService(
            new NotUsedPlanRepository(),
            new NotUsedRecommendationRepository(),
            _protectedPaths,
            NullLogger<StoragePlanService>.Instance);

    private static StoragePlan NewPlan() => new() { Id = "plan-1", SessionId = SessionId };

    private static ScanItemView Item(
        string path = @"D:\Games\SomeGame",
        string name = "SomeGame",
        long size = 40_000_000_000,
        bool isProtected = false,
        bool isReparsePoint = false,
        string? ruleId = null,
        bool canDelete = false,
        bool canMove = false,
        ItemType type = ItemType.Folder) => new(
            Id: "item-1",
            ParentId: null,
            Path: path,
            Name: name,
            Extension: null,
            ItemType: type,
            Size: size,
            AllocatedSize: size,
            FileCount: 100,
            FolderCount: 10,
            Depth: 2,
            CreationTime: null,
            LastWriteTime: null,
            IsReparsePoint: isReparsePoint,
            IsProtected: isProtected,
            IsHidden: false,
            IsSystem: false,
            RiskLevel: RiskLevel.Unknown,
            Category: StorageCategory.Unknown,
            DetectedTechnology: null,
            KnownRuleId: ruleId,
            Confidence: 0,
            CanDelete: canDelete,
            CanMove: canMove,
            CanRegenerate: false);

    [Theory]
    [InlineData(SuggestedAction.Delete)]
    [InlineData(SuggestedAction.Move)]
    public void Plans_AnItemNoRuleRecognises(SuggestedAction action)
    {
        var plan = NewPlan();

        var result = _service.Include(plan, Item(), SessionId, action);

        Assert.True(result.IsSuccess);
        Assert.Equal(@"D:\Games\SomeGame", result.Value.Path);
        Assert.True(result.Value.HasNoRule);
        Assert.Single(plan.Entries);
    }

    /// <summary>Two separate checks, and either one alone must be enough to stop the step.</summary>
    [Fact]
    public void Refuses_AnItemTheScanMarkedProtected()
    {
        var result = _service.Include(NewPlan(), Item(isProtected: true), SessionId, SuggestedAction.Delete);

        Assert.True(result.IsFailure);
        Assert.Equal(PlanErrors.ProtectedPath.Code, result.Error.Code);
    }

    [Fact]
    public void Refuses_AnItemUnderAProtectedRoot()
    {
        _protectedPaths.Roots.Add(@"C:\Windows");

        var result = _service.Include(
            NewPlan(),
            Item(path: @"C:\Windows\System32", name: "System32"),
            SessionId,
            SuggestedAction.Delete);

        Assert.True(result.IsFailure);
        Assert.Equal(PlanErrors.ProtectedPath.Code, result.Error.Code);
    }

    [Fact]
    public void Refuses_ALink()
    {
        var result = _service.Include(NewPlan(), Item(isReparsePoint: true), SessionId, SuggestedAction.Move);

        Assert.True(result.IsFailure);
        Assert.Equal(PlanErrors.IsLink.Code, result.Error.Code);
    }

    /// <summary>
    /// A recognised item's rule is knowledge the user does not have, so picking it by hand must not
    /// be a way past it. Only an item nothing recognised is decided by the user alone.
    /// </summary>
    [Fact]
    public void Refuses_WhatARuleForbids_EvenWhenPickedByHand()
    {
        var known = Item(ruleId: "npm.cache", canDelete: false, canMove: true);

        var result = _service.Include(NewPlan(), known, SessionId, SuggestedAction.Delete);

        Assert.True(result.IsFailure);
        Assert.Equal(PlanErrors.DeleteNotPermitted.Code, result.Error.Code);
    }

    [Fact]
    public void Refuses_AnItemFromADifferentScan()
    {
        var result = _service.Include(NewPlan(), Item(), "another-session", SuggestedAction.Delete);

        Assert.True(result.IsFailure);
        Assert.Equal(PlanErrors.WrongSession.Code, result.Error.Code);
    }

    [Fact]
    public void Plans_ASingleFile()
    {
        var plan = NewPlan();
        var file = Item(path: @"D:\media\raw.mkv", name: "raw.mkv", size: 8_000_000_000, type: ItemType.File);

        var result = _service.Include(plan, file, SessionId, SuggestedAction.Move);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsFolder);
    }

    private sealed class FakeProtectedPaths : IProtectedPathService
    {
        public HashSet<string> Roots { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> ProtectedRoots => [.. Roots];

        public bool IsProtected(string path) =>
            Roots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Neither repository is reached: nothing here loads or saves, it only decides.</summary>
    private sealed class NotUsedPlanRepository : IStoragePlanRepository
    {
        public Task<StoragePlan?> GetForSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(StoragePlan plan, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteForSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NotUsedRecommendationRepository : IRecommendationRepository
    {
        public Task<IReadOnlyList<Recommendation>> GetBySessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReplaceForSessionAsync(
            string sessionId,
            IEnumerable<Recommendation> recommendations,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReplaceAiAdviceAsync(
            string sessionId,
            IEnumerable<Recommendation> recommendations,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
