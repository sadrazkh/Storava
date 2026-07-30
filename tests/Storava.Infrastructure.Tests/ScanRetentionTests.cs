using Microsoft.Extensions.Logging.Abstractions;
using Storava.Application.Abstractions;
using Storava.Application.History;
using Storava.Application.Scanning;
using Storava.Application.Services;
using Storava.Infrastructure.Persistence;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// Retention discards old scans so the database stops growing without limit.
/// <para>
/// This is the one feature here that deletes data on its own initiative, so what it must
/// <em>not</em> take is as much the point as what it takes: never the scan being looked at, never
/// one with an unfinished run settled against it, and never the record of changes actually made to
/// the user's files.
/// </para>
/// </summary>
public class ScanRetentionTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task OnlyTheNewestScansSurvive()
    {
        await GivenScans(6);

        var result = await Retention().ApplyAsync(keep: 3);

        Assert.Equal(3, result.RemovedCount);

        var left = await Sessions().GetRecentAsync(50);
        Assert.Equal(3, left.Count);
        Assert.Equal(["scan-5", "scan-4", "scan-3"], left.Select(s => s.Id));
    }

    /// <summary>Fewer scans than the limit is not an occasion to delete anything.</summary>
    [Fact]
    public async Task NothingHappensWhenThereIsRoomToSpare()
    {
        await GivenScans(3);

        var result = await Retention().ApplyAsync(keep: 3);

        Assert.False(result.RemovedAnything);
        Assert.Equal(3, (await Sessions().GetRecentAsync(50)).Count);
    }

    /// <summary>
    /// The scan on screen stays whatever its age. Deleting the session a page is reading would
    /// empty that page underneath the user.
    /// </summary>
    [Fact]
    public async Task TheScanBeingViewedIsNotDiscarded()
    {
        await GivenScans(6);

        await Retention().ApplyAsync(keep: 2, protectedSessionId: "scan-0");

        var left = (await Sessions().GetRecentAsync(50)).Select(s => s.Id).ToList();
        Assert.Contains("scan-0", left);
        Assert.Equal(3, left.Count); // the two newest, plus the one being viewed
    }

    /// <summary>
    /// A run that stopped mid-step is settled against the scan that produced it: a file already
    /// copied but not yet linked is reconciled through the plan entry. Removing the scan would
    /// leave that half-done change with nothing to finish it.
    /// </summary>
    [Fact]
    public async Task AScanWithAnInterruptedRunIsKept()
    {
        await GivenScans(6);
        await GivenRun("scan-1", ExecutionStatus.Running);

        await Retention().ApplyAsync(keep: 3);

        var left = (await Sessions().GetRecentAsync(50)).Select(s => s.Id).ToList();
        Assert.Contains("scan-1", left);
        Assert.DoesNotContain("scan-0", left);
    }

    /// <summary>A run that finished is no reason to keep the scan: there is nothing left to settle.</summary>
    [Fact]
    public async Task AScanWhoseRunFinishedIsDiscardedNormally()
    {
        await GivenScans(6);
        await GivenRun("scan-1", ExecutionStatus.Completed);

        await Retention().ApplyAsync(keep: 3);

        Assert.DoesNotContain("scan-1", (await Sessions().GetRecentAsync(50)).Select(s => s.Id));
    }

    /// <summary>
    /// The audit trail outlives the scan that suggested it. What was done to the user's files is
    /// not a measurement that can be taken again, and losing it silently would be the worst
    /// possible outcome of a feature whose whole job is deleting things.
    /// </summary>
    [Fact]
    public async Task TheRecordOfWhatWasDoneToTheDiskSurvives()
    {
        await GivenScans(6);
        await GivenRun("scan-0", ExecutionStatus.Completed);

        await Retention().ApplyAsync(keep: 3);

        Assert.DoesNotContain("scan-0", (await Sessions().GetRecentAsync(50)).Select(s => s.Id));

        var run = await _host.Get<IPlanExecutionRepository>().GetAsync("run-scan-0");
        Assert.NotNull(run);
        Assert.Single(run.Steps);
        Assert.Equal(@"C:\already-moved", run.Steps[0].SourcePath);
    }

    /// <summary>Keeping zero would mean deleting the scan the user just took. One is the floor.</summary>
    [Fact]
    public async Task KeepingNoneIsTreatedAsKeepingOne()
    {
        await GivenScans(4);

        await Retention().ApplyAsync(keep: 0);

        Assert.Single(await Sessions().GetRecentAsync(50));
    }

    /// <summary>
    /// A discarded scan takes its plan with it.
    /// <para>
    /// A plan is a document about one scan and is reachable only through it, so one left behind is
    /// unreachable by definition. That was a slow leak while deleting was something a person did by
    /// hand; once retention began discarding scans on its own it became unbounded — every scan ever
    /// taken leaving its plan and every entry in it, which is the growth retention exists to stop.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ThePlanOfADiscardedScanGoesWithIt()
    {
        await GivenScans(4);
        await GivenPlan("scan-0");
        await GivenPlan("scan-3");

        await Retention().ApplyAsync(keep: 1);

        var plans = _host.Get<IStoragePlanRepository>();
        Assert.Null(await plans.GetForSessionAsync("scan-0"));
        Assert.NotNull(await plans.GetForSessionAsync("scan-3"));
    }

    /// <summary>
    /// And its entries, not only the plan row. Rows keyed by plan id rather than session id are the
    /// ones a delete written per table is most likely to miss.
    /// </summary>
    [Fact]
    public async Task ThePlanEntriesGoWithTheScanToo()
    {
        await GivenScans(2);
        await GivenPlan("scan-0");

        await Sessions().DeleteAsync("scan-0");

        var orphaned = await _host.Get<DatabaseGateway>().RunAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM StoragePlanEntries;";
            return Convert.ToInt64(await command.ExecuteScalarAsync(token));
        });

        Assert.Equal(0, orphaned);
    }

    /// <summary>
    /// Retention deletes; it does not rewrite the file.
    /// <para>
    /// Compacting takes an exclusive lock for its whole duration, and measuring it showed a query
    /// issued meanwhile waiting for the entire rewrite — around half a minute on a database of the
    /// size this exists to deal with, arriving the instant a scan finishes and somebody goes to
    /// read the results. Giving the room back to the operating system is a button on the Settings
    /// page instead, because it is a wait somebody should be choosing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DiscardingScansDoesNotRewriteTheWholeFile()
    {
        var maintenance = new CountingMaintenance();

        // Built against this class's own host, not a fresh one: GivenScans writes to that database,
        // and a second host would be a second database with nothing in it.
        var retention = new ScanRetentionService(
            _host.Get<IScanSessionRepository>(),
            _host.Get<IPlanExecutionRepository>(),
            NullLogger<ScanRetentionService>.Instance);

        await GivenScans(6);
        var removed = await retention.ApplyAsync(keep: 3);

        Assert.Equal(0, maintenance.Compactions);
        Assert.True(removed.RemovedAnything, "the deletes themselves still have to happen");
    }

    /// <summary>Counts compactions, because the point is that none happen unasked.</summary>
    private sealed class CountingMaintenance : IDatabaseMaintenance
    {
        public int Compactions { get; private set; }

        public long SizeOnDisk() => 0;

        public Task<long> CompactAsync(CancellationToken cancellationToken = default)
        {
            Compactions++;
            return Task.FromResult(0L);
        }
    }

    /// <summary>The scan items go with the session, which is where nearly all of the size was.</summary>
    [Fact]
    public async Task TheItemsOfADiscardedScanGoWithIt()
    {
        await GivenScans(4);
        await GivenItems("scan-0", 5);
        await GivenItems("scan-3", 5);

        await Retention().ApplyAsync(keep: 1);

        var query = _host.Get<IScanQueryService>();
        Assert.Empty(await query.GetRootsAsync("scan-0"));
        Assert.Equal(5, (await query.GetRootsAsync("scan-3")).Count);
    }

    /// <summary>
    /// Retention has to be automatic to be worth anything: the database grew past six gigabytes
    /// precisely because pruning it was something a person had to remember to do. This drives real
    /// scans through the coordinator rather than calling the service, so it fails if the wiring is
    /// removed even while the service itself still works perfectly.
    /// </summary>
    [Fact]
    public async Task ScanningRepeatedlyPrunesTheOlderScansByItself()
    {
        using var tree = new TestTree();
        tree.AddFile("a.bin", 512);

        var coordinator = _host.Get<ScanCoordinator>();
        var ids = new List<string>();

        for (var i = 0; i < 5; i++)
        {
            var result = await coordinator.RunAsync(
                new ScanRequest { RootPath = tree.Root },
                new Progress<ScanProgress>(),
                new PauseTokenSource().Token,
                CancellationToken.None);

            ids.Add(result.SessionId);

            // The scan does not wait for housekeeping, so the assertion has to.
            await coordinator.RetentionInProgress;
        }

        var left = (await Sessions().GetRecentAsync(50)).Select(s => s.Id).ToList();

        Assert.Equal(3, left.Count);
        Assert.Equal(ids.TakeLast(3).Reverse(), left);
    }

    // --- setup -----------------------------------------------------------------------------

    private ScanRetentionService Retention() => _host.Get<ScanRetentionService>();

    private IScanSessionRepository Sessions() => _host.Get<IScanSessionRepository>();

    /// <summary>Scans numbered oldest-first, so scan-0 is the first to go.</summary>
    private async Task GivenScans(int count)
    {
        var sessions = Sessions();

        for (var i = 0; i < count; i++)
        {
            await sessions.SaveAsync(new ScanSession
            {
                Id = $"scan-{i}",
                RootPath = @"C:\",
                Mode = ScanMode.Deep,
                Status = ScanStatus.Completed,
                StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i),
                CompletedAt = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero).AddDays(i)
            });
        }
    }

    /// <summary>A saved plan with one entry, which is what a discarded scan must not leave behind.</summary>
    private async Task GivenPlan(string sessionId)
    {
        var plan = new StoragePlan
        {
            Id = $"plan-{sessionId}",
            SessionId = sessionId
        };

        plan.TryAdd(
            new PlanCandidate
            {
                SessionId = sessionId,
                ScanItemId = $"item-{sessionId}",
                RecommendationId = $"rec-{sessionId}",
                Path = $@"C:\{sessionId}
ode_modules",
                Title = "node_modules",
                EstimatedSpace = 1024,
                RiskLevel = RiskLevel.Low,
                IsIdentified = true,
                CanDelete = true
            },
            SuggestedAction.Delete,
            $"entry-{sessionId}");

        await _host.Get<IStoragePlanRepository>().SaveAsync(plan);
    }

    private async Task GivenRun(string sessionId, ExecutionStatus status)
    {
        var execution = new PlanExecution
        {
            Id = $"run-{sessionId}",
            PlanId = $"plan-{sessionId}",
            SessionId = sessionId
        };

        execution.Add(new PlanExecutionStep
        {
            Id = $"step-{sessionId}",
            ExecutionId = execution.Id,
            PlanEntryId = $"entry-{sessionId}",
            ScanItemId = $"item-{sessionId}",
            SourcePath = @"C:\already-moved",
            Title = "A folder that was moved",
            Action = SuggestedAction.Move,
            Status = status
        });

        await _host.Get<IPlanExecutionRepository>().SaveAsync(execution);
    }

    private async Task GivenItems(string sessionId, int count)
    {
        await using var sink = _host.Get<IScanItemSinkFactory>().Create(sessionId);

        for (var i = 0; i < count; i++)
        {
            // Roots (no parent), so the assertion can count them back without walking a tree.
            await sink.AddAsync(new ScanItem
            {
                Id = $"{sessionId}-item-{i}",
                SessionId = sessionId,
                Path = $@"C:\{sessionId}\{i}",
                Name = i.ToString(),
                ItemType = ItemType.File,
                Size = 1024
            });
        }

        await sink.CompleteAsync();
    }
}
