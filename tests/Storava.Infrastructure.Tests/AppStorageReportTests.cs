using Microsoft.Extensions.Logging.Abstractions;
using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Infrastructure.Persistence;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// The report on Storava's own footprint. The interesting parts are what it refuses to touch and
/// what it tells the truth about after clearing — a store this class emptied whose file did not
/// shrink is not a bug, but reporting it as freed space would be.
/// </summary>
public class AppStorageReportTests : IDisposable
{
    private readonly string _root;
    private readonly StoravaDbOptions _options;

    public AppStorageReportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "storava-app-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        string database = Path.Combine(_root, "storava.db");
        _options = new StoravaDbOptions
        {
            DatabasePath = database,
            ConnectionString = $"Data Source={database}"
        };
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private AppStorageReport Build(FakeSessions? sessions = null, FakeMaintenance? maintenance = null) =>
        new(_options,
            sessions ?? new FakeSessions(),
            maintenance ?? new FakeMaintenance(),
            NullLogger<AppStorageReport>.Instance);

    private void WriteFile(string relative, int bytes)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    [Fact]
    public void EveryStoreIsDescribed()
    {
        var kinds = Build().Describe().Select(entry => entry.Kind).ToList();

        Assert.Equal(Enum.GetValues<AppStorageKind>().Length, kinds.Count);
        Assert.Equal(Enum.GetValues<AppStorageKind>().ToHashSet(), kinds.ToHashSet());
    }

    /// <summary>The whole point of the list is to show where the room went, so the biggest goes first.</summary>
    [Fact]
    public void TheLargestStoreComesFirst()
    {
        WriteFile("storava.db", 400);
        WriteFile(Path.Combine("logs", "today.log"), 900);

        var entries = Build().Describe();

        Assert.Equal(AppStorageKind.Logs, entries[0].Kind);
        Assert.True(entries.Zip(entries.Skip(1)).All(pair => pair.First.Bytes >= pair.Second.Bytes));
    }

    /// <summary>The write-ahead files are part of what the database occupies, not separate rows.</summary>
    [Fact]
    public void TheDatabaseIncludesItsWriteAheadFiles()
    {
        WriteFile("storava.db", 100);
        WriteFile("storava.db-wal", 30);
        WriteFile("storava.db-shm", 7);

        var scans = Build().Describe().Single(entry => entry.Kind == AppStorageKind.Scans);

        Assert.Equal(137, scans.Bytes);
        Assert.Equal(3, scans.FileCount);
    }

    [Fact]
    public void AStoreThatDoesNotExistYetReportsNothing()
    {
        var logs = Build().Describe().Single(entry => entry.Kind == AppStorageKind.Logs);

        Assert.Equal(0, logs.Bytes);
        Assert.Equal(0, logs.FileCount);
        Assert.False(logs.Exists);
    }

    [Fact]
    public void SubfoldersCountTowardsAStore()
    {
        WriteFile(Path.Combine("Agent", "nested", "deep.log"), 64);

        var agent = Build().Describe().Single(entry => entry.Kind == AppStorageKind.Agent);

        Assert.Equal(64, agent.Bytes);
        Assert.Equal(1, agent.FileCount);
    }

    [Theory]
    [InlineData(AppStorageKind.Scans, true)]
    [InlineData(AppStorageKind.Logs, true)]
    [InlineData(AppStorageKind.Secrets, false)]
    [InlineData(AppStorageKind.Agent, false)]
    [InlineData(AppStorageKind.AccountServer, false)]
    public void OnlyThisApplicationsOwnScansAndLogsMayBeCleared(AppStorageKind kind, bool expected)
    {
        var entry = Build().Describe().Single(candidate => candidate.Kind == kind);

        Assert.Equal(expected, entry.CanClear);
    }

