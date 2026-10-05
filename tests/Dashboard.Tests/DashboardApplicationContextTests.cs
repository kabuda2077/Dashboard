namespace Dashboard.Tests;

public sealed class DashboardApplicationContextTests
{
    [Fact]
    public async Task ExitKeepsCleanupPendingUntilAsynchronousShutdownCompletes()
    {
        var calls = new List<string>();
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var exit = DashboardApplicationContext.CompleteExitAsync(
            () => { calls.Add("shutdown"); return shutdown.Task; },
            () => calls.Add("close"),
            () => calls.Add("exit"));
        Assert.Equal(["shutdown"], calls);
        Assert.False(exit.IsCompleted);

        shutdown.SetResult();
        await exit;
        Assert.Equal(["shutdown", "close", "exit"], calls);
    }

    [Theory]
    [InlineData(true, false, true, 0)]
    [InlineData(false, false, false, 1)]
    [InlineData(false, true, true, 1)]
    public async Task ExitWaitsForFlushAndRespectsTheDiscardDecision(bool saved, bool discard, bool expected, int confirmations)
    {
        var flush = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = 0;
        var exit = DashboardApplicationContext.ConfirmExitAsync(() => flush.Task, () => { asked++; return discard; });
        Assert.False(exit.IsCompleted);
        Assert.Equal(0, asked);
        flush.SetResult(saved);
        Assert.Equal(expected, await exit);
        Assert.Equal(confirmations, asked);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReplacementIsNotCreatedWhileExitConfirmationIsPending(bool confirmed)
    {
        var calls = new List<string>();
        var consent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = DashboardApplicationContext.RelaunchAfterConfirmationAsync(() => consent.Task,
            () => calls.Add("spawn"), () => { calls.Add("finish"); return Task.CompletedTask; });
        Assert.Empty(calls);
        consent.SetResult(confirmed);
        Assert.Equal(confirmed, await result);
        Assert.Equal(confirmed ? new[] { "spawn", "finish" } : Array.Empty<string>(), calls);
    }

    [Fact]
    public async Task FailedOrCancelledReplacementKeepsTheOldHostAlive()
    {
        var exited = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => DashboardApplicationContext.RelaunchAfterConfirmationAsync(
            () => Task.FromResult(true), () => throw new InvalidOperationException("UAC cancelled"),
            () => { exited = true; return Task.CompletedTask; }));
        Assert.False(exited);
    }

    [Fact]
    public async Task StartupStartsCoreBeforeAutostartReconciliation()
    {
        var calls = new List<string>();

        await DashboardApplicationContext.RunStartupOperationsAsync(
            shouldStartCore: true,
            () => { calls.Add("start"); return Task.CompletedTask; },
            () => { calls.Add("reconcile"); return Task.CompletedTask; });

        Assert.Equal(["start", "reconcile"], calls);
    }

    [Fact]
    public async Task StartupWithoutCoreStillReconcilesAutostart()
    {
        var calls = new List<string>();

        await DashboardApplicationContext.RunStartupOperationsAsync(
            shouldStartCore: false,
            () => { calls.Add("start"); return Task.CompletedTask; },
            () => { calls.Add("reconcile"); return Task.CompletedTask; });

        Assert.Equal(["reconcile"], calls);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void StartingCoreWithoutElevationDefersWindowAndAutostart(
        bool shouldStartCore,
        bool isAdministrator,
        bool expected)
    {
        Assert.Equal(
            expected,
            DashboardApplicationContext.WillRelaunchElevated(shouldStartCore, isAdministrator));
    }

    [Theory]
    [InlineData(true, false, false, true, true, false, true)]
    [InlineData(true, false, false, true, true, true, false)]
    [InlineData(true, false, false, true, false, false, false)]
    [InlineData(true, false, false, false, true, false, false)]
    [InlineData(true, true, false, true, true, false, false)]
    [InlineData(true, false, true, true, true, false, false)]
    [InlineData(false, false, false, true, true, false, false)]
    public void ResumeRecoveryOnlyRestartsAStaleMihomoTun(
        bool coreRunning,
        bool isSingBox,
        bool coreOperationInProgress,
        bool tunWasUpBeforeSuspend,
        bool physicalNetworkUp,
        bool tunUp,
        bool expected)
    {
        Assert.Equal(
            expected,
            DashboardApplicationContext.ShouldRestartCoreAfterResume(
                coreRunning,
                isSingBox,
                coreOperationInProgress,
                tunWasUpBeforeSuspend,
                physicalNetworkUp,
                tunUp));
    }
}
