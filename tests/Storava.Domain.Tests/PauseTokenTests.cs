using Storava.Application.Scanning;

namespace Storava.Domain.Tests;

public class PauseTokenTests
{
    [Fact]
    public void NotPaused_WaitCompletesImmediately()
    {
        var source = new PauseTokenSource();
        Assert.False(source.Token.IsPaused);
        Assert.True(source.Token.WaitWhilePausedAsync().IsCompleted);
    }

    [Fact]
    public async Task Pause_BlocksUntilResume()
    {
        var source = new PauseTokenSource();
        source.Pause();

        Assert.True(source.Token.IsPaused);
        var waiting = source.Token.WaitWhilePausedAsync();
        Assert.False(waiting.IsCompleted);

        source.Resume();

        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(source.Token.IsPaused);
    }

    [Fact]
    public async Task Resume_ReleasesAllWaiters()
    {
        var source = new PauseTokenSource();
        source.Pause();

        var waiters = Enumerable.Range(0, 5).Select(_ => source.Token.WaitWhilePausedAsync()).ToArray();
        source.Resume();

        await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.All(waiters, w => Assert.True(w.IsCompletedSuccessfully));
    }

    [Fact]
    public void RepeatedPause_IsIdempotent()
    {
        var source = new PauseTokenSource();
        source.Pause();
        var first = source.Token.WaitWhilePausedAsync();
        source.Pause();
        var second = source.Token.WaitWhilePausedAsync();

        Assert.Same(first, second);
    }

    [Fact]
    public void ResumeWithoutPause_DoesNotThrow()
    {
        var source = new PauseTokenSource();
        source.Resume();
        Assert.False(source.Token.IsPaused);
    }
}
