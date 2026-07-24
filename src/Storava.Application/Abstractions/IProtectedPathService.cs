namespace Storava.Application.Abstractions;

/// <summary>
/// Central authority for protected, system-critical locations. Any path reported as
/// protected must never be offered for deletion or migration — not even on AI advice.
/// This is enforced by the architecture, independent of the UI.
/// </summary>
public interface IProtectedPathService
{
    bool IsProtected(string path);

    IReadOnlyList<string> ProtectedRoots { get; }
}
