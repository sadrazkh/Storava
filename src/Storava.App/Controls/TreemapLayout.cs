using System.Windows;

namespace Storava.App.Controls;

/// <summary>
/// Squarified treemap layout: lays items out so rectangles stay close to square, which makes
/// relative areas much easier to compare than a naive slice-and-dice arrangement.
/// Implements the Bruls/Huizing/van Wijk approach.
/// </summary>
internal static class TreemapLayout
{
    /// <summary>
    /// Assigns <see cref="TreemapItem.Bounds"/> for each item within <paramref name="bounds"/>.
    /// Items must be sorted by value descending for good results.
    /// </summary>
    public static void Arrange(IReadOnlyList<TreemapItem> items, Rect bounds)
    {
        if (items.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        long total = items.Sum(i => Math.Max(0, i.Value));
        if (total <= 0)
        {
            foreach (var item in items)
                item.Bounds = Rect.Empty;
            return;
        }

        double scale = bounds.Width * bounds.Height / total;
        Squarify(items, 0, items.Count, bounds, scale);
    }

    private static void Squarify(IReadOnlyList<TreemapItem> items, int start, int end, Rect area, double scale)
    {
        while (start < end)
        {
            if (area.Width <= 0 || area.Height <= 0)
            {
                for (int i = start; i < end; i++)
                    items[i].Bounds = Rect.Empty;
                return;
            }

            bool horizontal = area.Width >= area.Height;
            double shortSide = horizontal ? area.Height : area.Width;

            // Grow the row while doing so improves (lowers) the worst aspect ratio.
            int count = 0;
            double rowArea = 0;
            double bestRatio = double.MaxValue;

            for (int i = start; i < end; i++)
            {
                double candidateArea = rowArea + Math.Max(0, items[i].Value) * scale;
                if (candidateArea <= 0)
                {
                    count++;
                    continue;
                }

                double ratio = WorstAspectRatio(items, start, i + 1, candidateArea, shortSide, scale);
                if (ratio > bestRatio)
                    break;

                bestRatio = ratio;
                rowArea = candidateArea;
                count = i - start + 1;
            }

            if (count == 0)
                count = 1;
            if (rowArea <= 0)
                rowArea = Math.Max(0, items[start].Value) * scale;

            double rowThickness = shortSide > 0 ? rowArea / shortSide : 0;
            LayoutRow(items, start, start + count, area, rowThickness, horizontal, scale);

            area = horizontal
                ? new Rect(area.X + rowThickness, area.Y, Math.Max(0, area.Width - rowThickness), area.Height)
                : new Rect(area.X, area.Y + rowThickness, area.Width, Math.Max(0, area.Height - rowThickness));

            start += count;
        }
    }

    private static void LayoutRow(
        IReadOnlyList<TreemapItem> items, int start, int end,
        Rect area, double thickness, bool horizontal, double scale)
    {
        double offset = 0;
        double rowLength = horizontal ? area.Height : area.Width;

        for (int i = start; i < end; i++)
        {
            double itemArea = Math.Max(0, items[i].Value) * scale;
            double length = thickness > 0 ? itemArea / thickness : 0;

            // Never overflow the row because of accumulated rounding.
            length = Math.Min(length, Math.Max(0, rowLength - offset));

            items[i].Bounds = horizontal
                ? new Rect(area.X, area.Y + offset, Math.Min(thickness, area.Width), length)
                : new Rect(area.X + offset, area.Y, length, Math.Min(thickness, area.Height));

            offset += length;
        }
    }

    private static double WorstAspectRatio(
        IReadOnlyList<TreemapItem> items, int start, int end,
        double rowArea, double shortSide, double scale)
    {
        if (rowArea <= 0 || shortSide <= 0)
            return double.MaxValue;

        double thickness = rowArea / shortSide;
        double worst = 1;

        for (int i = start; i < end; i++)
        {
            double itemArea = Math.Max(0, items[i].Value) * scale;
            if (itemArea <= 0)
                continue;

            double length = itemArea / thickness;
            double ratio = Math.Max(thickness / length, length / thickness);
            worst = Math.Max(worst, ratio);
        }

        return worst;
    }
}
