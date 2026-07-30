namespace Storava.Application.Abstractions;

/// <summary>
/// Reclaims the disk space that deleted rows leave behind.
/// <para>
/// SQLite does not shrink its file when rows go: the pages are marked free and reused later. For
/// most data that is exactly right. For scan items it is not — a discarded scan of a whole drive is
/// gigabytes, and leaving the file that size would mean retention freed nothing the user can see.
/// </para>
/// </summary>
public interface IDatabaseMaintenance
{
    /// <summary>How much room the database is taking on disk right now, including its side files.</summary>
    long SizeOnDisk();

    /// <summary>
    /// Rewrites the database compactly and reports how many bytes the file lost.
    /// <para>
    /// Something a person asks for, never something that happens behind them. SQLite rewrites the
    /// whole file under an exclusive lock, and measuring it showed every query issued meanwhile
    /// waiting for the entire rewrite — around half a minute on a database of the size this exists
    /// to deal with. It also needs temporary room roughly equal to the database, on a machine whose
    /// owner is by definition short of space.
    /// </para>
    /// <para>
    /// Not doing it is cheap: the pages that deleted rows freed are reused, so the file stops
    /// growing regardless. This is only about giving that room back to the operating system.
    /// </para>
    /// <para>
    /// Failure is reported as zero rather than thrown — a database that could not be compacted is
    /// untidy, not broken.
    /// </para>
    /// </summary>
    Task<long> CompactAsync(CancellationToken cancellationToken = default);
}
