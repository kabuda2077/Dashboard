namespace Dashboard.Tests;

public sealed class ShutdownTaskTrackerTests
{
    [Fact]
    public async Task ShutdownCancelsOwnedWorkAndWaitsForCleanup()
    {
        using var tracker = new ShutdownTaskTracker();
        var cleanupFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owned = tracker.Run(async token =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { cleanupFinished.SetResult(); }
        });

        Assert.True(await tracker.StopAndWaitAsync(TimeSpan.FromSeconds(2)));
        await owned;
        Assert.True(cleanupFinished.Task.IsCompletedSuccessfully);
        Assert.True(tracker.IsClosing);
    }

    [Fact]
    public async Task ShutdownUsesOneBoundedBudgetForUncooperativeWork()
    {
        using var tracker = new ShutdownTaskTracker();
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tracker.Run(_ => never.Task);
        var started = DateTime.UtcNow;

        Assert.False(await tracker.StopAndWaitAsync(TimeSpan.FromMilliseconds(100)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
        never.SetResult();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaultedWorkDoesNotEndShutdownBeforeOtherWorkFinishes(bool taskTimesOut)
    {
        using var tracker = new ShutdownTaskTracker();
        var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = tracker.Run(_ => failure.Task);
        var owned = tracker.Run(_ => finish.Task);
        var shutdown = tracker.StopAndWaitAsync(TimeSpan.FromSeconds(2));

        Exception error = taskTimesOut ? new TimeoutException("Work timed out.") : new InvalidOperationException("Work failed.");
        failure.SetException(error);
        Assert.Same(error, await Record.ExceptionAsync(() => failed));
        Assert.False(shutdown.IsCompleted);

        finish.SetResult();
        Assert.True(await shutdown);
        await owned;
    }

    [Fact]
    public async Task UnlimitedCleanupWaitStillWaitsForOwnedWork()
    {
        using var tracker = new ShutdownTaskTracker();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owned = tracker.Run(_ => finish.Task);
        var drain = tracker.StopAndWaitAsync(Timeout.InfiniteTimeSpan);
        Assert.False(drain.IsCompleted);
        finish.SetResult();
        Assert.True(await drain.WaitAsync(TimeSpan.FromSeconds(2)));
        await owned;
    }

    [Fact]
    public async Task ClosingTrackerRejectsOperationBeforeDelegateStarts()
    {
        using var tracker = new ShutdownTaskTracker();
        Assert.True(await tracker.StopAndWaitAsync(TimeSpan.FromSeconds(1)));
        var invoked = false;

        await tracker.Run(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        Assert.False(invoked);
    }

    [Fact]
    public async Task DelegateStartsOnCallingSynchronizationContext()
    {
        using var tracker = new ShutdownTaskTracker();
        var previous = SynchronizationContext.Current;
        var context = new SynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            SynchronizationContext? observed = null;
            await tracker.Run(_ =>
            {
                observed = SynchronizationContext.Current;
                return Task.CompletedTask;
            });
            Assert.Same(context, observed);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task DelegateIsRegisteredBeforeItCanRaceShutdown()
    {
        using var tracker = new ShutdownTaskTracker();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owned = tracker.Run(async _ =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task;

        var shutdown = tracker.StopAndWaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(shutdown.IsCompleted);
        release.SetResult();

        Assert.True(await shutdown);
        await owned;
    }
}
