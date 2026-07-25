using System.Windows;
using System.Windows.Media;
using Storava.App.Controls;

namespace Storava.App.Tests;

public class TreemapLayoutTests
{
    private static TreemapItem Tile(string id, long value) => new()
    {
        Id = id,
        Label = id,
        Value = value,
        Color = Colors.Teal
    };

    private static List<TreemapItem> Tiles(params long[] values) =>
        values.Select((v, i) => Tile($"t{i}", v)).OrderByDescending(t => t.Value).ToList();

    [Fact]
    public void Arrange_FillsTheAreaWithoutOverflowing()
    {
        var items = Tiles(500, 300, 150, 50);
        var bounds = new Rect(0, 0, 400, 300);

        TreemapLayout.Arrange(items, bounds);

        foreach (var item in items)
        {
            Assert.True(item.Bounds.Width >= 0 && item.Bounds.Height >= 0);
            Assert.True(item.Bounds.Left >= bounds.Left - 0.01, $"{item.Id} starts left of the area.");
            Assert.True(item.Bounds.Top >= bounds.Top - 0.01, $"{item.Id} starts above the area.");
            Assert.True(item.Bounds.Right <= bounds.Right + 0.01, $"{item.Id} overflows to the right.");
            Assert.True(item.Bounds.Bottom <= bounds.Bottom + 0.01, $"{item.Id} overflows at the bottom.");
        }
    }

    [Fact]
    public void Arrange_AreaIsProportionalToValue()
    {
        var items = Tiles(600, 300, 100);
        TreemapLayout.Arrange(items, new Rect(0, 0, 400, 400));

        double totalArea = items.Sum(i => i.Bounds.Width * i.Bounds.Height);
        Assert.True(totalArea > 400 * 400 * 0.9, $"Only {totalArea} of 160000 was used.");

        double first = items[0].Bounds.Width * items[0].Bounds.Height;
        double second = items[1].Bounds.Width * items[1].Bounds.Height;

        // 600 vs 300 should be roughly a 2:1 area ratio.
        Assert.InRange(first / second, 1.7, 2.3);
    }

    [Fact]
    public void Arrange_LargerValueGetsLargerArea()
    {
        var items = Tiles(1000, 500, 250, 125, 60);
        TreemapLayout.Arrange(items, new Rect(0, 0, 600, 400));

        for (int i = 1; i < items.Count; i++)
        {
            double previous = items[i - 1].Bounds.Width * items[i - 1].Bounds.Height;
            double current = items[i].Bounds.Width * items[i].Bounds.Height;
            Assert.True(previous >= current - 0.5, $"{items[i - 1].Id} should not be smaller than {items[i].Id}.");
        }
    }

    [Fact]
    public void Arrange_KeepsTilesReasonablySquare()
    {
        // The point of the squarified algorithm: avoid extreme slivers.
        var items = Tiles(400, 350, 300, 250, 200, 150, 100, 80, 60, 40);
        TreemapLayout.Arrange(items, new Rect(0, 0, 500, 400));

        foreach (var item in items.Where(i => i.Bounds.Width > 2 && i.Bounds.Height > 2))
        {
            double ratio = Math.Max(
                item.Bounds.Width / item.Bounds.Height,
                item.Bounds.Height / item.Bounds.Width);
            Assert.True(ratio < 12, $"{item.Id} has aspect ratio {ratio:F1}.");
        }
    }

    [Fact]
    public void Arrange_TilesDoNotOverlap()
    {
        var items = Tiles(500, 400, 300, 200, 100, 50);
        TreemapLayout.Arrange(items, new Rect(0, 0, 450, 350));

        for (int i = 0; i < items.Count; i++)
        {
            for (int j = i + 1; j < items.Count; j++)
            {
                var a = items[i].Bounds;
                var b = items[j].Bounds;
                if (a.Width <= 0.5 || a.Height <= 0.5 || b.Width <= 0.5 || b.Height <= 0.5)
                    continue;

                var overlap = Rect.Intersect(a, b);
                double overlapArea = overlap.IsEmpty ? 0 : overlap.Width * overlap.Height;
                Assert.True(overlapArea < 1.0,
                    $"{items[i].Id} and {items[j].Id} overlap by {overlapArea:F2}.");
            }
        }
    }

    [Fact]
    public void Arrange_HandlesSingleItem()
    {
        var items = Tiles(100);
        var bounds = new Rect(0, 0, 200, 150);

        TreemapLayout.Arrange(items, bounds);

        Assert.Equal(bounds.Width, items[0].Bounds.Width, precision: 3);
        Assert.Equal(bounds.Height, items[0].Bounds.Height, precision: 3);
    }

    [Fact]
    public void Arrange_HandlesEmptyInput()
    {
        var items = new List<TreemapItem>();

        TreemapLayout.Arrange(items, new Rect(0, 0, 100, 100));

        Assert.Empty(items);
    }

    [Fact]
    public void Arrange_ZeroSizedAreaProducesNoLayout()
    {
        var items = Tiles(100, 50);

        TreemapLayout.Arrange(items, new Rect(0, 0, 0, 0));

        Assert.All(items, i => Assert.True(i.Bounds.Width == 0 || i.Bounds.IsEmpty));
    }

    [Fact]
    public void Arrange_AllZeroValuesLeavesTilesEmpty()
    {
        var items = Tiles(0, 0, 0);

        TreemapLayout.Arrange(items, new Rect(0, 0, 100, 100));

        Assert.All(items, i => Assert.True(i.Bounds.IsEmpty));
    }

    [Fact]
    public void Arrange_HandlesManyTilesQuickly()
    {
        var items = Enumerable.Range(1, 500)
            .Select(i => Tile($"t{i}", 500 - i + 1))
            .ToList();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        TreemapLayout.Arrange(items, new Rect(0, 0, 1200, 800));
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 200, $"Layout took {stopwatch.ElapsedMilliseconds} ms.");
        Assert.All(items, i => Assert.True(i.Bounds.Right <= 1200.01 && i.Bounds.Bottom <= 800.01));
    }
}
