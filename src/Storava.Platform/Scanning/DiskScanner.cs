using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Platform.Scanning;

/// <summary>
/// Iterative, streaming disk scanner. Walks the tree with an explicit stack (post-order so
/// folder sizes aggregate), writes every item to the sink as it is finalized, and keeps only
/// the current path stack plus running counters in memory. Robust to access errors and
/// reparse-point loops.
/// <para>
/// Because the stack is explicit, it is also what an interrupted scan needs to carry on: the
/// frames still on it when the walk stops are handed back through the resume point, and a later
/// run pushes them again instead of starting at the root.
/// </para>
/// </summary>
public sealed class DiskScanner : IDiskScanner
{
    private const int ProgressIntervalMs = 200;

    private readonly IProtectedPathService _protectedPaths;
    private readonly ILogger<DiskScanner> _logger;

    public DiskScanner(IProtectedPathService protectedPaths, ILogger<DiskScanner> logger)
    {
        _protectedPaths = protectedPaths;
        _logger = logger;
    }

    public async Task<ScanOutcome> ScanAsync(
        ScanRequest request,
        string sessionId,
        IScanItemSink sink,
        IProgress<ScanProgress>? progress,
        PauseToken pauseToken,
        ScanResumePoint? resumePoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rootDir = new DirectoryInfo(request.RootPath);
        if (!rootDir.Exists)
            throw new DirectoryNotFoundException($"Scan root not found: {request.RootPath}");

        var excludedExtensions = new HashSet<string>(
            request.ExcludedExtensions.Select(e => e.StartsWith('.') ? e : "." + e),
            StringComparer.OrdinalIgnoreCase);
        var excludedPaths = new HashSet<string>(
            request.ExcludedPaths.Select(NormalizePath),
            StringComparer.OrdinalIgnoreCase);
        bool deep = request.Mode == ScanMode.Deep;

        var stopwatch = Stopwatch.StartNew();
        var resume = resumePoint?.Resume;
        long files = resume?.FilesScanned ?? 0;
        long folders = resume?.FoldersScanned ?? 0;
        long bytes = resume?.BytesScanned ?? 0;
        int errors = resume?.ErrorCount ?? 0;
        long lastReportMs = -ProgressIntervalMs;
        string currentPath = rootDir.FullName;

        var stack = new Stack<Frame>();
        if (resume is { Pending.Count: > 0 })
        {
            // Outermost first, so the deepest folder ends up on top and is finished first — the
            // same order the walk had reached when it stopped.
            foreach (var folder in resume.Pending)
                stack.Push(Frame.Restored(folder));
        }
        else
        {
            stack.Push(new Frame(rootDir, NewId(), parentId: null, depth: 0));
        }

        try
        {
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pauseToken.IsPaused)
                    await pauseToken.WaitWhilePausedAsync().ConfigureAwait(false);

                var frame = stack.Peek();

                if (frame.Enumerator is null)
                {
                    currentPath = frame.Dir.FullName;
                    try
                    {
                        frame.Enumerator = frame.Dir.EnumerateFileSystemInfos().GetEnumerator();
                    }
                    catch (Exception ex) when (IsRecoverable(ex))
                    {
                        errors++;
                        _logger.LogDebug(ex, "Cannot enumerate {Path}.", frame.Dir.FullName);
                        frame.Enumerator = EmptyEnumerator;
                    }
                }

                FileSystemInfo? entry = null;
                bool moved;
                try
                {
                    moved = frame.Enumerator!.MoveNext();
                    if (moved)
                        entry = frame.Enumerator.Current;
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    errors++;
                    _logger.LogDebug(ex, "Enumeration error under {Path}.", frame.Dir.FullName);
                    moved = false;
                }

                if (moved && entry is not null)
                {
                    // A restored folder is walked from the beginning again, because an enumerator
                    // cannot be saved. Anything already written for it is skipped here instead, so
                    // the earlier run's work is neither repeated nor counted twice.
                    if (frame.AlreadyStored(entry.Name))
                        continue;

                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        var di = (DirectoryInfo)entry;
                        if (excludedPaths.Contains(NormalizePath(di.FullName)))
                            continue;

                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            // Reparse point (junction/symlink): record it but never descend — avoids loops.
                            var reparseItem = BuildItem(di, NewId(), frame.Id, frame.Depth + 1, sessionId,
                                ItemType.Folder, 0, 0, 0, 0, isReparse: true);
                            await sink.AddAsync(reparseItem, cancellationToken).ConfigureAwait(false);
                            folders++;
                            frame.FolderCount++;
                        }
                        else
                        {
                            stack.Push(new Frame(di, NewId(), frame.Id, frame.Depth + 1));
                        }
                    }
                    else
                    {
                        var fi = (FileInfo)entry;
                        if (fi.Extension.Length > 0 && excludedExtensions.Contains(fi.Extension))
                            continue;

                        long length = 0;
                        try { length = fi.Length; }
                        catch (Exception ex) when (IsRecoverable(ex)) { errors++; _logger.LogDebug(ex, "Cannot read length of {Path}.", fi.FullName); }

                        long allocated = deep ? GetOnDiskSize(fi.FullName, length) : length;

                        var fileItem = BuildItem(fi, NewId(), frame.Id, frame.Depth + 1, sessionId,
                            ItemType.File, length, allocated, 0, 0, isReparse: false);
                        await sink.AddAsync(fileItem, cancellationToken).ConfigureAwait(false);

                        files++;
                        bytes += length;
                        frame.Size += length;
                        frame.Allocated += allocated;
                        frame.FileCount++;
                    }

