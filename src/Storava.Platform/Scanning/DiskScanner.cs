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
        long files = 0, folders = 0, bytes = 0;
        int errors = 0;
        long lastReportMs = -ProgressIntervalMs;
        string currentPath = rootDir.FullName;

        var stack = new Stack<Frame>();
        stack.Push(new Frame(rootDir, NewId(), parentId: null, depth: 0));

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
                }

                lastReportMs = MaybeReport(progress, ref lastReportMs, stopwatch, currentPath, files, folders, bytes, errors, force: false);
            }
        }

        MaybeReport(progress, ref lastReportMs, stopwatch, currentPath, files, folders, bytes, errors, force: true);
        return new ScanOutcome(bytes, files, folders, errors);
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

        public DirectoryInfo Dir { get; }
        public string Id { get; }
        public string? ParentId { get; }
        public int Depth { get; }
        public IEnumerator<FileSystemInfo>? Enumerator { get; set; }
        public long Size { get; set; }
        public long Allocated { get; set; }
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
    }
}
