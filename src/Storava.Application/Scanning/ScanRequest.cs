using Storava.Domain.Enums;

namespace Storava.Application.Scanning;

/// <summary>Describes a scan to run: what to scan, how, and what to skip.</summary>
public sealed record ScanRequest
{
    public required string RootPath { get; init; }
    public ScanMode Mode { get; init; } = ScanMode.Quick;

    /// <summary>Full paths to skip entirely (and not descend into).</summary>
    public IReadOnlyCollection<string> ExcludedPaths { get; init; } = [];

    /// <summary>File extensions to skip, each including the leading dot (e.g. ".tmp").</summary>
    public IReadOnlyCollection<string> ExcludedExtensions { get; init; } = [];

    public string? Label { get; init; }
}
