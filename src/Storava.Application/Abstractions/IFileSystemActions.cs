using Storava.Application.Migration;
using Storava.Domain.Common;
using Storava.Domain.Enums;

namespace Storava.Application.Abstractions;

/// <summary>
/// The only way anything in Storava changes the file system.
/// <para>
/// There is deliberately no permanent-delete operation on this interface. Removal always means
/// <see cref="MoveToRecycleBinAsync"/>, including when Storava is undoing a copy it made itself, so
/// no code path anywhere in the app is able to destroy data outright. Layers that must not be able
/// to act — <c>Storava.AI</c> above all — simply do not reference the project that implements this.
/// </para>
/// </summary>
public interface IFileSystemActions
{
    bool DirectoryExists(string path);

    /// <summary>
    /// True for a junction, symbolic link or mount point. Such a folder is a pointer, not storage:
    /// deleting it frees nothing and moving it would copy the target's contents.
    /// </summary>
    bool IsReparsePoint(string path);

    /// <summary>True when the folder exists and holds no entries at all.</summary>
    bool IsEmptyDirectory(string path);

    /// <summary>Bytes currently available to this user on the volume holding <paramref name="path"/>.</summary>
    Result<long> GetAvailableFreeSpace(string path);

    /// <summary>The volume root for a path, used to tell "different drive" from "same drive".</summary>
    Result<string> GetVolumeRoot(string path);

    /// <summary>Walks the folder and totals what is really there. Read-only.</summary>
    Task<Result<DirectoryFacts>> MeasureAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a folder tree. The source is never touched. A cancelled or failed copy leaves
    /// whatever was written at the destination for the caller to clean up — this method does not
    /// decide what to do about it.
    /// </summary>
    Task<Result> CopyDirectoryAsync(
        string sourcePath,
        string destinationPath,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a folder to the Recycle Bin. This is the only removal Storava performs, so anything it
    /// takes away can be put back by the user from Explorer.
    /// </summary>
    Task<Result> MoveToRecycleBinAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Leaves a junction or symbolic link at <paramref name="linkPath"/> pointing at
    /// <paramref name="targetPath"/>, so tools that hard-code the old location keep working.
    /// </summary>
    Result CreateDirectoryLink(string linkPath, string targetPath, MigrationMethod method);
}
