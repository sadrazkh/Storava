namespace Storava.Application.Scanning;

/// <summary>
/// Carries resume information in and out of one scan run.
/// <para>
/// It exists because a cancelled scan leaves by throwing, so it has nowhere to return the work it
/// did not get to. The scanner writes what is still outstanding into <see cref="Pending"/> on its
/// way out, whether it finished, was cancelled, or failed.
/// </para>
/// </summary>
public sealed class ScanResumePoint
{
    /// <summary>Where to carry on from, or null to start at the root.</summary>
    public ScanResumeState? Resume { get; init; }

    /// <summary>
    /// Set by the scanner when it stops with folders still unfinished, and left null when the walk
    /// ran to the end. The caller decides whether to store it.
    /// </summary>
    public ScanResumeState? Pending { get; set; }
}
