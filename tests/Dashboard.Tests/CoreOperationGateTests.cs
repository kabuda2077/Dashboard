namespace Dashboard.Tests;

public sealed class CoreOperationGateTests
{
    [Fact]
    public async Task ConcurrentOperationsAreRejectedRatherThanQueued()
    {
        var gate = new CoreOperationGate();
        using (var active = gate.TryEnter())
        {
            Assert.NotNull(active);
            var attempts = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => gate.TryEnter())));
            Assert.All(attempts, Assert.Null);
        }
        using var retry = gate.TryEnter();
        Assert.NotNull(retry);
    }

    [Fact]
    public void ShutdownRejectsNewWorkEvenAfterCurrentLeaseIsReleased()
    {
        var gate = new CoreOperationGate();
        var active = gate.TryEnter();
        gate.Close();
        active!.Dispose();
        active.Dispose();
        Assert.True(gate.IsClosing);
        Assert.Null(gate.TryEnter());
    }

    [Fact]
    public async Task ShutdownWaitsForActiveConfigurationLeaseWithinBudget()
    {
        var gate = new CoreOperationGate();
        var active = gate.TryEnter();
        gate.Close();

        var wait = gate.WaitForIdleAsync(TimeSpan.FromSeconds(2));
        Assert.False(wait.IsCompleted);
        active!.Dispose();
        Assert.True(await wait);
    }

    [Fact]
    public async Task ShutdownTimesOutWithoutRevokingActiveLease()
    {
        var gate = new CoreOperationGate();
        using var active = gate.TryEnter();
        gate.Close();

        Assert.False(await gate.WaitForIdleAsync(TimeSpan.FromMilliseconds(50)));
        Assert.Null(gate.TryEnter());
    }

    [Fact]
    public void DisposedProcessManagerCannotStartAgain()
    {
        var manager = new CoreProcessManager();
        manager.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.Start(new AppSettings()));
    }
}
