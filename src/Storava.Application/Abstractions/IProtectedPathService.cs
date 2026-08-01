namespace Storava.Application.Abstractions;

/// <summary>
/// Central authority for protected, system-critical locations. Any path reported as
/// protected must never be offered for deletion or migration — not even on AI advice.
/// This is enforced by the architecture, independent of the UI.
/// </summary>
public interface IProtectedPathService
{
    bool IsProtected(string path);

    /// <summary>
    /// Which protected root covers this path, or null when none does.
    /// <para>
    /// "This is a protected system location" tells somebody nothing they can act on: they cannot
    /// see which rule matched or how far up it reaches. Naming the root turns a refusal into a
    /// fact — that a folder is refused because it sits under C:\Windows is something a person can
    /// agree or disagree with.
    /// </para>
    /// </summary>
    string? MatchingRoot(string path);

    IReadOnlyList<string> ProtectedRoots { get; }
}
