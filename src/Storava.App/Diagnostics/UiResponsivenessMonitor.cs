using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace Storava.App.Diagnostics;

/// <summary>
/// Reports when the interface stops responding, and for how long.
/// <para>
/// It exists because "the app feels slow" cannot be fixed by reading code. The first cause found
/// this way — SQLite's async methods running synchronously on the UI thread — was not the one that
/// looked most likely, and a rewrite done on intuition instead of measurement turned out to be nine
/// times slower than what it replaced. So the stall gets timed, and whatever the timings point at
/// is what gets fixed.
/// </para>
/// <para>
/// The cost is a timer tick on a background thread. It is left on in normal builds on purpose: a
/// stall the user can feel is worth a line in the log, and the log is the only account of it that
/// survives the moment.
/// </para>
/// </summary>
public sealed class UiResponsivenessMonitor : IDisposable
{
    /// <summary>Below roughly this, a delay is not perceived as the interface having stopped.</summary>
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly Dispatcher _dispatcher;
    private readonly ILogger<UiResponsivenessMonitor> _logger;
    private readonly System.Threading.Timer _timer;

    private readonly object _gate = new();
    private Stopwatch? _outstanding;
    private bool _disposed;

    public UiResponsivenessMonitor(Dispatcher dispatcher, ILogger<UiResponsivenessMonitor> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        _timer = new System.Threading.Timer(_ => Probe(), null, Interval, Interval);
    }

    /// <summary>The longest single stall seen so far, for a test or a report to assert against.</summary>
    public TimeSpan WorstStall { get; private set; }

    /// <summary>How many stalls crossed the threshold.</summary>
    public int StallCount { get; private set; }

    private void Probe()
    {
        lock (_gate)
        {
            // One in flight at a time. While the UI thread is stuck the probe cannot come back, and
            // queueing more would only measure the queue rather than the stall.
            if (_disposed || _outstanding is not null)
                return;

            _outstanding = Stopwatch.StartNew();
        }

        // Background priority, so this is served only once the thread has nothing real left to do.
        // That is the point: the round trip measures how long real work kept the interface busy.
        _dispatcher.BeginInvoke(DispatcherPriority.Background, Completed);
    }

    private void Completed()
    {
        Stopwatch? elapsed;

        lock (_gate)
        {
            elapsed = _outstanding;
            _outstanding = null;
        }

        if (elapsed is null)
            return;

        elapsed.Stop();
        if (elapsed.Elapsed < Threshold)
            return;

        StallCount++;
        if (elapsed.Elapsed > WorstStall)
            WorstStall = elapsed.Elapsed;

        _logger.LogWarning(
            "The interface was busy for {Milliseconds} ms and could not respond.",
            (long)elapsed.Elapsed.TotalMilliseconds);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _timer.Dispose();
    }
}
