using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Storava.App.Controls;

/// <summary>
/// A squarified treemap drawn directly onto the drawing context. Rendering hundreds of
/// rectangles this way avoids creating a visual per item, which keeps interaction smooth on
/// large scans. Hover highlights, tooltips and click-to-drill-down are handled internally.
/// </summary>
public sealed class TreemapControl : FrameworkElement
{

    /// <summary>
    /// Which way a label's text runs, from the culture rather than from the control.
    /// <para>
    /// The control itself is pinned left-to-right so its geometry is not mirrored; that says
    /// nothing about the text drawn on it, which still has to follow the language.
    /// </para>
    /// </summary>
    private static FlowDirection LabelFlow =>
        CultureInfo.CurrentCulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

    private const double MinLabelWidth = 54;
    private const double MinLabelHeight = 22;

    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly Pen GapPen = CreateGapPen();
    private static readonly Pen HoverPen = CreateHoverPen();

    private readonly List<TreemapItem> _layoutItems = [];
    private TreemapItem? _hovered;
    private readonly ToolTip _toolTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };

    public TreemapControl()
    {
        ToolTip = _toolTip;
        ToolTipService.SetInitialShowDelay(this, 150);
        ToolTipService.SetShowDuration(this, 30000);
        ClipToBounds = true;

        // Pinned left-to-right whatever the shell is using. Right-to-left is implemented as a
        // mirror transform that descendants inherit, which is correct for laid-out content and
        // wrong for drawn geometry: under Persian the tiles came out mirror-imaged. A treemap is
        // anchored to its numbers, not to reading order.
        FlowDirection = FlowDirection.LeftToRight;
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(TreemapControl),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Raised when the user activates an item that supports drilling down.</summary>
    public event EventHandler<TreemapItem>? ItemActivated;

    /// <summary>Raised when the user selects an item (single click).</summary>
    public event EventHandler<TreemapItem>? ItemSelected;

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TreemapControl)d;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= control.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += control.OnCollectionChanged;

        control.Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        _layoutItems.Clear();
        _hovered = null;

        if (ItemsSource is not null)
        {
            foreach (var entry in ItemsSource)
            {
                if (entry is TreemapItem item && item.Value > 0)
                    _layoutItems.Add(item);
            }
            _layoutItems.Sort((a, b) => b.Value.CompareTo(a.Value));
        }

        ArrangeItems();
        InvalidateVisual();
    }

    private void ArrangeItems()
    {
        if (ActualWidth > 0 && ActualHeight > 0)
            TreemapLayout.Arrange(_layoutItems, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        ArrangeItems();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        // A transparent background makes the whole surface hit-testable for hover/click.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        foreach (var item in _layoutItems)
        {
            var bounds = item.Bounds;
            if (bounds.Width <= 1 || bounds.Height <= 1)
                continue;

            var fill = new SolidColorBrush(item.Color);
            bool isHovered = ReferenceEquals(item, _hovered);
            if (isHovered)
                fill.Opacity = 0.85;

            context.DrawRectangle(fill, isHovered ? HoverPen : GapPen, bounds);

            if (bounds.Width >= MinLabelWidth && bounds.Height >= MinLabelHeight)
                DrawLabel(context, item, bounds);
        }
    }

    private static void DrawLabel(DrawingContext context, TreemapItem item, Rect bounds)
    {
        // Dark or light text depending on the tile colour, so labels stay readable.
        var brush = IsLightColor(item.Color) ? Brushes.Black : Brushes.White;

        var text = new FormattedText(
            item.Label,
            CultureInfo.CurrentCulture,
            // The tile is not mirrored, but its label still has to read the right way round: a
            // Persian name laid out left-to-right renders its words in the wrong order.
            LabelFlow,
            LabelTypeface,
            12,
            brush,
            VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, bounds.Width - 10),
            MaxTextHeight = Math.Max(1, bounds.Height - 6),
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 1
        };

        context.DrawText(text, new Point(bounds.X + 5, bounds.Y + 4));

        // Show the size underneath when there is enough room.
        if (item.Detail is { Length: > 0 } detail && bounds.Height >= 40)
        {
            var detailBrush = brush.Clone();
            detailBrush.Opacity = 0.85;
            detailBrush.Freeze();

            var detailText = new FormattedText(
                detail,
                CultureInfo.CurrentCulture,
                LabelFlow,
                LabelTypeface,
                11,
                detailBrush,
                VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, bounds.Width - 10),
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1
            };
            context.DrawText(detailText, new Point(bounds.X + 5, bounds.Y + 20));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hit = HitTest(e.GetPosition(this));
        if (ReferenceEquals(hit, _hovered))
            return;

        _hovered = hit;
        Cursor = hit?.CanDrillDown == true ? Cursors.Hand : Cursors.Arrow;
        _toolTip.Content = hit is null
            ? null
            : string.IsNullOrEmpty(hit.Detail) ? hit.Label : $"{hit.Label}\n{hit.Detail}";
        _toolTip.IsOpen = hit is not null && IsMouseOver;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = null;
        _toolTip.IsOpen = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var hit = HitTest(e.GetPosition(this));
        if (hit is null)
            return;

        ItemSelected?.Invoke(this, hit);

        if (e.ClickCount >= 2 && hit.CanDrillDown)
            ItemActivated?.Invoke(this, hit);
    }

    private TreemapItem? HitTest(Point point)
    {
        // Later items are drawn on top of earlier ones only when nested; a linear reverse scan
        // is accurate here and fast enough for the few hundred tiles we render.
        for (int i = _layoutItems.Count - 1; i >= 0; i--)
        {
            if (_layoutItems[i].Bounds.Contains(point))
                return _layoutItems[i];
        }

        return null;
    }

    private static bool IsLightColor(Color color)
    {
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        return luminance > 0.6;
    }

    private static Pen CreateGapPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateHoverPen()
    {
        var pen = new Pen(Brushes.White, 2);
        pen.Freeze();
        return pen;
    }
}
