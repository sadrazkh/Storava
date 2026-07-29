using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Storava.App.Tests;

/// <summary>
/// The sidebar's section labels have to actually render.
/// <para>
/// This exists because UI Automation cannot answer the question. WPF builds a group's automation
/// children from the items in that group, so a <c>GroupStyle</c> header never appears in the
/// automation tree whether or not it is on screen — reading it there suggests the labels are
/// missing even when they are perfectly visible. The visual tree is the only honest witness.
/// </para>
/// <para>
/// What this pins down, from running it against deliberately broken versions: dropping the grouping
/// fails it, and a header template that renders nothing fails it. Removing the <c>HeaderTemplate</c>
/// altogether does not, because WPF's default group header draws the group's name by itself. So it
/// proves the labels appear with their text, not that the custom template is what draws them.
/// </para>
/// </summary>
public class NavigationGroupHeaderTests
{
    [Fact]
    public void TheSidebarSectionHeadersAreRendered()
    {
        var error = RunOnStaThread(() =>
        {
            var items = new ObservableCollection<Row>
            {
                new("Dashboard", "OVERVIEW"),
                new("New scan", "ANALYZE"),
                new("Scan explorer", "ANALYZE"),
                new("Cleanup", "TAKE ACTION"),
            };

            var view = CollectionViewSource.GetDefaultView(items);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Row.Group)));

            var list = new ListBox { ItemsSource = view };
            list.ItemTemplate = Template("<TextBlock Text=\"{Binding Title}\" />");
            list.GroupStyle.Add(new GroupStyle
            {
                HeaderTemplate = Template("<TextBlock Text=\"{Binding Name}\" />")
            });

            Render(list);

            var rendered = TextBlocksIn(list).Select(t => t.Text).ToList();

            // The headers, not merely the items: an item happens to carry the same words in one
            // case, so this asserts on the set that only a rendered header can complete.
            Assert.Contains("OVERVIEW", rendered);
            Assert.Contains("ANALYZE", rendered);
            Assert.Contains("TAKE ACTION", rendered);

            // Three groups, three headers — a header drawn once for a group that repeats would
            // still satisfy the assertions above.
            Assert.Equal(3, rendered.Count(t => t is "OVERVIEW" or "ANALYZE" or "TAKE ACTION"));
        });

        Assert.Null(error);
    }

    private sealed record Row(string Title, string Group);

    private static DataTemplate Template(string inner) =>
        (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            $"""<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">{inner}</DataTemplate>""");

    /// <summary>Lays the control out, which is what makes containers and group headers exist.</summary>
    private static void Render(FrameworkElement element)
    {
        var window = new Window { Width = 300, Height = 600, Content = element };
        window.Show();
        element.UpdateLayout();
        window.Close();
    }

    private static IEnumerable<TextBlock> TextBlocksIn(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is TextBlock text)
                yield return text;

            foreach (var nested in TextBlocksIn(child))
                yield return nested;
        }
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