    /// <summary>
    /// The stored key and the other editions' data are refused at this layer, not only greyed out in
    /// the UI. A caller that asks anyway must be turned down.
    /// </summary>
    [Theory]
    [InlineData(AppStorageKind.Secrets)]
    [InlineData(AppStorageKind.Agent)]
    [InlineData(AppStorageKind.AccountServer)]
    public async Task ClearingARefusedStoreDoesNothing(AppStorageKind kind)
    {
        WriteFile(Path.Combine("secrets", "key.bin"), 48);
        WriteFile(Path.Combine("Agent", "agent-scans.db"), 48);
        WriteFile(Path.Combine("Web", "storava-accounts.db"), 48);

        var result = await Build().ClearAsync(kind);

        Assert.Equal(AppStorageClearResult.Nothing, result);
        Assert.Equal(3, Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task ClearingLogsRemovesThemAndReportsWhatWent()
    {
        WriteFile(Path.Combine("logs", "old.log"), 500);
        WriteFile(Path.Combine("logs", "older.log"), 250);

        var result = await Build().ClearAsync(AppStorageKind.Logs);

        Assert.Equal(750, result.BytesFreed);
        Assert.Equal(2, result.Removed);
        Assert.False(result.NeedsCompacting);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "logs")));
    }

    /// <summary>
    /// The logger holds today's file open. Losing the whole operation over the one file that cannot
    /// go would defeat the purpose, so it is counted as untouched and the rest still go.
    /// </summary>
    [Fact]
    public async Task ALogFileInUseIsLeftBehindWithoutFailingTheRest()
    {
        WriteFile(Path.Combine("logs", "old.log"), 500);
        WriteFile(Path.Combine("logs", "today.log"), 100);

        using var held = new FileStream(
            Path.Combine(_root, "logs", "today.log"), FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = await Build().ClearAsync(AppStorageKind.Logs);

        Assert.Equal(500, result.BytesFreed);
        Assert.Equal(1, result.Removed);
        Assert.True(File.Exists(Path.Combine(_root, "logs", "today.log")));
    }

    /// <summary>
    /// Through the repository, one session at a time. Deleting the file instead would be faster and
    /// would take the record of changes made to the user's files with it.
    /// </summary>
    [Fact]
    public async Task ClearingScansGoesThroughTheRepository()
    {
        WriteFile("storava.db", 4096);
        var sessions = new FakeSessions("a", "b", "c");

        var result = await Build(sessions).ClearAsync(AppStorageKind.Scans);

        Assert.Equal(new[] { "a", "b", "c" }, sessions.Deleted);
        Assert.Equal(3, result.Removed);
    }

    /// <summary>
    /// Deleting the rows only frees pages inside the file, so the room is handed back in the same
    /// act. Without this the file stays exactly as large as it was and Empty looks like it did
    /// nothing — which is what was reported.
    /// </summary>
    [Fact]
    public async Task ClearingScansHandsTheRoomBack()
    {
        var maintenance = new FakeMaintenance(4096, 1024);

        var result = await Build(new FakeSessions("only"), maintenance).ClearAsync(AppStorageKind.Scans);

        Assert.Equal(1, maintenance.Compactions);
        Assert.False(result.NeedsCompacting);
        Assert.Equal(3072, result.BytesFreed);
    }

    /// <summary>
    /// A compaction that cannot run — most often for want of free disk to write the copy — does not
    /// undo the clear. The scans are gone either way; what is left is to say the room has not come
    /// back, so an unchanged number is explained rather than mysterious.
    /// </summary>
    [Fact]
    public async Task ScansStillGoWhenTheRoomCannotBeHandedBack()
    {
        var maintenance = new FakeMaintenance(4096, 4096) { CompactThrows = true };
        var sessions = new FakeSessions("only");

        var result = await Build(sessions, maintenance).ClearAsync(AppStorageKind.Scans);

        Assert.Equal(["only"], sessions.Deleted);
        Assert.True(result.NeedsCompacting);
        Assert.Equal(0, result.BytesFreed);
    }

    /// <summary>The shrink is reported, and never as a negative number.</summary>
    [Theory]
    [InlineData(4096, 1024, 3072)]
    [InlineData(1024, 4096, 0)]
    public async Task ClearingScansReportsTheDifferenceOnDisk(long before, long after, long expected)
    {
        var result = await Build(new FakeSessions("only"), new FakeMaintenance(before, after))
            .ClearAsync(AppStorageKind.Scans);

        Assert.Equal(expected, result.BytesFreed);
    }

    // --- doubles ---------------------------------------------------------------------------------

    private sealed class FakeSessions : IScanSessionRepository
    {
        private readonly List<string> _ids;

        public FakeSessions(params string[] ids) => _ids = [.. ids];

        public List<string> Deleted { get; } = [];

        public Task<IReadOnlyList<ScanSession>> GetRecentAsync(
            int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScanSession>>(
                _ids.Take(count).Select(id => new ScanSession { Id = id }).ToArray());

        public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Deleted.Add(sessionId);
            _ids.Remove(sessionId);
            return Task.CompletedTask;
        }

        public Task SaveAsync(ScanSession session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ScanSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMaintenance : IDatabaseMaintenance
    {
        private readonly Queue<long> _sizes;

        public FakeMaintenance(params long[] sizes) => _sizes = new Queue<long>(sizes);

        public int Compactions { get; private set; }

        /// <summary>Set to make compacting fail the way a full disk makes it fail.</summary>
        public bool CompactThrows { get; init; }

        public long SizeOnDisk() => _sizes.Count > 0 ? _sizes.Dequeue() : 0;

        public Task<long> CompactAsync(CancellationToken cancellationToken = default)
        {
            if (CompactThrows)
                throw new IOException("not enough room to write the rewritten copy");

            Compactions++;
            return Task.FromResult(0L);
        }
    }
}
