using Storava.Application.Abstractions;
using Storava.Application.Scanning;
using Storava.Application.Services;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// Covers carrying on a scan that stopped early. The property that matters is that resuming
/// produces the same numbers as one uninterrupted walk of the same tree: an item counted twice
/// would inflate every folder above it, and an item skipped would understate the whole scan.
/// </summary>
public sealed class ScanResumeTests
{
    /// <summary>A tree deep and wide enough that stopping partway leaves several levels pending.</summary>
    private static void BuildTree(TestTree tree)
    {
        for (int branch = 0; branch < 4; branch++)
        {
            tree.AddFile($@"branch{branch}\top.bin", 100 + branch);
            for (int leaf = 0; leaf < 5; leaf++)
            {
                tree.AddFile($@"branch{branch}\level1\level2\leaf{leaf}.bin", 1000 + leaf);
                tree.AddFile($@"branch{branch}\level1\side{leaf}.bin", 50);
            }
        }

        tree.AddFile("root.bin", 7);
    }

    private static Task<ScanResult> ScanAsync(TestHost host, string root) =>
        host.Get<ScanCoordinator>().RunAsync(
            new ScanRequest { RootPath = root },
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            CancellationToken.None);

    private static Task<ScanResult?> ResumeAsync(TestHost host, string sessionId) =>
        host.Get<ScanCoordinator>().ResumeAsync(
            sessionId,
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            CancellationToken.None);

    /// <summary>
    /// Runs a scan that cancels itself once <paramref name="stopAfter"/> items have been written.
    /// Interrupting at the sink rather than on a timer makes the stopping point exact.
    /// </summary>
    private static async Task<ScanResult> ScanUntilAsync(string databasePath, string root, int stopAfter)
    {
        using var cts = new CancellationTokenSource();
        using var host = new TestHost(
            databasePath: databasePath,
            decorateSink: inner => new InterruptingSinkFactory(inner, stopAfter, cts));

        return await host.Get<ScanCoordinator>().RunAsync(
            new ScanRequest { RootPath = root },
            new Progress<ScanProgress>(),
            new PauseTokenSource().Token,
            cts.Token);
    }

    private static async Task<ScanSession> GetSessionAsync(TestHost host, string sessionId)
    {
        var session = await host.Get<IScanSessionRepository>().GetAsync(sessionId);
        Assert.NotNull(session);
        return session!;
    }

    [Fact]
    public async Task CompletedScan_KeepsNoResumeState()
    {
        using var tree = new TestTree();
        BuildTree(tree);

        using var host = new TestHost();
        var result = await ScanAsync(host, tree.Root);

        var session = await GetSessionAsync(host, result.SessionId);
        Assert.Equal(ScanStatus.Completed, session.Status);
        Assert.Null(session.ResumeState);
        Assert.False(session.CanResume);
    }

