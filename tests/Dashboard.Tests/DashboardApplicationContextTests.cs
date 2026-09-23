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

    [Fact]
    public async Task StartupStartsCoreBeforeAutostartReconciliation()
    {
        var calls = new List<string>();

        await DashboardApplicationContext.RunStartupOperationsAsync(
            shouldStartCore: true,
            () => calls.Add("start"),
            () => { calls.Add("reconcile"); return Task.CompletedTask; });

        Assert.Equal(["start", "reconcile"], calls);
    }

    [Fact]
    public async Task StartupWithoutCoreStillReconcilesAutostart()
    {
        var calls = new List<string>();

        await DashboardApplicationContext.RunStartupOperationsAsync(
            shouldStartCore: false,
            () => calls.Add("start"),
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
