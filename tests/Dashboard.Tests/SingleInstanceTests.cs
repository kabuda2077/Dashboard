namespace Dashboard.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void NamesAreIsolatedByUserSid()
    {
        var first = SingleInstance.GetNames("S-1-5-21-1000");
        var second = SingleInstance.GetNames("S-1-5-21-2000");

        Assert.NotEqual(first.MutexName, second.MutexName);
        Assert.NotEqual(first.PipeName, second.PipeName);
        Assert.StartsWith("Local\\Dashboard.SingleInstance.", first.MutexName);
        Assert.StartsWith("Dashboard.Activate.", first.PipeName);
    }

    [Fact]
    public async Task OversizedActivationCommandIsBounded()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(new string('x', 4096)));
        using var reader = new StreamReader(stream);
        Assert.Equal("", await SingleInstance.ReadActivationCommandAsync(reader, CancellationToken.None));
    }

    [Fact]
    public void SecondInstanceReceivesActivationAcknowledgement()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var names = new SingleInstanceNames(
            $"Local\\Dashboard.Tests.{suffix}",
            $"Dashboard.Tests.{suffix}");
        using var activated = new ManualResetEventSlim();
        Assert.True(SingleInstance.TryCreate(
            activated.Set,
            waitForPreviousExit: false,
            names,
            out var primary));
        using (primary)
        {
            Assert.False(SingleInstance.TryCreate(
                () => { },
                waitForPreviousExit: false,
                names,
                out var secondary));
            Assert.Null(secondary);
            Assert.True(activated.Wait(TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public async Task ReplacementWaitsForDelayedExitDecisionWhileOrdinaryLaunchStillActivates()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var names = new SingleInstanceNames($"Local\\Dashboard.Tests.{suffix}", $"Dashboard.Tests.{suffix}");
        using var decision = new ManualResetEventSlim();
        var decisionPending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primaryDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitBound = 0;
        var activations = 0;
        var primaryThread = new Thread(() =>
        {
            try
            {
                Assert.True(SingleInstance.TryCreate(() => Interlocked.Increment(ref activations), false, names, out var primary));
                using (primary)
                {
                    DashboardApplicationContext.CompleteExitAsync(() => Task.FromResult(false),
                        () => throw new InvalidOperationException("No retry selected"), () =>
                        {
                            decisionPending.SetResult();
                            Assert.True(decision.Wait(TimeSpan.FromSeconds(10)), "Exit decision watchdog");
                            return false; // explicit force exit, after the simulated dialog closes
                        }, () => { }, () => { }).GetAwaiter().GetResult();
                }
                primaryDone.SetResult();
            }
            catch (Exception error) { primaryDone.TrySetException(error); }
        }) { IsBackground = true };
        var replacementThread = new Thread(() =>
        {
            try
            {
                Assert.True(SingleInstance.TryCreate(() => { }, true, names, out var replacement,
                    (handle, timeout) =>
                    {
                        waitBound = timeout;
                        waiting.SetResult();
                        return handle.WaitOne(timeout);
                    }));
                using (replacement) { }
                replacementDone.SetResult();
            }
            catch (Exception error) { replacementDone.TrySetException(error); }
        }) { IsBackground = true };
        primaryThread.Start();
        var replacementStarted = false;
        try
        {
            await decisionPending.Task.WaitAsync(TimeSpan.FromSeconds(5));
            replacementThread.Start();
            replacementStarted = true;
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Inspect the actual wait bound instead of sleeping past the former 20-second deadline.
            Assert.Equal(Timeout.Infinite, waitBound);
            Assert.False(primaryDone.Task.IsCompleted);
            Assert.False(replacementDone.Task.IsCompleted);
            Assert.False(SingleInstance.TryCreate(() => { }, false, names, out var ordinary));
            Assert.Null(ordinary);
            Assert.Equal(1, Volatile.Read(ref activations));
            Assert.False(replacementDone.Task.IsCompleted);
            decision.Set();
            await Task.WhenAll(primaryDone.Task, replacementDone.Task).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            decision.Set();
            Assert.True(primaryThread.Join(TimeSpan.FromSeconds(5)));
            if (replacementStarted) Assert.True(replacementThread.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void ReplacementAcquiresAbandonedPredecessorMutex()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var names = new SingleInstanceNames($"Local\\Dashboard.Tests.{suffix}", $"Dashboard.Tests.{suffix}");
        Mutex? predecessor = null;
        var owner = new Thread(() => predecessor = new Mutex(true, names.MutexName)) { IsBackground = true };
        owner.Start();
        Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
        try
        {
            Assert.NotNull(predecessor);
            Assert.True(SingleInstance.TryCreate(() => { }, true, names, out var replacement));
            using (replacement) { }
        }
        finally { predecessor?.Dispose(); }
    }
}
