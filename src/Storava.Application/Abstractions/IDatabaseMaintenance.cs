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
    /// <summary>
    /// Rewrites the database compactly and reports how many bytes the file lost.
    /// <para>
    /// Slow and not free: it needs temporary room roughly the size of the database, so it is worth
    /// doing after a scan is discarded and not on a schedule. Failure is reported as zero rather
    /// than thrown — a database that could not be compacted is untidy, not broken.
    /// </para>
    /// </summary>
    Task<long> CompactAsync(CancellationToken cancellationToken = default);
}
