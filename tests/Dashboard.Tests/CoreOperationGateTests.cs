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
        Assert.Null(gate.TryEnter());
    }

    [Fact]
    public async Task DisposedProcessManagerCannotStartAgain()
    {
        var manager = new CoreProcessManager();
        manager.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => manager.StartAsync(new(CoreKind.Mihomo, new CoreProfile())));
    }
}
