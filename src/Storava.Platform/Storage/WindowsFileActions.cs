using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Migration;
using Storava.Domain.Common;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Platform.Storage;

/// <summary>
/// The only code in Storava that changes the file system.
/// <para>
/// Two decisions shape everything here. Removal always goes through the shell's Recycle Bin
/// (<c>SHFileOperation</c> with <c>FOF_ALLOWUNDO</c>) rather than <c>Directory.Delete</c>, so the
/// user can undo anything from Explorer. And reparse points are never followed — a junction inside
/// a tree is counted and stepped over, never walked into, so a move can never drag in a folder the
/// user did not choose.
/// </para>
/// </summary>
public sealed class WindowsFileActions : IFileSystemActions
{
    private const int CopyBufferSize = 1024 * 1024;

    /// <summary>Progress is reported at most this often, so a tree of small files does not flood the UI.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(120);

    private readonly ILogger<WindowsFileActions> _logger;

    public WindowsFileActions(ILogger<WindowsFileActions> logger) => _logger = logger;

    public bool DirectoryExists(string path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    public bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    public bool IsReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Unreadable is treated as "yes, a link": the safe answer is the one that stops the step.
            return true;
        }
    }

    public bool IsEmptyDirectory(string path)
    {
        try
        {
            return Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public Result<long> GetAvailableFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
                return Result.Failure<long>(ExecutionErrors.DestinationInvalid);

            return Result.Success(new DriveInfo(root).AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Free space could not be read for a destination.");
            return Result.Failure<long>(ExecutionErrors.DestinationInvalid);
        }
    }

    public Result<string> GetVolumeRoot(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrWhiteSpace(root)
                ? Result.Failure<string>(ExecutionErrors.DestinationInvalid)
                : Result.Success(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result.Failure<string>(ExecutionErrors.DestinationInvalid);
        }
    }

    public Task<Result<DirectoryFacts>> MeasureAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Measure(path, cancellationToken), cancellationToken);

    private Result<DirectoryFacts> Measure(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            // One file counts as one file, so that comparing a copied file against its source uses
            // exactly the same comparison a copied tree does.
            if (File.Exists(path))
            {
                try
                {
                    return Result.Success(new DirectoryFacts(new FileInfo(path).Length, 1, 0, 0));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    _logger.LogWarning(ex, "A file could not be measured.");
                    return Result.Failure<DirectoryFacts>(ExecutionErrors.CopyFailed);
                }
            }

            return Result.Failure<DirectoryFacts>(ExecutionErrors.SourceMissing);
        }

        long bytes = 0, files = 0, folders = 0, links = 0;

        // Iterative so a deep tree cannot overflow the stack, and so each level's errors are
        // survivable on their own.
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            try
            {
                foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos())
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        links++;
                        continue;
                    }

                    if (entry is DirectoryInfo directory)
                    {
                        folders++;
                        pending.Push(directory.FullName);
                    }
                    else if (entry is FileInfo file)
                    {
                        files++;
                        bytes += file.Length;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                // A folder that cannot even be read cannot be copied faithfully either, so the
                // verification step would fail later anyway. Refusing now costs the user nothing.
                _logger.LogWarning(ex, "A folder could not be measured.");
                return Result.Failure<DirectoryFacts>(ExecutionErrors.CopyFailed);
            }
        }

        return Result.Success(new DirectoryFacts(bytes, files, folders, links));
    }

    public Task<Result> CopyAsync(
        string sourcePath,
        string destinationPath,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Copy(sourcePath, destinationPath, progress, cancellationToken), cancellationToken);

    private Result Copy(
        string sourcePath,
        string destinationPath,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourcePath) && File.Exists(sourcePath))
            return CopySingleFile(sourcePath, destinationPath, progress, cancellationToken);

        return CopyDirectory(sourcePath, destinationPath, progress, cancellationToken);
    }

    /// <summary>
    /// Copies one file to <paramref name="destinationPath"/>, which names the file itself rather
    /// than a folder to drop it in — the caller has already decided what it should be called.
    /// </summary>
    private Result CopySingleFile(
        string sourcePath,
        string destinationPath,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(sourcePath);

            string? parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            CopyFile(file, destinationPath, cancellationToken);
            progress?.Report(new CopyProgress(file.Length, file.Length, destinationPath));
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure(ExecutionErrors.Cancelled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "A file could not be copied.");
            return Result.Failure(ExecutionErrors.CopyFailed);
        }
    }

    private Result CopyDirectory(
        string sourcePath,
        string destinationPath,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        var measured = Measure(sourcePath, cancellationToken);
        if (measured.IsFailure)
            return Result.Failure(measured.Error);

        long total = measured.Value.Bytes;
        long copied = 0;
        var lastReport = DateTime.UtcNow;

        try
        {
            Directory.CreateDirectory(destinationPath);

            var pending = new Stack<(string Source, string Destination)>();
            pending.Push((sourcePath, destinationPath));

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (source, destination) = pending.Pop();
                Directory.CreateDirectory(destination);

                foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Skipped for the same reason as in Measure, which is what keeps the two counts
                    // comparable — verification would fail if only one of them followed links.
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        continue;

                    string target = Path.Combine(destination, entry.Name);

                    if (entry is DirectoryInfo directory)
                    {
                        pending.Push((directory.FullName, target));
                        continue;
                    }

                    if (entry is not FileInfo file)
                        continue;

                    CopyFile(file, target, cancellationToken);
                    copied += file.Length;

                    if (progress is not null && DateTime.UtcNow - lastReport >= ProgressInterval)
                    {
                        progress.Report(new CopyProgress(copied, total, file.FullName));
                        lastReport = DateTime.UtcNow;
                    }
                }
            }

            progress?.Report(new CopyProgress(copied, total, destinationPath));
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogError(ex, "Copying a folder failed.");
            return Result.Failure(ExecutionErrors.CopyFailed);
        }
    }

    private static void CopyFile(FileInfo file, string target, CancellationToken cancellationToken)
    {
        using (var input = new FileStream(
                   file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan))
        using (var output = new FileStream(
                   target, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.SequentialScan))
        {
            input.CopyTo(output, CopyBufferSize);
        }

        // Timestamps are part of what makes a cache usable after a move; tools compare them.
        try
        {
            File.SetLastWriteTimeUtc(target, file.LastWriteTimeUtc);
            File.SetCreationTimeUtc(target, file.CreationTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            // Cosmetic. Never worth abandoning a copy that otherwise succeeded.
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task<Result> MoveToRecycleBinAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => MoveToRecycleBin(path), cancellationToken);

    private Result MoveToRecycleBin(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
            return Result.Failure(ExecutionErrors.SourceMissing);

        // SHFileOperation takes a double-null-terminated list, so the extra '\0' is required.
        var operation = new NativeMethods.ShFileOpStruct
        {
            Wnd = IntPtr.Zero,
            Func = NativeMethods.FoDelete,
            From = Path.GetFullPath(path) + '\0' + '\0',
            To = null,
            Flags = NativeMethods.FofAllowUndo
                    | NativeMethods.FofNoConfirmation
                    | NativeMethods.FofNoConfirmMkDir
                    | NativeMethods.FofSilent
                    | NativeMethods.FofNoErrorUi
        };

        int code = NativeMethods.SHFileOperation(ref operation);

        if (code != 0 || operation.AnyOperationsAborted)
        {
            _logger.LogError("SHFileOperation refused to recycle a folder (code {Code}).", code);
            return Result.Failure(RecycleErrorFor(code));
        }

        // The shell reports success even when a folder survives, for example on a volume with no
        // Recycle Bin. Confirming by hand is what stops a step being called done when it is not.
        if (Directory.Exists(path) || File.Exists(path))
        {
            _logger.LogError("A folder was still present after the shell reported it recycled.");
            return Result.Failure(ExecutionErrors.RecycleFailed);
        }

        return Result.Success();
    }

    /// <summary>
    /// Turns the shell's number into something the person in front of the screen can act on.
    /// <para>
    /// This is worth the trouble because of what a failure here costs. The removal is the last step
    /// of a move, so by the time it fails the copy has already been made and verified, and the
    /// recovery is to undo all of it. Being told only that "the folder could not be sent to the
    /// Recycle Bin" after that leaves no way to tell a locked file — close the program, try again —
    /// apart from a permission problem, which retrying will never fix.
    /// </para>
    /// <para>
    /// <c>SHFileOperation</c> returns a mixture of its own pre-Win32 <c>DE_</c> codes and ordinary
    /// Win32 ones, so both are matched. Anything unrecognised stays the general failure rather than
    /// being dressed up as a diagnosis.
    /// </para>
    /// </summary>
    internal static Error RecycleErrorFor(int code) => code switch
    {
        NativeMethods.ErrorSharingViolation or NativeMethods.ErrorLockViolation
            => ExecutionErrors.RecycleSourceInUse,

        NativeMethods.ErrorAccessDenied or NativeMethods.DeAccessDeniedSrc
            => ExecutionErrors.RecycleAccessDenied,

        _ => ExecutionErrors.RecycleFailed
    };

    public Result CreateLink(string linkPath, string targetPath, MigrationMethod method, bool isFolder = true)
    {
        try
        {
            // A junction only exists for directories. A file has to have a symbolic link, which
            // needs a privilege a normal user does not have — so this can legitimately fail on a
            // move that otherwise worked, and the caller records that rather than rolling back.
            if (!isFolder)
            {
                File.CreateSymbolicLink(linkPath, targetPath);
                return Result.Success();
            }

            switch (method)
            {
                case MigrationMethod.SymbolicLink:
                    // Needs SeCreateSymbolicLinkPrivilege — administrator, or Developer Mode on.
                    Directory.CreateSymbolicLink(linkPath, targetPath);
                    return Result.Success();

                case MigrationMethod.Junction:
                case MigrationMethod.OfficialSetting:
                case MigrationMethod.None:
                default:
                    // A junction needs no special privilege, which is why it is the default: the
                    // app is expected to run as a normal user.
                    return CreateJunction(linkPath, targetPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "A link back to the original location could not be created.");
            return Result.Failure(ExecutionErrors.LinkFailed);
        }
    }

    private Result CreateJunction(string linkPath, string targetPath)
    {
        string fullTarget = Path.GetFullPath(targetPath);
        string fullLink = Path.GetFullPath(linkPath);

        // The reparse point is set on an existing, empty directory.
        Directory.CreateDirectory(fullLink);

        using var handle = NativeMethods.CreateFile(
            fullLink,
            NativeMethods.GenericWrite,
            FileShare.Read | FileShare.Write,
            IntPtr.Zero,
            FileMode.Open,
            NativeMethods.FileFlagBackupSemantics | NativeMethods.FileFlagOpenReparsePoint,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            _logger.LogWarning("The new junction directory could not be opened (error {Error}).",
                Marshal.GetLastWin32Error());
            return Result.Failure(ExecutionErrors.LinkFailed);
        }

        byte[] buffer = BuildMountPointReparseBuffer(fullTarget, out int dataLength);

        bool ok = NativeMethods.DeviceIoControl(
            handle, NativeMethods.FsctlSetReparsePoint, buffer, dataLength, IntPtr.Zero, 0, out _, IntPtr.Zero);

        if (!ok)
        {
            int error = Marshal.GetLastWin32Error();
            _logger.LogWarning("Setting the junction reparse point failed (error {Error}).", error);

            // Leaving an empty directory behind would look like the move had failed entirely.
            try
            {
                Directory.Delete(fullLink);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Harmless: an empty folder at the old path.
            }

            return Result.Failure(ExecutionErrors.LinkFailed);
        }

        return Result.Success();
    }

    /// <summary>
    /// Lays out a <c>REPARSE_DATA_BUFFER</c> for a mount point. The substitute name is the NT form
    /// (<c>\??\C:\path</c>) that the file system resolves; the print name is what Explorer shows.
    /// </summary>
    private static byte[] BuildMountPointReparseBuffer(string target, out int totalLength)
    {
        string substituteName = @"\??\" + target;
        string printName = target;

        byte[] substituteBytes = System.Text.Encoding.Unicode.GetBytes(substituteName);
        byte[] printBytes = System.Text.Encoding.Unicode.GetBytes(printName);

        // Both names are null-terminated inside the path buffer.
        int pathBufferLength = substituteBytes.Length + 2 + printBytes.Length + 2;

        // 8 bytes of header (tag, data length, reserved) + 8 bytes of offsets/lengths.
        const int HeaderLength = 8;
        const int OffsetsLength = 8;

        totalLength = HeaderLength + OffsetsLength + pathBufferLength;
        var buffer = new byte[totalLength];
        var span = buffer.AsSpan();

        BitConverter.TryWriteBytes(span[..4], NativeMethods.IoReparseTagMountPoint);
        BitConverter.TryWriteBytes(span.Slice(4, 2), (ushort)(OffsetsLength + pathBufferLength));
        BitConverter.TryWriteBytes(span.Slice(6, 2), (ushort)0); // Reserved

        BitConverter.TryWriteBytes(span.Slice(8, 2), (ushort)0);                        // SubstituteNameOffset
        BitConverter.TryWriteBytes(span.Slice(10, 2), (ushort)substituteBytes.Length);   // SubstituteNameLength
        BitConverter.TryWriteBytes(span.Slice(12, 2), (ushort)(substituteBytes.Length + 2)); // PrintNameOffset
        BitConverter.TryWriteBytes(span.Slice(14, 2), (ushort)printBytes.Length);        // PrintNameLength

        int cursor = HeaderLength + OffsetsLength;
        substituteBytes.CopyTo(buffer, cursor);
        cursor += substituteBytes.Length + 2;
        printBytes.CopyTo(buffer, cursor);

        return buffer;
    }
}
