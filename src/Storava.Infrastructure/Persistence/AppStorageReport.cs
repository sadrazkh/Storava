using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;

namespace Storava.Infrastructure.Persistence;

/// <inheritdoc />
public sealed class AppStorageReport : IAppStorageReport
{
    private readonly StoravaDbOptions _options;
    private readonly IScanSessionRepository _sessions;
    private readonly IDatabaseMaintenance _maintenance;
    private readonly ILogger<AppStorageReport> _logger;

    public AppStorageReport(
        StoravaDbOptions options,
        IScanSessionRepository sessions,
        IDatabaseMaintenance maintenance,
        ILogger<AppStorageReport> logger)
    {
        _options = options;
        _sessions = sessions;
        _maintenance = maintenance;
        _logger = logger;
    }

    /// <summary>
    /// Everything lives under one folder, the one holding the database. Derived from that rather than
    /// rebuilt from the environment so this cannot end up describing a different folder than the one
    /// the application is actually using — which is precisely the mistake it exists to catch.
    /// </summary>
    private string Root => Path.GetDirectoryName(Path.GetFullPath(_options.DatabasePath)) ?? string.Empty;

    public IReadOnlyList<AppStorageEntry> Describe()
    {
        var entries = new List<AppStorageEntry>
        {
            Database(),
            Folder(AppStorageKind.Logs, "logs", canClear: true),

            // Not clearable here on purpose. The key is the one thing in this list that cannot be
            // recreated, and it is removed from the AI settings, next to the explanation of what
            // losing it means.
            Folder(AppStorageKind.Secrets, "secrets", canClear: false),

            // Another program's data, which may be running right now.
            Folder(AppStorageKind.Agent, "Agent", canClear: false),
            Folder(AppStorageKind.AccountServer, "Web", canClear: false)
        };

        return entries.OrderByDescending(entry => entry.Bytes).ToArray();
    }

    public async Task<AppStorageClearResult> ClearAsync(
        AppStorageKind kind, CancellationToken cancellationToken = default)
    {
        // Checked against the same description the user was shown, rather than trusting the caller.
        var entry = Describe().FirstOrDefault(candidate => candidate.Kind == kind);
        if (entry is null || !entry.CanClear)
            return AppStorageClearResult.Nothing;

        return kind switch
        {
            AppStorageKind.Scans => await ClearScansAsync(cancellationToken).ConfigureAwait(false),
            AppStorageKind.Logs => ClearLogs(),
            _ => AppStorageClearResult.Nothing
        };
    }

    /// <summary>
    /// Removes every scan, through the repository rather than by deleting the file.
    /// <para>
    /// Going through the repository is what keeps the record of changes made to the user's files: it
    /// removes the measurements, the advice derived from them and the plans built on them, and
    /// deliberately leaves the execution log alone. Deleting the database instead would take that
    /// with it, and it is the one thing in there that cannot be measured again.
    /// </para>
    /// </summary>
    private async Task<AppStorageClearResult> ClearScansAsync(CancellationToken cancellationToken)
    {
        long before = _maintenance.SizeOnDisk();

        var sessions = await _sessions.GetRecentAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);
        foreach (var session in sessions)
            await _sessions.DeleteAsync(session.Id, cancellationToken).ConfigureAwait(false);

        long after = _maintenance.SizeOnDisk();

        _logger.LogInformation("Removed {Count} scan(s) at the user's request.", sessions.Count);

        // The rows are gone but SQLite keeps the pages for reuse, so the file is very likely the same
        // size. Reporting the difference and saying compacting is still needed is the honest answer.
        return new AppStorageClearResult(Math.Max(0, before - after), sessions.Count, NeedsCompacting: true);
    }

    /// <summary>
    /// Deletes the log files, skipping whichever one is currently being written.
    /// <para>
    /// Today's file is held open by the logger. Failing the whole operation over it would be absurd —
    /// the point is to reclaim the old ones — so it is counted as untouched rather than as an error.
    /// </para>
    /// </summary>
    private AppStorageClearResult ClearLogs()
    {
        string directory = Path.Combine(Root, "logs");
        if (!Directory.Exists(directory))
            return AppStorageClearResult.Nothing;

        long freed = 0;
        int removed = 0;

        foreach (var file in new DirectoryInfo(directory).EnumerateFiles())
        {
            try
            {
                long size = file.Length;
                file.Delete();
                freed += size;
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use, almost certainly today's.
            }
        }

        if (removed > 0)
            _logger.LogInformation("Removed {Count} log file(s), freeing {Bytes} bytes.", removed, freed);

        return new AppStorageClearResult(freed, removed, NeedsCompacting: false);
    }

    /// <summary>The database and its write-ahead files, which are one store as far as anyone cares.</summary>
    private AppStorageEntry Database()
    {
        long bytes = 0;
        int files = 0;

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var info = new FileInfo(_options.DatabasePath + suffix);
            if (!info.Exists)
                continue;

            bytes += info.Length;
            files++;
        }

        return new AppStorageEntry(AppStorageKind.Scans, _options.DatabasePath, bytes, files, CanClear: true);
    }

    private AppStorageEntry Folder(AppStorageKind kind, string name, bool canClear)
    {
        string path = Path.Combine(Root, name);
        long bytes = 0;
        int files = 0;

        if (Directory.Exists(path))
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    bytes += file.Length;
                    files++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file that cannot be measured still exists; counting it without a size is
                    // closer to the truth than pretending the folder is smaller than it is.
                    files++;
                }
            }
        }

        return new AppStorageEntry(kind, path, bytes, files, canClear);
    }
}
