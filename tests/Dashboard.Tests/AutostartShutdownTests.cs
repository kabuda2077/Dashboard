namespace Dashboard.Tests;

public sealed class AutostartShutdownTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ExitWaitsForAcceptedSystemChangeAndDiskCommit(bool success, bool reentrantShutdown)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), ephemeralPort: true,
            setAutostart: async enabled =>
            {
                Assert.True(enabled);
                Interlocked.Increment(ref calls);
                entered.SetResult();
                await release.Task;
                return success ? AutostartOperationResult.Ok("installed") : AutostartOperationResult.Failure("cancelled");
            });
        Task? shutdown = null;
        if (reentrantShutdown)
            host.StateChanged += (_, _) =>
            {
                if (host.IsAutostartUpdating)
                    shutdown ??= host.ShutdownAsync(TimeSpan.FromMilliseconds(50));
            };
        var request = new HostRequest { Type = "setDesktopOption", Option = "autostart", Value = true };
        var command = host.ExecuteAsync(request);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            shutdown ??= host.ShutdownAsync(TimeSpan.FromMilliseconds(50));
            await Task.WhenAny(shutdown, Task.Delay(150));
            Assert.False(shutdown.IsCompleted); // well beyond the ordinary background-work budget
            Assert.False(host.Settings.DesktopOptions.Autostart);
            Assert.Equal("closing", (await host.ExecuteAsync(request)).Code);
            Assert.Equal(1, calls);
        }
        finally
        {
            release.TrySetResult();
            await command.WaitAsync(TimeSpan.FromSeconds(5));
            await (shutdown ?? host.ShutdownAsync()).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(success ? "completed" : "failed", (await command).Status);
        Assert.Equal(success, (await command).Saved);
        Assert.Equal(success, new SettingsStore(TestRoot).Current.DesktopOptions.Autostart);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    public async Task FailedSettingsCommitRestoresSystemStateOrReportsRollbackFailure(bool enabled, bool rollbackSucceeds, bool rollbackThrows)
    {
        var original = !enabled;
        await new SettingsStore(TestRoot).SetOptionAsync("autostart", original);
        var calls = new List<bool>();
        var systemEnabled = original;
        using (var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), ephemeralPort: true,
            setAutostart: value =>
            {
                calls.Add(value);
                if (calls.Count == 2 && !rollbackSucceeds)
                {
                    if (rollbackThrows) throw new IOException("rollback failed");
                    return Task.FromResult(AutostartOperationResult.Failure("rollback refused"));
                }
                systemEnabled = value;
                return Task.FromResult(AutostartOperationResult.Ok("changed"));
            }))
        {
            using (var held = new FileStream(Path.Combine(TestRoot, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = await host.ExecuteAsync(new() { Type = "setDesktopOption", Option = "autostart", Value = enabled });
                Assert.Equal("failed", result.Status);
                Assert.Equal(rollbackSucceeds ? "autostartNotSaved" : "autostartRollbackFailed", result.Code);
                Assert.Equal(new[] { enabled, original }, calls);
                Assert.Equal(original, host.Settings.DesktopOptions.Autostart);
                Assert.Equal(rollbackSucceeds ? original : enabled, systemEnabled);
            }
        }
        // The next launch reads the same state as the restored system task.
        Assert.Equal(original, new SettingsStore(TestRoot).Current.DesktopOptions.Autostart);
    }

    [Fact]
    public async Task ClosingBeforeCommandDispatchNeverChangesSystemState()
    {
        var calls = 0;
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), ephemeralPort: true,
            setAutostart: _ => { calls++; return Task.FromResult(AutostartOperationResult.Ok("unexpected")); });
        await host.ShutdownAsync();
        var result = await host.ExecuteAsync(new() { Type = "setDesktopOption", Option = "autostart", Value = true });
        Assert.Equal("closing", result.Code);
        Assert.Equal(0, calls);
        Assert.False(new SettingsStore(TestRoot).Current.DesktopOptions.Autostart);
    }
}
