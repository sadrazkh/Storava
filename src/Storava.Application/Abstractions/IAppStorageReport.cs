namespace Storava.Application.Abstractions;

/// <summary>
/// What Storava itself has put on the disk, and which of it can be thrown away.
/// <para>
/// An application whose whole subject is disk usage should be able to answer the question about
/// itself. On the machine this was written for the answer was six and a half gigabytes of scan
/// database against a few hundred kilobytes of everything else — worth being able to see rather than
/// having to go and look in a folder.
/// </para>
/// </summary>
public interface IAppStorageReport
{
    /// <summary>Every store, largest first. Reads sizes off the disk; changes nothing.</summary>
    IReadOnlyList<AppStorageEntry> Describe();

    /// <summary>
    /// Empties one store and reports how many bytes went.
    /// <para>
    /// Refuses anything whose <see cref="AppStorageEntry.CanClear"/> is false rather than trusting
    /// the caller: the same list is shown to the user, and the reasons a store is not clearable are
    /// reasons, not presentation.
    /// </para>
    /// </summary>
    Task<AppStorageClearResult> ClearAsync(AppStorageKind kind, CancellationToken cancellationToken = default);
}

/// <summary>The stores Storava creates. Each one is somebody's data, so each is named.</summary>
public enum AppStorageKind
{
    /// <summary>The scan database: measurements, the advice derived from them, and the plans.</summary>
    Scans = 0,

    /// <summary>Diagnostic logs. The only store here that exists purely for the developers.</summary>
    Logs,

    /// <summary>The encrypted API key. Tiny, and the one thing here that cannot be recreated.</summary>
    Secrets,

    /// <summary>The companion Agent's own database and logs, on the same machine.</summary>
    Agent,

    /// <summary>The account server's identity database, if this machine has run one.</summary>
    AccountServer
}

/// <summary>One store, as it is right now.</summary>
/// <param name="Kind">Which store.</param>
/// <param name="Location">Where it is, so somebody can go and look.</param>
/// <param name="Bytes">What it occupies, including any side files.</param>
/// <param name="FileCount">How many files it is spread over. Zero means the store does not exist yet.</param>
/// <param name="CanClear">
/// Whether this application may empty it. False for the Agent's and the account server's data —
/// those belong to other programs that may be running — and for the stored key, which is removed
/// from the AI settings where its consequences are explained.
/// </param>
public sealed record AppStorageEntry(
    AppStorageKind Kind,
    string Location,
    long Bytes,
    int FileCount,
    bool CanClear)
{
    public bool Exists => FileCount > 0;
}

/// <summary>What clearing did.</summary>
/// <param name="BytesFreed">How much smaller the store became.</param>
/// <param name="Removed">How many files or scans went.</param>
/// <param name="NeedsCompacting">
/// True when the room was freed inside the database rather than given back to the operating system,
/// so the file on disk is the same size until it is compacted. Saying otherwise would have the user
/// looking at an unchanged number wondering what happened.
/// </param>
public sealed record AppStorageClearResult(long BytesFreed, int Removed, bool NeedsCompacting)
{
    public static readonly AppStorageClearResult Nothing = new(0, 0, false);
}
