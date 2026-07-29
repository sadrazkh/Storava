using System.Windows;
using System.Windows.Controls;
using Storava.App.Controls;

namespace Storava.App.Tests;

/// <summary>
/// The charts must not inherit the shell's reading direction.
/// <para>
/// Right-to-left is implemented in WPF as a mirror transform that every descendant inherits. For
/// laid-out content that is the whole point. For a control that draws its own geometry it is not:
/// under Persian the donut's arcs swept backwards and the treemap's tiles landed mirror-imaged,
/// labels and all. A chart is anchored to its numbers rather than to reading order.
/// </para>
/// </summary>
public class ChartFlowDirectionTests
{
    [Fact]
    public void TheDonutDoesNotMirrorWithTheShell()
    {
        var error = RunOnStaThread(() =>
        {
            var host = new Grid { FlowDirection = FlowDirection.RightToLeft };
            var chart = new DonutChart();
            host.Children.Add(chart);

            Assert.Equal(FlowDirection.LeftToRight, chart.FlowDirection);
        });

        Assert.Null(error);
    }

    [Fact]
    public void TheTreemapDoesNotMirrorWithTheShell()
    {
        var error = RunOnStaThread(() =>
        {
            var host = new Grid { FlowDirection = FlowDirection.RightToLeft };
            var treemap = new TreemapControl();
            host.Children.Add(treemap);

            Assert.Equal(FlowDirection.LeftToRight, treemap.FlowDirection);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The opt-out has to survive being placed in a right-to-left tree, not merely be the default.
    /// Setting it in the constructor is a local value, which inheritance cannot overwrite — this
    /// is what checks that reasoning rather than trusting it.
    /// </summary>
    [Fact]
    public void TheOptOutSurvivesADeeplyNestedRightToLeftParent()
    {
        var error = RunOnStaThread(() =>
        {
            var outer = new Grid { FlowDirection = FlowDirection.RightToLeft };
            var middle = new Grid();
            var inner = new Grid();
            var chart = new DonutChart();

            outer.Children.Add(middle);
            middle.Children.Add(inner);
            inner.Children.Add(chart);

            Assert.Equal(FlowDirection.RightToLeft, inner.FlowDirection);
            Assert.Equal(FlowDirection.LeftToRight, chart.FlowDirection);
        });

        Assert.Null(error);
    }

    private static Exception? RunOnStaThread(Action action)
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return captured;
    }
}
