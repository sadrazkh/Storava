using Storava.App.Models;
using Storava.App.Services;

namespace Storava.App.Tests;

/// <summary>
/// Moving through the cleanup page's phases.
/// <para>
/// The first test here is the one that matters. A rewrite once left the last phase reachable with
/// no way to act from it — the button that would have started the run was hidden on that phase and
/// the older Start button had been removed with the layout it lived in. Everything still built and
/// every other test still passed. "Every phase has a primary action" is now something checked
/// rather than something intended.
/// </para>
/// </summary>
public class CleanupPhasesTests
{
    private static readonly CleanupPhase[] AllPhases =
        [CleanupPhase.Choose, CleanupPhase.Destination, CleanupPhase.Run];

    [Fact]
    public void EveryPhase_NamesAPrimaryAction()
    {
        foreach (var phase in AllPhases)
        {
            var key = CleanupPhases.PrimaryKey(phase);
            Assert.False(string.IsNullOrWhiteSpace(key), $"{phase} has no primary action.");
        }

        // And no two phases claim the same one, which would mean a button that says the same thing
        // whatever it is about to do.
        var keys = AllPhases.Select(CleanupPhases.PrimaryKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Every phase can be advanced from, given the state that phase asks for. A phase that can
    /// never advance is a dead end however good its button looks.
    /// </summary>
    [Fact]
    public void EveryPhase_CanBeAdvancedFrom()
    {
        Assert.True(CleanupPhases.CanAdvance(
            CleanupPhase.Choose, hasSelection: true, hasMoves: false, hasDestination: false, canRun: false));

        Assert.True(CleanupPhases.CanAdvance(
            CleanupPhase.Destination, hasSelection: true, hasMoves: true, hasDestination: true, canRun: false));

        Assert.True(CleanupPhases.CanAdvance(
            CleanupPhase.Run, hasSelection: true, hasMoves: false, hasDestination: false, canRun: true));
    }

    [Fact]
    public void Choose_NeedsSomethingSelected()
    {
        Assert.False(CleanupPhases.CanAdvance(
            CleanupPhase.Choose, hasSelection: false, hasMoves: false, hasDestination: false, canRun: true));
    }

    /// <summary>A destination is demanded only when something is actually being moved.</summary>
    [Fact]
    public void Destination_NeedsAFolderOnlyWhenSomethingMoves()
    {
        Assert.False(CleanupPhases.CanAdvance(
            CleanupPhase.Destination, hasSelection: true, hasMoves: true, hasDestination: false, canRun: false));

        Assert.True(CleanupPhases.CanAdvance(
            CleanupPhase.Destination, hasSelection: true, hasMoves: false, hasDestination: false, canRun: false));
    }

    /// <summary>
    /// The run cannot begin when the check refused every step. That is not a dead end — the page
    /// says why and Back is offered — but it must not present a button that would do nothing.
    /// </summary>
    [Fact]
    public void Run_NeedsSomethingThatCanActuallyRun()
    {
        Assert.False(CleanupPhases.CanAdvance(
            CleanupPhase.Run, hasSelection: true, hasMoves: false, hasDestination: true, canRun: false));
    }

    [Fact]
    public void WithNothingToMove_TheDestinationPhaseIsSkippedInBothDirections()
    {
        Assert.Equal(CleanupPhase.Run, CleanupPhases.Next(CleanupPhase.Choose, hasMoves: false));
        Assert.Equal(CleanupPhase.Choose, CleanupPhases.Back(CleanupPhase.Run, hasMoves: false));
    }

    [Fact]
    public void WithSomethingToMove_TheDestinationPhaseIsVisitedInBothDirections()
    {
        Assert.Equal(CleanupPhase.Destination, CleanupPhases.Next(CleanupPhase.Choose, hasMoves: true));
        Assert.Equal(CleanupPhase.Run, CleanupPhases.Next(CleanupPhase.Destination, hasMoves: true));

        Assert.Equal(CleanupPhase.Destination, CleanupPhases.Back(CleanupPhase.Run, hasMoves: true));
        Assert.Equal(CleanupPhase.Choose, CleanupPhases.Back(CleanupPhase.Destination, hasMoves: true));
    }

    /// <summary>Going forward and back returns you where you were, whichever route applies.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForwardThenBack_ReturnsToTheSamePhase(bool hasMoves)
    {
        var next = CleanupPhases.Next(CleanupPhase.Choose, hasMoves);

        Assert.Equal(CleanupPhase.Choose, CleanupPhases.Back(next, hasMoves));
    }
}
