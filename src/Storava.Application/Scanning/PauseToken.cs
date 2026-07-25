namespace Storava.Application.Scanning;

/// <summary>
/// A cooperative pause primitive. The scanner awaits <see cref="PauseToken.WaitWhilePausedAsync"/>
/// at safe checkpoints; pausing blocks there until resumed, without spinning.
/// </summary>
public sealed class PauseTokenSource
{
    private readonly object _gate = new();
    private TaskCompletionSource<bool>? _paused;

    public PauseToken Token => new(this);

    public bool IsPaused
    {
        get
        {
            lock (_gate)
                return _paused is not null;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _paused ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource<bool>? toRelease;
        lock (_gate)
        {
            toRelease = _paused;
            _paused = null;
        }

        toRelease?.TrySetResult(true);
    }

    internal Task WaitWhilePausedAsync()
    {
        lock (_gate)
            return _paused?.Task ?? Task.CompletedTask;
    }
}

public readonly struct PauseToken
{
    private readonly PauseTokenSource? _source;

    internal PauseToken(PauseTokenSource source) => _source = source;

    public bool IsPaused => _source?.IsPaused ?? false;

    public Task WaitWhilePausedAsync() => _source?.WaitWhilePausedAsync() ?? Task.CompletedTask;
}
