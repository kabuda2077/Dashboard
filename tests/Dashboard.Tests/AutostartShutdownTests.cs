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
