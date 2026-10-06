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

    [Fact]
    public void ReleaseHasNoPowerEventOrNetworkAdapterCallbacks()
    {
        // The unreliable power-driven restart workaround stays withdrawn until
        // a separately validated replacement exists; this is not a TUN recovery test.
        var methods = typeof(DashboardApplicationContext).GetMethods(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(methods, method => method.GetParameters().Any(parameter =>
            parameter.ParameterType.FullName is "Microsoft.Win32.PowerModeChangedEventArgs"
                or "System.Net.NetworkInformation.NetworkInterface"));
    }
}
