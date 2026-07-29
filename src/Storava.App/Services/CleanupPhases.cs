using Storava.App.Models;

namespace Storava.App.Services;

/// <summary>
/// How the cleanup page moves between its phases, and what its one primary button does on each.
/// <para>
/// Pulled out of the view model because of the defect it exists to prevent. The rewrite that gave
/// the page a single primary action hid that button on the last phase and deleted the Start button
/// that used to live inside the check card — leaving a phase the user could reach and then not act
/// from. Nothing caught it, because "every phase has a way forward" was an idea in the layout
/// rather than a rule anything checked.
/// </para>
/// </summary>
public static class CleanupPhases
{
    /// <param name="hasMoves">
    /// Whether anything is being moved. When nothing is, the destination phase does not exist —
    /// asking for a folder that no step would use is a screen that only exists to be dismissed.
    /// </param>
    public static CleanupPhase Next(CleanupPhase phase, bool hasMoves) => phase switch
    {
        CleanupPhase.Choose => hasMoves ? CleanupPhase.Destination : CleanupPhase.Run,
        CleanupPhase.Destination => CleanupPhase.Run,
        _ => CleanupPhase.Run
    };

    public static CleanupPhase Back(CleanupPhase phase, bool hasMoves) => phase switch
    {
        CleanupPhase.Run => hasMoves ? CleanupPhase.Destination : CleanupPhase.Choose,
        _ => CleanupPhase.Choose
    };

    /// <summary>
    /// The localization key for the primary button's label.
    /// <para>
    /// Every phase returns one. A phase whose primary action has no name is a phase with no
    /// primary action, which is the state this class exists to make impossible to reach quietly.
    /// </para>
    /// </summary>
    public static string PrimaryKey(CleanupPhase phase) => phase switch
    {
        CleanupPhase.Choose => "Str.Cleanup.Next.Destination",
        CleanupPhase.Destination => "Str.Cleanup.Next.Check",
        _ => "Str.Cleanup.Start"
    };

    /// <summary>Whether the primary button can be pressed right now.</summary>
    /// <param name="hasSelection">Something is ticked.</param>
    /// <param name="hasMoves">Something being moved, so a destination is required.</param>
    /// <param name="hasDestination">A destination folder has been chosen.</param>
    /// <param name="canRun">The check found at least one step that would actually run.</param>
    public static bool CanAdvance(
        CleanupPhase phase,
        bool hasSelection,
        bool hasMoves,
        bool hasDestination,
        bool canRun) => phase switch
    {
        CleanupPhase.Choose => hasSelection,
        CleanupPhase.Destination => !hasMoves || hasDestination,
        _ => canRun
    };
}
