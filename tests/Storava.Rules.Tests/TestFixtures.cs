using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Rules;
using Storava.Rules.Catalog;
using Storava.Rules.Scoring;

namespace Storava.Rules.Tests;

/// <summary>Shared builders so each test states only what it actually cares about.</summary>
internal static class TestFixtures
{
    internal static RuleEngine Engine() => new([new BuiltInRuleProvider()]);

    internal static ClassificationService Classifier(IProtectedPathService? protectedPaths = null) =>
        new(Engine(), protectedPaths ?? new FakeProtectedPaths());

    internal static RecommendationBuilder Builder(IProtectedPathService? protectedPaths = null) =>
        new(Classifier(protectedPaths), new RecommendationScoreCalculator(), Engine());

    internal static ScanItem Folder(
        string path,
        long size = 1024L * 1024 * 1024,
        DateTimeOffset? lastWrite = null,
        bool isProtected = false,
        bool isSystem = false) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = "s",
            Path = path,
            Name = System.IO.Path.GetFileName(path.TrimEnd('\\')),
            ItemType = ItemType.Folder,
            Size = size,
            LastWriteTime = lastWrite ?? DateTimeOffset.UtcNow.AddDays(-200),
            IsProtected = isProtected,
            IsSystem = isSystem
        };

    internal static ScanItem File(
        string path,
        long size = 1024L * 1024 * 1024,
        DateTimeOffset? lastWrite = null) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = "s",
            Path = path,
            Name = System.IO.Path.GetFileName(path),
            Extension = System.IO.Path.GetExtension(path) is { Length: > 0 } e ? e : null,
            ItemType = ItemType.File,
            Size = size,
            LastWriteTime = lastWrite ?? DateTimeOffset.UtcNow.AddDays(-200)
        };
}

/// <summary>Treats anything under C:\Windows or C:\Program Files as protected.</summary>
internal sealed class FakeProtectedPaths : IProtectedPathService
{
    public IReadOnlyList<string> ProtectedRoots { get; } =
        [@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)"];

    public bool IsProtected(string path) => MatchingRoot(path) is not null;

    public string? MatchingRoot(string path) =>
        ProtectedRoots.FirstOrDefault(root =>
            path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));
}
