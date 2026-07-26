namespace Storava.Domain.Enums;

/// <summary>
/// Where a plan step stands in its one and only run. A step never goes backwards except through
/// <see cref="RolledBack"/>, which is written by the recovery path rather than by the executor.
/// </summary>
public enum ExecutionStatus
{
    /// <summary>Confirmed by the user but not started.</summary>
    Pending = 0,

    /// <summary>The file system is being touched right now.</summary>
    Running = 1,

    /// <summary>Finished and verified.</summary>
    Completed = 2,

    /// <summary>Stopped before anything irreversible happened, or undone afterwards.</summary>
    Failed = 3,

    /// <summary>Preflight refused it, or the user passed over it.</summary>
    Skipped = 4,

    /// <summary>Partly done, then undone. The source is back where it was.</summary>
    RolledBack = 5,

    /// <summary>Cancelled between steps; nothing of this step ran.</summary>
    Cancelled = 6
}
