using System.Windows;
using System.Windows.Media;

namespace Storava.App.Controls;

/// <summary>One rectangle in the treemap, laid out by <see cref="TreemapLayout"/>.</summary>
public sealed class TreemapItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required long Value { get; init; }

    /// <summary>Fill colour, chosen by the caller from category or risk.</summary>
    public required Color Color { get; init; }

    /// <summary>True when drilling into this node is possible.</summary>
    public bool CanDrillDown { get; init; }

    /// <summary>Extra line shown in the tooltip, e.g. the humanized size.</summary>
    public string? Detail { get; init; }

    /// <summary>Assigned during layout.</summary>
    public Rect Bounds { get; internal set; }
}
