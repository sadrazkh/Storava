namespace Storava.Application.Abstractions;

/// <summary>
/// Gives the disk space that deleted rows leave behind back to the operating system.
/// <para>
/// SQLite does not shrink its file when rows go: the pages are marked free and reused later. That
/// means the file stops growing on its own, which is most of what matters — a discarded scan of a
/// whole drive is gigabytes, and the next scan writes into the room it left rather than extending
/// the file. Handing that room back is a separate, expensive thing, and the timing of it belongs to
/// whoever is sitting in front of the application.
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