                    lastReportMs = MaybeReport(progress, ref lastReportMs, stopwatch, currentPath, files, folders, bytes, errors, force: false);
                }
                else
                {
                    stack.Pop();
                    frame.Enumerator?.Dispose();

                    var folderItem = BuildItem(frame.Dir, frame.Id, frame.ParentId, frame.Depth, sessionId,
                        ItemType.Folder, frame.Size, frame.Allocated, frame.FileCount, frame.FolderCount, isReparse: false);
                    await sink.AddAsync(folderItem, cancellationToken).ConfigureAwait(false);
                    folders++;

                    if (stack.Count > 0)
                    {
                        var parent = stack.Peek();
                        parent.Size += frame.Size;
                        parent.Allocated += frame.Allocated;
                        parent.FileCount += frame.FileCount;
                        parent.FolderCount += frame.FolderCount + 1;
                        parent.MarkStored(frame.Dir.Name);
                    }

                    lastReportMs = MaybeReport(progress, ref lastReportMs, stopwatch, currentPath, files, folders, bytes, errors, force: false);
                }
            }
        }
        finally
        {
            // Runs whether the walk finished, was cancelled, or threw. An empty stack means there
            // is nothing left to come back to, and the caller clears any stored state.
            if (resumePoint is not null)
                resumePoint.Pending = Capture(stack, request, files, folders, bytes, errors);
        }

        MaybeReport(progress, ref lastReportMs, stopwatch, currentPath, files, folders, bytes, errors, force: true);
        return new ScanOutcome(bytes, files, folders, errors);
    }

    /// <summary>
    /// The folders still on the stack, outermost first, with the totals each had reached. Returns
    /// null once the walk is done, which is what tells the caller the scan is not resumable.
    /// </summary>
    private static ScanResumeState? Capture(
        Stack<Frame> stack, ScanRequest request, long files, long folders, long bytes, int errors)
    {
        if (stack.Count == 0)
            return null;

        // A Stack enumerates top-down; the resume order is bottom-up.
        var frames = stack.ToArray();
        Array.Reverse(frames);

        return new ScanResumeState
        {
            FilesScanned = files,
            FoldersScanned = folders,
            BytesScanned = bytes,
            ErrorCount = errors,
            ExcludedPaths = [.. request.ExcludedPaths],
            ExcludedExtensions = [.. request.ExcludedExtensions],
            Pending = [.. frames.Select(f => new ResumeFolder
            {
                Path = f.Dir.FullName,
                Id = f.Id,
                ParentId = f.ParentId,
                Depth = f.Depth,
                Size = f.Size,
                Allocated = f.Allocated,
                FileCount = f.FileCount,
                FolderCount = f.FolderCount
            })]
        };
    }

    private ScanItem BuildItem(
        FileSystemInfo info, string id, string? parentId, int depth, string sessionId,
        ItemType type, long size, long allocated, int fileCount, int folderCount, bool isReparse)
    {
        var attributes = SafeAttributes(info);
        return new ScanItem
        {
            Id = id,
            SessionId = sessionId,
            ParentId = parentId,
            Path = info.FullName,
            Name = info.Name,
            Extension = type == ItemType.File ? NullIfEmpty(info.Extension) : null,
            ItemType = type,
            Size = size,
            AllocatedSize = allocated,
            FileCount = fileCount,
            FolderCount = folderCount,
            Depth = depth,
            CreationTime = SafeTime(() => info.CreationTimeUtc),
            LastWriteTime = SafeTime(() => info.LastWriteTimeUtc),
            LastAccessTime = SafeTime(() => info.LastAccessTimeUtc),
            Attributes = attributes,
            IsHidden = (attributes & FileAttributes.Hidden) != 0,
            IsSystem = (attributes & FileAttributes.System) != 0,
            IsReparsePoint = isReparse || (attributes & FileAttributes.ReparsePoint) != 0,
            IsProtected = _protectedPaths.IsProtected(info.FullName)
        };
    }

    private static long MaybeReport(
        IProgress<ScanProgress>? progress, ref long lastReportMs, Stopwatch stopwatch,
        string currentPath, long files, long folders, long bytes, int errors, bool force)
    {
        if (progress is null)
            return lastReportMs;

        long now = stopwatch.ElapsedMilliseconds;
        if (!force && now - lastReportMs < ProgressIntervalMs)
            return lastReportMs;

        progress.Report(new ScanProgress(currentPath, files, folders, bytes, errors, stopwatch.Elapsed));
        return now;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static FileAttributes SafeAttributes(FileSystemInfo info)
    {
        try { return info.Attributes; }
        catch { return 0; }
    }

    private static DateTimeOffset? SafeTime(Func<DateTime> getter)
    {
        try { return new DateTimeOffset(getter(), TimeSpan.Zero); }
        catch { return null; }
    }

    private static string NormalizePath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return path.TrimEnd('\\', '/'); }
    }

    private static bool IsRecoverable(Exception ex) =>
        ex is UnauthorizedAccessException or IOException or SecurityException;

    private static readonly IEnumerator<FileSystemInfo> EmptyEnumerator =
        Enumerable.Empty<FileSystemInfo>().GetEnumerator();

    private static long GetOnDiskSize(string path, long fallback)
    {
        uint low = GetCompressedFileSizeW(path, out uint high);
        if (low == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0)
            return fallback;
        return ((long)high << 32) | low;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    private sealed class Frame
    {
        public Frame(DirectoryInfo dir, string id, string? parentId, int depth)
        {
            Dir = dir;
            Id = id;
            ParentId = parentId;
            Depth = depth;
        }

        /// <summary>
        /// A folder an earlier run left unfinished. It keeps that run's id so the children already
        /// stored under it still belong to it, and the totals it had reached so the finished
        /// subtrees below it are not measured again.
        /// </summary>
        public static Frame Restored(ResumeFolder folder) =>
            new(new DirectoryInfo(folder.Path), folder.Id, folder.ParentId, folder.Depth)
            {
                Size = folder.Size,
                Allocated = folder.Allocated,
                FileCount = folder.FileCount,
                FolderCount = folder.FolderCount,
                _completedChildren = folder.CompletedChildren
            };

        private HashSet<string>? _completedChildren;

        public DirectoryInfo Dir { get; }
        public string Id { get; }
        public string? ParentId { get; }
        public int Depth { get; }
        public IEnumerator<FileSystemInfo>? Enumerator { get; set; }
        public long Size { get; set; }
        public long Allocated { get; set; }
        public int FileCount { get; set; }
        public int FolderCount { get; set; }

        /// <summary>
        /// True when this entry was already written for this folder. Only a restored frame can
        /// answer yes, so a fresh folder pays nothing for the check.
        /// </summary>
        public bool AlreadyStored(string name) => _completedChildren?.Contains(name) == true;

        /// <summary>
        /// Records a child this run has just finished. It matters for the one folder a restored
        /// frame was in the middle of: that folder completes before its parent starts enumerating
        /// again, and without this the parent would meet it a second time and walk it twice.
        /// </summary>
        public void MarkStored(string name) => _completedChildren?.Add(name);
    }
}
