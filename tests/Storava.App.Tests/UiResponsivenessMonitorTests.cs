using System.Threading;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Storava.App.Diagnostics;

namespace Storava.App.Tests;

/// <summary>
/// The watcher has to actually notice a stall, and has to survive the window closing under it.
/// <para>
/// The first version of this file asserted only that nothing threw while shutting down. That was
/// true of every version of the code, including the ones it was supposed to reject, so it caught
/// nothing. Checking showed why: a shut-down dispatcher returns an aborted operation from
/// <c>BeginInvoke</c> rather than throwing, and only the blocking <c>Invoke</c> throws — which the
/// monitor deliberately never calls. What is worth pinning is the measurement itself.
/// </para>
/// <para>
/// What these do pin, from running them against deliberately broken versions: a stall going
/// unreported fails them, a stopwatch started at the wrong moment fails them, and removing the
/// threshold so an idle interface looks busy fails them. What they do not pin is the priority the
/// probe is posted at — these block the dispatcher outright, so a probe sent at any priority queues
/// behind it just the same. Posting at a higher priority would let the probe jump ahead of ordinary
/// work and under-report, and nothing here would notice.
/// </para>
/// </summary>
public class UiResponsivenessMonitorTests
{
    /// <summary>
    /// The whole point: work that holds the UI thread is seen, and reported as roughly its real
    /// length. Without this the monitor could report nothing at all and every other test would pass.
    /// <para>
    /// The block is held open until the monitor's own timer has posted a probe, rather than for a
    /// fixed stretch that the timer is assumed to fire inside. That assumption is what failed in CI:
    /// a machine running ten test assemblies at once can leave a thread-pool timer callback waiting
    /// longer than the block lasted, so no probe was ever outstanding while the thread was stuck and
    /// a real stall went unmeasured. Waiting for the condition instead of for a duration means load
    /// makes this slower and never wrong — and because it waits for the real timer rather than
    /// reaching past it, a monitor that never armed one still fails here.
    /// </para>
    /// </summary>
    [Fact]
    public void WorkThatHoldsTheInterfaceIsNoticed()
    {
        var error = RunWithDispatcher(dispatcher =>
        {
            using var monitor = new UiResponsivenessMonitor(
                dispatcher, NullLogger<UiResponsivenessMonitor>.Instance);

            using var blocking = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();

            // Queued, not invoked: this thread has to stay free to release it again.
            dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                blocking.Set();
                release.Wait(TimeSpan.FromSeconds(20));
            });

            Assert.True(blocking.Wait(TimeSpan.FromSeconds(10)), "the interface never became busy");
            Assert.True(WaitForOutstandingProbe(monitor), "the monitor never probed a busy interface");

            // Held past the 200 ms the monitor treats as perceptible. Measured from when the probe
            // above started, so this is a floor on the stall, never a ceiling.
            Thread.Sleep(500);
            release.Set();

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (monitor.StallCount == 0 && DateTime.UtcNow < deadline)
                Thread.Sleep(20);

            Assert.True(monitor.StallCount > 0, "a half-second block went unreported");
            Assert.True(
                monitor.WorstStall > TimeSpan.FromMilliseconds(300),
                $"the stall was reported as only {monitor.WorstStall.TotalMilliseconds:F0} ms");
        });

        Assert.Null(error);
    }

    /// <summary>
    /// An interface that is free is not accused of stalling.
    /// <para>
    /// Driven through the probe directly rather than by idling for a while and hoping nothing is
    /// reported. Idling asserts something about the machine as much as about the monitor: a build
    /// agent under load can leave even a do-nothing dispatcher thread unscheduled past the
    /// threshold, at which point the interface really was unresponsive and reporting it is correct.
    /// The claim worth pinning is that a probe answered promptly is not counted.
    /// </para>
    /// </summary>
    [Fact]
    public void AnIdleInterfaceReportsNothing()
    {
        var error = RunWithDispatcher(dispatcher =>
        {
            using var monitor = new UiResponsivenessMonitor(
                dispatcher, NullLogger<UiResponsivenessMonitor>.Instance);

            for (int round = 0; round < 3; round++)
            {
                Probe(monitor);

                // Nothing is holding the thread, so the probe comes back on the next idle turn.
                dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            }

            Assert.Equal(0, monitor.StallCount);
            Assert.Equal(TimeSpan.Zero, monitor.WorstStall);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A probe posted after shutdown is aborted and never reports, which is the right outcome for a
    /// measurement nobody will read. What must not happen is a stall being invented out of it.
    /// </summary>
    [Fact]
    public void ATickDuringShutdownIsHarmless()
    {
        var error = RunWithDispatcher(dispatcher =>
        {
            using var monitor = new UiResponsivenessMonitor(
                dispatcher, NullLogger<UiResponsivenessMonitor>.Instance);

            ShutDownAndWait(dispatcher);

            Probe(monitor);
            Probe(monitor);

            Assert.Equal(0, monitor.StallCount);
        });

        Assert.Null(error);
    }

    /// <summary>Disposing twice is what a shutdown path does when it is being careful.</summary>
    [Fact]
    public void DisposingIsSafeAndRepeatable()
    {
        var error = RunWithDispatcher(dispatcher =>
        {
            var monitor = new UiResponsivenessMonitor(
                dispatcher, NullLogger<UiResponsivenessMonitor>.Instance);

            monitor.Dispose();
            monitor.Dispose();
        });

        Assert.Null(error);
    }

    /// <summary>
    /// Shuts the dispatcher down and waits for it to finish.
    /// <para>
    /// Called from another thread, <c>InvokeShutdown</c> only queues the request — the dispatcher
    /// still has to process it. Probing straight afterwards tests a dispatcher that is still
    /// perfectly alive, which is how the first version of this test proved nothing at all.
    /// </para>
    /// </summary>
    private static void ShutDownAndWait(Dispatcher dispatcher)
    {
        dispatcher.InvokeShutdown();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!dispatcher.HasShutdownFinished && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.True(dispatcher.HasShutdownFinished, "the dispatcher never finished shutting down");
    }

    /// <summary>
    /// Waits until the monitor has a probe in flight, which only its own timer can arrange.
    /// <para>
    /// Reads the field the probe sets rather than sleeping for however long the timer's interval
    /// happens to be: the interval is an implementation detail, and a test that encodes it starts
    /// failing when somebody tunes it for good reasons.
    /// </para>
    /// </summary>
    private static bool WaitForOutstandingProbe(UiResponsivenessMonitor monitor)
    {
        var field = typeof(UiResponsivenessMonitor)
            .GetField("_outstanding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (field.GetValue(monitor) is not null)
                return true;

            Thread.Sleep(10);
        }

        return false;
    }

    /// <summary>
    /// Reaches the private tick directly. Waiting for the real timer would make the test a race
    /// about scheduling rather than about what the tick does when the window is closing.
    /// </summary>
    private static void Probe(UiResponsivenessMonitor monitor) =>
        typeof(UiResponsivenessMonitor)
            .GetMethod("Probe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(monitor, null);

    /// <summary>A real dispatcher on its own thread, since that is what the monitor is built around.</summary>
    private static Exception? RunWithDispatcher(Action<Dispatcher> action)
    {
        Exception? captured = null;
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();

        try
        {
            action(dispatcher!);
        }
        catch (Exception ex)
        {
            captured = ex is System.Reflection.TargetInvocationException { InnerException: { } inner }
                ? inner
                : ex;
        }
        finally
        {
            dispatcher!.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5));
        }

        return captured;
    }
}