    [Fact]
    public async Task InterruptedScan_IsResumable()
    {
        using var tree = new TestTree();
        BuildTree(tree);
        string databasePath = TempDatabasePath();

        try
        {
            var interrupted = await ScanUntilAsync(databasePath, tree.Root, stopAfter: 12);
            Assert.Equal(ScanStatus.Cancelled, interrupted.Status);

            using var host = new TestHost(databasePath: databasePath);
            var session = await GetSessionAsync(host, interrupted.SessionId);

            Assert.True(session.CanResume);
            var state = ScanResumeState.Deserialize(session.ResumeState);
            Assert.NotNull(state);
            Assert.NotEmpty(state!.Pending);

            // Outermost first: the root is the bottom of the stack, the deepest folder the top.
            Assert.Equal(tree.Root, state.Pending[0].Path);
            Assert.Null(state.Pending[0].ParentId);
            Assert.Equal(state.Pending.Select(p => p.Depth).Order(), state.Pending.Select(p => p.Depth));
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    [Fact]
    public async Task Resume_ProducesTheSameTotalsAsOneUninterruptedScan()
    {
        using var tree = new TestTree();
        BuildTree(tree);

        // Reference: the same tree walked in a single pass.
        using var reference = new TestHost();
        var whole = await ScanAsync(reference, tree.Root);

        string databasePath = TempDatabasePath();
        try
        {
            var interrupted = await ScanUntilAsync(databasePath, tree.Root, stopAfter: 15);
            Assert.Equal(ScanStatus.Cancelled, interrupted.Status);
            Assert.True(interrupted.TotalFiles < whole.TotalFiles);

            using var host = new TestHost(databasePath: databasePath);
            var resumed = await ResumeAsync(host, interrupted.SessionId);

            Assert.NotNull(resumed);
            Assert.Equal(ScanStatus.Completed, resumed!.Status);
            Assert.Equal(whole.TotalFiles, resumed.TotalFiles);
            Assert.Equal(whole.TotalFolders, resumed.TotalFolders);
            Assert.Equal(whole.TotalSize, resumed.TotalSize);

            // The finished scan has nothing left to come back to.
            var session = await GetSessionAsync(host, interrupted.SessionId);
            Assert.Null(session.ResumeState);
            Assert.False(session.CanResume);
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    [Fact]
    public async Task Resume_StoresEveryItemExactlyOnce()
    {
        using var tree = new TestTree();
        BuildTree(tree);
        string databasePath = TempDatabasePath();

        try
        {
            var interrupted = await ScanUntilAsync(databasePath, tree.Root, stopAfter: 15);

            using var host = new TestHost(databasePath: databasePath);
            var resumed = await ResumeAsync(host, interrupted.SessionId);
            Assert.NotNull(resumed);

            var items = await host.Get<IScanQueryService>()
                .GetLargestAsync(resumed!.SessionId, 100_000, foldersOnly: false);

            var duplicates = items
                .GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(duplicates.Count == 0, $"Stored twice: {string.Join(", ", duplicates)}");
            Assert.Equal(resumed.TotalFiles + resumed.TotalFolders, items.Count);
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    [Fact]
    public async Task Resume_RebuildsFolderAggregatesCorrectly()
    {
        using var tree = new TestTree();
        BuildTree(tree);

        using var reference = new TestHost();
        var whole = await ScanAsync(reference, tree.Root);
        var expected = (await reference.Get<IScanQueryService>().GetRootsAsync(whole.SessionId)).Single();

        string databasePath = TempDatabasePath();
        try
        {
            var interrupted = await ScanUntilAsync(databasePath, tree.Root, stopAfter: 15);

            using var host = new TestHost(databasePath: databasePath);
            var resumed = await ResumeAsync(host, interrupted.SessionId);
            Assert.NotNull(resumed);

            // The root row is written last and aggregates everything below it, so if any subtree
            // were measured twice or missed, it is this number that would be wrong.
            var actual = (await host.Get<IScanQueryService>().GetRootsAsync(resumed!.SessionId)).Single();
            Assert.Equal(expected.Size, actual.Size);
            Assert.Equal(expected.FileCount, actual.FileCount);
            Assert.Equal(expected.FolderCount, actual.FolderCount);
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    [Fact]
    public async Task Resume_SurvivesBeingInterruptedAgain()
    {
        using var tree = new TestTree();
        BuildTree(tree);

        using var reference = new TestHost();
        var whole = await ScanAsync(reference, tree.Root);

        string databasePath = TempDatabasePath();
        try
        {
            var interrupted = await ScanUntilAsync(databasePath, tree.Root, stopAfter: 10);
            string sessionId = interrupted.SessionId;

            // Stop the resumed run partway as well, then carry on from that.
            using (var cts = new CancellationTokenSource())
            using (var second = new TestHost(
                       databasePath: databasePath,
                       decorateSink: inner => new InterruptingSinkFactory(inner, 10, cts)))
            {
                var again = await second.Get<ScanCoordinator>().ResumeAsync(
                    sessionId, new Progress<ScanProgress>(), new PauseTokenSource().Token, cts.Token);

                Assert.NotNull(again);
                Assert.Equal(ScanStatus.Cancelled, again!.Status);
            }

            using var host = new TestHost(databasePath: databasePath);
            var finished = await ResumeAsync(host, sessionId);

            Assert.NotNull(finished);
            Assert.Equal(ScanStatus.Completed, finished!.Status);
            Assert.Equal(whole.TotalFiles, finished.TotalFiles);
            Assert.Equal(whole.TotalFolders, finished.TotalFolders);
            Assert.Equal(whole.TotalSize, finished.TotalSize);
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    [Fact]
    public async Task Resume_HonorsTheExclusionsTheInterruptedRunUsed()
    {
        using var tree = new TestTree();
        BuildTree(tree);
        tree.AddFile(@"branch0\level1\level2\skip.tmp", 999_999);
        string databasePath = TempDatabasePath();

        try
        {
            using var cts = new CancellationTokenSource();
            using (var first = new TestHost(
                       databasePath: databasePath,
                       decorateSink: inner => new InterruptingSinkFactory(inner, 8, cts)))
            {
                await first.Get<ScanCoordinator>().RunAsync(
                    new ScanRequest { RootPath = tree.Root, ExcludedExtensions = [".tmp"] },
                    new Progress<ScanProgress>(),
                    new PauseTokenSource().Token,
                    cts.Token);
            }

            using var host = new TestHost(databasePath: databasePath);
            var sessions = await host.Get<IScanSessionRepository>().GetRecentAsync(1);
            var resumed = await ResumeAsync(host, sessions[0].Id);

            Assert.NotNull(resumed);
            var items = await host.Get<IScanQueryService>()
                .GetLargestAsync(resumed!.SessionId, 100_000, foldersOnly: false);

            Assert.DoesNotContain(items, i => i.Name == "skip.tmp");
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    [Fact]
    public async Task Resume_OnACompletedScan_DoesNothing()
    {
        using var tree = new TestTree();
        BuildTree(tree);

        using var host = new TestHost();
        var result = await ScanAsync(host, tree.Root);

        Assert.Null(await ResumeAsync(host, result.SessionId));
    }

    [Fact]
    public async Task Resume_OnAnUnknownScan_DoesNothing()
    {
        using var host = new TestHost();
        Assert.Null(await ResumeAsync(host, "no-such-session"));
    }

    [Fact]
    public async Task UnreadableResumeState_IsDiscardedRatherThanGuessedAt()
    {
        using var tree = new TestTree();
        tree.AddFile("a.bin", 10);

        using var host = new TestHost();
        var sessions = host.Get<IScanSessionRepository>();
        var result = await ScanAsync(host, tree.Root);

        var session = await GetSessionAsync(host, result.SessionId);
        session.Status = ScanStatus.Cancelled;
        session.ResumeState = "{ this is not the state we wrote }";
        await sessions.SaveAsync(session);

        Assert.Null(await ResumeAsync(host, result.SessionId));

        // The unusable state is cleared, so nothing keeps offering to resume it.
        var after = await GetSessionAsync(host, result.SessionId);
        Assert.Null(after.ResumeState);
        Assert.False(after.CanResume);
    }

    [Fact]
    public void ResumeState_FromANewerVersion_IsNotAccepted()
    {
        var state = new ScanResumeState
        {
            Version = ScanResumeState.CurrentVersion + 1,
            Pending = [new ResumeFolder { Path = @"C:\x", Id = "1" }]
        };

        Assert.Null(ScanResumeState.Deserialize(state.Serialize()));
    }

    [Fact]
    public void ResumeState_DoesNotCarryTheChildNamesItLoadsFromTheDatabase()
    {
        var folder = new ResumeFolder { Path = @"C:\x", Id = "1" };
        folder.CompletedChildren.Add("already-stored.bin");

        string json = new ScanResumeState { Pending = [folder] }.Serialize();

        // Those names can run to hundreds of thousands; the database already holds them.
        Assert.DoesNotContain("already-stored.bin", json, StringComparison.Ordinal);
    }

    private static string TempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"storava-resume-{Guid.NewGuid():N}.db");

    private static void Cleanup(string databasePath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                string path = databasePath + suffix;
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Cancels the scan once a fixed number of items has reached the real sink.</summary>
    private sealed class InterruptingSinkFactory(
        IScanItemSinkFactory inner, int stopAfter, CancellationTokenSource cancellation) : IScanItemSinkFactory
    {
        public IScanItemSink Create(string sessionId) =>
            new InterruptingSink(inner.Create(sessionId), stopAfter, cancellation);
    }

    private sealed class InterruptingSink(
        IScanItemSink inner, int stopAfter, CancellationTokenSource cancellation) : IScanItemSink
    {
        private int _written;

        public async ValueTask AddAsync(ScanItem item, CancellationToken cancellationToken = default)
        {
            // Written first, then cancelled: this mirrors a real interruption, where everything
            // handed to the sink before the stop is what survives in the database.
            await inner.AddAsync(item, cancellationToken).ConfigureAwait(false);

            if (++_written >= stopAfter)
                await cancellation.CancelAsync().ConfigureAwait(false);
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default) =>
            inner.CompleteAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
