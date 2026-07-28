using Storava.Application.Planning;

namespace Storava.Application.Tests;

/// <summary>
/// One folder for everything is the option that makes clearing a drive bearable, and collisions
/// are what stop it working. These cover the case that actually happens on a developer's machine:
/// a dozen folders with the same name under different projects.
/// </summary>
public class MoveDestinationPlannerTests
{
    [Fact]
    public void A_single_item_keeps_its_own_name()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var destination = MoveDestinationPlanner.Resolve(@"E:\archive", @"C:\projects\api\node_modules", taken);

        Assert.Equal(@"E:\archive\node_modules", destination);
    }

    /// <summary>
    /// Without this the second move would be refused for landing in a non-empty folder — a message
    /// about the destination, for a problem the user never created.
    /// </summary>
    [Fact]
    public void Two_items_with_the_same_name_are_told_apart_by_where_they_came_from()
    {
        var resolved = MoveDestinationPlanner.ResolveAll(@"E:\archive",
        [
            @"C:\projects\api\node_modules",
            @"C:\projects\web\node_modules"
        ]);

        // The one that collides is the one that gets qualified, and it borrows its own parent, so
        // the longer name describes the item it actually names.
        Assert.Equal(@"E:\archive\node_modules", resolved[@"C:\projects\api\node_modules"]);
        Assert.Equal(@"E:\archive\web-node_modules", resolved[@"C:\projects\web\node_modules"]);
        Assert.Equal(2, resolved.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Borrowing_continues_up_the_path_while_names_still_collide()
    {
        var resolved = MoveDestinationPlanner.ResolveAll(@"E:\archive",
        [
            @"C:\one\api\cache",
            @"C:\two\api\cache",
            @"C:\three\api\cache"
        ]);

        Assert.Equal(3, resolved.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(@"E:\archive\cache", resolved[@"C:\one\api\cache"]);
        Assert.Equal(@"E:\archive\api-cache", resolved[@"C:\two\api\cache"]);
        // "api-cache" is spoken for by now, so the third has to reach one segment further up.
        Assert.Equal(@"E:\archive\three-api-cache", resolved[@"C:\three\api\cache"]);
    }

    /// <summary>
    /// The drive letter is dropped from the borrowed segments, so paths differing only by drive
    /// have identical segments — and once those run out a number is all that is left.
    /// </summary>
    [Fact]
    public void Identical_paths_on_different_drives_still_get_separate_destinations()
    {
        var resolved = MoveDestinationPlanner.ResolveAll(@"E:\archive",
        [
            @"C:\data\cache",
            @"D:\data\cache",
            @"F:\data\cache"
        ]);

        Assert.Equal(3, resolved.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(@"E:\archive\cache", resolved[@"C:\data\cache"]);
        Assert.Equal(@"E:\archive\data-cache", resolved[@"D:\data\cache"]);
        Assert.Equal(@"E:\archive\data-cache-2", resolved[@"F:\data\cache"]);
    }

    [Fact]
    public void The_same_selection_always_produces_the_same_destinations()
    {
        string[] sources = [@"C:\a\cache", @"C:\b\cache", @"C:\c\cache"];

        var first = MoveDestinationPlanner.ResolveAll(@"E:\archive", sources);
        var second = MoveDestinationPlanner.ResolveAll(@"E:\archive", sources);

        Assert.Equal(first, second);
    }

    /// <summary>Case differs, the folder does not — Windows would treat these as one place.</summary>
    [Fact]
    public void Names_that_differ_only_in_case_are_treated_as_colliding()
    {
        var resolved = MoveDestinationPlanner.ResolveAll(@"E:\archive",
        [
            @"C:\one\Cache",
            @"C:\two\cache"
        ]);

        Assert.Equal(2, resolved.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_drive_root_still_lands_somewhere_usable()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var destination = MoveDestinationPlanner.Resolve(@"E:\archive", @"C:\", taken);

        Assert.StartsWith(@"E:\archive", destination, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(@"E:\archive", destination.TrimEnd('\\'));
    }
}
