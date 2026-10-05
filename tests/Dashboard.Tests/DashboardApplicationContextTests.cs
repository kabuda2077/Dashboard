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

    [Fact]
    public void ReleaseHasNoPowerEventOrNetworkAdapterCallbacks()
    {
        // Power-driven core restarts are intentionally deferred to v2. This
        // guards the removed callbacks; it is not a TUN recovery test.
        var methods = typeof(DashboardApplicationContext).GetMethods(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(methods, method => method.GetParameters().Any(parameter =>
            parameter.ParameterType.FullName is "Microsoft.Win32.PowerModeChangedEventArgs"
                or "System.Net.NetworkInformation.NetworkInterface"));
    }
}
