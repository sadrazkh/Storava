using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace Storava.App.Controls;

/// <summary>One slice of a <see cref="DonutChart"/>.</summary>
public sealed class DonutSlice
{
    public required string Label { get; init; }
    public required double Value { get; init; }
    public required Color Color { get; init; }
}

/// <summary>
/// A minimal donut chart drawn with arc geometry. Purpose-built rather than pulling in a
/// charting dependency, so it matches the app's design tokens exactly.
/// </summary>
public sealed class DonutChart : FrameworkElement
{
    private const double Thickness = 26;
    private const double GapDegrees = 1.5;

    private readonly List<DonutSlice> _slices = [];

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(DonutChart),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (DonutChart)d;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += chart.OnCollectionChanged;

        chart.Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        _slices.Clear();
        if (ItemsSource is not null)
        {
            foreach (var entry in ItemsSource)
            {
                if (entry is DonutSlice slice && slice.Value > 0)
                    _slices.Add(slice);
            }
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2 || _slices.Count == 0)
            return;

        double total = _slices.Sum(s => s.Value);
        if (total <= 0)
            return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = size / 2 - 2;
        double innerRadius = radius - Thickness;

        double startAngle = -90; // start at twelve o'clock
        foreach (var slice in _slices)
        {
            double sweep = slice.Value / total * 360;
            double drawSweep = Math.Max(0.1, sweep - (sweep > GapDegrees * 2 ? GapDegrees : 0));

            var geometry = BuildArc(center, radius, innerRadius, startAngle, drawSweep);
            var brush = new SolidColorBrush(slice.Color);
            brush.Freeze();
            context.DrawGeometry(brush, null, geometry);

            startAngle += sweep;
        }
    }

    private static Geometry BuildArc(Point center, double outer, double inner, double startAngle, double sweep)
    {
        // A full circle cannot be expressed as a single arc segment; draw it as two halves.
        if (sweep >= 359.9)
        {
            var full = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new EllipseGeometry(center, outer, outer),
                new EllipseGeometry(center, inner, inner));
            full.Freeze();
            return full;
        }

        double endAngle = startAngle + sweep;
        var outerStart = PointOnCircle(center, outer, startAngle);
        var outerEnd = PointOnCircle(center, outer, endAngle);
        var innerEnd = PointOnCircle(center, inner, endAngle);
        var innerStart = PointOnCircle(center, inner, startAngle);
        bool isLarge = sweep > 180;

        var figure = new PathFigure { StartPoint = outerStart, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(outer, outer), 0, isLarge, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(inner, inner), 0, isLarge, SweepDirection.Counterclockwise, true));

        var path = new PathGeometry();
        path.Figures.Add(figure);
        path.Freeze();
        return path;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
