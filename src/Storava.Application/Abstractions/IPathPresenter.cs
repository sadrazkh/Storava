namespace Storava.Application.Abstractions;

/// <summary>
/// Getting at a path Storava is talking about: onto the clipboard, or open in the file manager.
/// <para>
/// Every page here names files and folders, and until now the only thing a person could do with
/// one was read it off the screen and type it somewhere themselves. A path that cannot be copied
/// is a path you have to transcribe, and the ones this application shows are long.
/// </para>
/// </summary>
public interface IPathPresenter
{
    /// <summary>Puts the path on the clipboard. False when the clipboard refused it.</summary>
    bool Copy(string? path);

    /// <summary>
    /// Opens the file manager with the item selected, or the folder itself if it is a folder.
    /// <para>
    /// False when there is nothing to show — the path is empty, or it no longer exists, which is
    /// ordinary here since a scan describes the disk as it was and the user may have acted since.
    /// </para>
    /// </summary>
    bool Reveal(string? path);

    /// <summary>Whether <see cref="Reveal"/> could do anything with this path right now.</summary>
    bool CanReveal(string? path);
}
