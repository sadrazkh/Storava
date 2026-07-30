using System.Threading;
using Storava.Application.Abstractions;
using Storava.Infrastructure.Persistence;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// Database work must not run on the thread that asked for it.
/// <para>
/// This is the regression that came back. Microsoft.Data.Sqlite's asynchronous methods are
/// synchronous underneath — SQLite has no async file I/O, so awaiting them never yields — which
/// means a page awaiting a query runs that query on the UI thread. It was fixed once in the query
/// service, the repositories kept doing it, and measuring the running application later showed the
/// interface frozen for up to eighteen seconds while a page opened.
/// </para>
/// <para>
/// So the guarantee is tested at the gateway every repository now goes through, rather than trusted
/// to each method remembering.
/// </para>
/// </summary>
public class DatabaseGatewayTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>
    /// Run from a dedicated thread of our own, which is never a pool thread, so "it moved" is a
    /// real observation rather than a coincidence of which pool thread got picked.
    /// </summary>
    [Fact]
    public void WorkLeavesTheCallingThread()
    {
        var gateway = _host.Get<DatabaseGateway>();

        int callerThread = 0;
        int workThread = 0;

        var thread = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;

            gateway.RunAsync((_, _) =>
            {
                workThread = Environment.CurrentManagedThreadId;
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
        });

        thread.Start();
        thread.Join();

        Assert.NotEqual(0, callerThread);
        Assert.NotEqual(0, workThread);
        Assert.NotEqual(callerThread, workThread);
    }

    /// <summary>Every repository goes through it, so the same has to hold through a real query.</summary>
    [Fact]
    public void ARepositoryQueryAlsoLeavesTheCallingThread()
    {
        var sessions = _host.Get<IScanSessionRepository>();
        var pool = new HashSet<int>();

        int callerThread = 0;

        var thread = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;

            // Reading is enough: the freeze was in page loads, which only read.
            _ = sessions.GetRecentAsync(5).ContinueWith(
                t => pool.Add(Environment.CurrentManagedThreadId),
                TaskContinuationOptions.ExecuteSynchronously).GetAwaiter().GetResult();
        });

        thread.Start();
        thread.Join();

        Assert.DoesNotContain(callerThread, pool);
    }

    /// <summary>
    /// Compacting has to leave the caller's thread too.
    /// <para>
    /// It does not go through the gateway — VACUUM has to be the only thing holding the file — which
    /// means it has to remember on its own, and it did not. That was invisible while only a
    /// background pass called it. The moment it became a button on the Settings page it became a
    /// whole-file rewrite on the UI thread: a window frozen for as long as the rewrite takes, which
    /// on a database of the size this exists for is around half a minute.
    /// </para>
    /// </summary>
    [Fact]
    public void CompactingLeavesTheCallingThread()
    {
        var maintenance = _host.Get<IDatabaseMaintenance>();

        int callerThread = 0;
        int workThread = 0;

        // A dedicated thread, which is never a pool thread, so "it moved" is a real observation
        // rather than a coincidence of which pool thread got picked.
        var thread = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;

            maintenance.CompactAsync()
                .ContinueWith(
                    _ => workThread = Environment.CurrentManagedThreadId,
                    TaskContinuationOptions.ExecuteSynchronously)
                .GetAwaiter()
                .GetResult();
        });

        thread.Start();
        thread.Join();

        Assert.NotEqual(0, callerThread);
        Assert.NotEqual(0, workThread);
        Assert.NotEqual(callerThread, workThread);
    }

    /// <summary>The connection only exists inside the callback, which is what makes it unforgettable.</summary>
    [Fact]
    public async Task TheGatewayOpensAUsableConnection()
    {
        var gateway = _host.Get<DatabaseGateway>();

        var count = await gateway.RunAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM ScanSessions;";
            return Convert.ToInt64(await command.ExecuteScalarAsync(token));
        });

        Assert.Equal(0, count);
    }
}
