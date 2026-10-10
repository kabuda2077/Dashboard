using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Dashboard.Tests;

public sealed class HostLifecycleRecoveryTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData("executable")]
    [InlineData("config")]
    [InlineData("credential")]
    public async Task PredictedElevationRecoversStartupWhenValidationFailsBeforeRelaunch(string missing)
    {
        var settings = new SettingsStore(TestRoot).Current;
        var profile = settings.ActiveProfile;
        Directory.CreateDirectory(Path.GetDirectoryName(profile.ExePath)!);
        if (missing != "executable") File.WriteAllText(profile.ExePath, "fixture, never executed");
        if (missing != "config") File.WriteAllText(profile.ConfigPath, "fixture");
        if (missing == "credential")
            SettingsStore.WriteAtomically(Path.Combine(TestRoot, "settings.json"), JsonSerializer.Serialize(
                settings.WithProfile(CoreKind.Mihomo, profile with { ProtectedSecret = "unreadable-fixture" }), AppSettings.JsonOptions));
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true, () => false,
            readCoreVersion: (_, _, _) => Task.FromResult(""));
        var calls = new List<string>();
        var relaunches = 0;
        host.RelaunchRequested += (_, _) => relaunches++;
        try
        {
            Assert.True(DashboardApplicationContext.WillRelaunchElevated(true, false));
            await DashboardApplicationContext.RunStartupOperationsAsync(true, () => host.StartCoreAsync(),
                () => { calls.Add("reconcile"); return Task.CompletedTask; },
                result => { Assert.Equal("failed", result.Status); Assert.NotEmpty(result.Message!); calls.Add("recover"); });
            Assert.Equal(["recover", "reconcile"], calls);
            Assert.Equal(0, relaunches);
            Assert.False(host.IsRunning);
        }
        finally { await host.ShutdownAsync(); }
    }

    [Fact]
    public async Task ActualElevationDefersRecoveryAndAutostartUntilReplacement()
    {
        var profile = new SettingsStore(TestRoot).Current.ActiveProfile;
        Directory.CreateDirectory(Path.GetDirectoryName(profile.ExePath)!);
        File.WriteAllText(profile.ExePath, "fixture, never executed");
        File.WriteAllText(profile.ConfigPath, "fixture");
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true, () => false,
            readCoreVersion: (_, _, _) => Task.FromResult(""));
        var relaunches = 0;
        host.RelaunchRequested += (_, _) => relaunches++;
        try
        {
            await DashboardApplicationContext.RunStartupOperationsAsync(true, () => host.StartCoreAsync(),
                () => throw new InvalidOperationException("Autostart must wait for elevation"),
                _ => throw new InvalidOperationException("Elevation is not a startup failure"));
            Assert.Equal(1, relaunches);
            Assert.False(host.IsRunning);
        }
        finally { await host.ShutdownAsync(); }
    }

    [Fact]
    public async Task RelaunchDrainsAcceptedAutostartAndBlocksNewChangesThroughReplacementAndExit()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true,
            setAutostart: async _ => { calls++; entered.SetResult(); await release.Task; return AutostartOperationResult.Ok("installed"); });
        var request = new HostRequest { Type = "setDesktopOption", Option = "autostart", Value = true };
        var accepted = host.ExecuteAsync(request);
        Task<bool>? relaunch = null;
        Task<CommandResult>? duringSpawn = null;
        var replacements = 0;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            relaunch = DashboardApplicationContext.RelaunchAfterConfirmationAsync(() => Task.FromResult(true),
                () => host.RelaunchAfterAutostartAsync(() =>
                {
                    replacements++;
                    Assert.True(new SettingsStore(TestRoot).Current.DesktopOptions.Autostart);
                    duringSpawn = host.ExecuteAsync(request with { Value = false });
                }, () => host.ShutdownAsync()));
            Assert.Equal("busy", (await host.ExecuteAsync(request with { Value = false })).Code);
            Assert.False(relaunch.IsCompleted);
            Assert.Equal(0, replacements);
            release.SetResult();
            Assert.True(await relaunch.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True((await accepted).Saved);
            Assert.Equal("busy", (await duringSpawn!).Code);
            Assert.Equal("closing", (await host.ExecuteAsync(request)).Code);
            Assert.Equal(1, replacements);
            Assert.Equal(1, calls);
        }
        finally
        {
            release.TrySetResult();
            await accepted.WaitAsync(TimeSpan.FromSeconds(5));
            if (relaunch is not null) await relaunch.WaitAsync(TimeSpan.FromSeconds(5));
            await host.ShutdownAsync();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedOrCancelledReplacementReleasesAutostartGateAndLeavesOldHostUsable(bool uacCancelled)
    {
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true,
            setAutostart: _ => Task.FromResult(AutostartOperationResult.Ok("installed")));
        var exits = 0;
        try
        {
            var error = await Record.ExceptionAsync(() => DashboardApplicationContext.RelaunchAfterConfirmationAsync(
                () => Task.FromResult(true), () => host.RelaunchAfterAutostartAsync(
                    () => throw (uacCancelled ? new Win32Exception(1223) : new InvalidOperationException("replacement could not start")),
                    () => { exits++; return host.ShutdownAsync(); })));
            Assert.Equal(uacCancelled ? typeof(Win32Exception) : typeof(InvalidOperationException), error?.GetType());
            Assert.Equal(0, exits);
            Assert.False(host.IsClosing);
            Assert.True((await host.ExecuteAsync(new() { Type = "setDesktopOption", Option = "lightweightMode", Value = false })).Saved);
            Assert.True((await host.ExecuteAsync(new() { Type = "setDesktopOption", Option = "autostart", Value = true })).Saved);
        }
        finally { await host.ShutdownAsync(); }
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task LiveCoreStopFailureRequiresRetryOrExplicitForceExitAfterHostIsSealed(bool retry, int failures)
    {
        using var observed = Process.GetCurrentProcess();
        var processField = typeof(CoreProcessManager).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var stops = 0;
        CoreProcessManager? core = null;
        // Observe this test process without ever terminating it; successful stop removes only the injected reference.
        core = new CoreProcessManager(_ =>
        {
            stops++;
            if (stops <= failures) throw new IOException("injected termination failure");
            processField.SetValue(core, null);
        }, (_, _) => true);
        processField.SetValue(core, observed);
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true, core: core);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var calls = new List<string>();
        Task<CommandResult>? rejected = null;
        try
        {
            await DashboardApplicationContext.CompleteExitAsync(async () =>
            {
                calls.Add("shutdown");
                Assert.False(await host.ShutdownAsync(TimeSpan.FromMilliseconds(200)));
                Assert.True(host.IsRunning);
                await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(host.DashboardUri));
                return false;
            }, host.RetryCoreShutdownAsync, () =>
            {
                Assert.DoesNotContain("close", calls);
                Assert.True(host.IsRunning);
                Assert.True(host.IsClosing);
                rejected = host.ExecuteAsync(new() { Type = "stop", CoreType = CoreKind.Mihomo });
                calls.Add(retry ? "retry" : "force");
                return retry;
            }, () => calls.Add("close"), () => calls.Add("exit"));
            var expected = new List<string> { "shutdown" };
            expected.AddRange(Enumerable.Repeat(retry ? "retry" : "force", retry ? failures : 1));
            expected.AddRange(["close", "exit"]);
            Assert.Equal(expected, calls);
            Assert.Equal("closing", (await rejected!).Code);
            Assert.Equal(!retry, host.IsRunning);
            Assert.Equal(retry ? failures + 1 : 1, stops);
            if (retry) Assert.True(await host.ShutdownAsync());
            Assert.False(observed.HasExited);
        }
        finally
        {
            processField.SetValue(core, null);
            Assert.True(await host.RetryCoreShutdownAsync());
        }
    }

    [Fact]
    public async Task RetryAfterTimeoutWaitsForTheExistingStopBeforeDisposingCore()
    {
        using var observed = Process.GetCurrentProcess();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processField = typeof(CoreProcessManager).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var disposedField = typeof(CoreProcessManager).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        CoreProcessManager? core = null;
        core = new CoreProcessManager(_ =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Stop watchdog");
            processField.SetValue(core, null); // never terminate the observed test process
        }, (_, _) => true);
        processField.SetValue(core, observed);
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true, core: core);
        Task<bool>? retry = null;
        try
        {
            var shutdown = host.ShutdownAsync(TimeSpan.FromMilliseconds(50));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await shutdown);
            retry = host.RetryCoreShutdownAsync();
            Assert.False(retry.IsCompleted);
            Assert.False((bool)disposedField.GetValue(core)!);
            release.Set();
            Assert.True(await retry.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True((bool)disposedField.GetValue(core)!);
            Assert.Contains("core stopped.", core.GetLogTail(8000));
            Assert.DoesNotContain("core is not running.", core.GetLogTail(8000));
            Assert.False(observed.HasExited);
        }
        finally
        {
            release.Set();
            if (retry is not null) await retry.WaitAsync(TimeSpan.FromSeconds(5));
            var stopped = await host.ShutdownAsync();
            processField.SetValue(core, null);
            if (!stopped) Assert.True(await host.RetryCoreShutdownAsync());
        }
    }

    [Fact]
    public async Task FailureAfterReplacementStartsDoesNotReopenTheSealedHost()
    {
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true);
        var replacementStarted = false;
        await Assert.ThrowsAsync<IOException>(() => host.RelaunchAfterAutostartAsync(
            () => replacementStarted = true, async () =>
            {
                Assert.True(await host.ShutdownAsync());
                throw new IOException("injected failure after shutdown");
            }));
        Assert.True(replacementStarted);
        Assert.True(host.IsClosing);
        Assert.Equal("closing", (await host.ExecuteAsync(new() { Type = "stop", CoreType = CoreKind.Mihomo })).Code);
    }

    [Fact]
    public async Task ShutdownExceptionStillRequiresAnExplicitDecisionAndAllowsRetry()
    {
        var calls = new List<string>();
        await DashboardApplicationContext.CompleteExitAsync(
            () => { calls.Add("shutdown"); throw new IOException("injected shutdown failure"); },
            () => { calls.Add("retry"); return Task.FromResult(true); },
            () => { calls.Add("ask"); return true; },
            () => calls.Add("close"), () => calls.Add("exit"));
        Assert.Equal(["shutdown", "ask", "retry", "close", "exit"], calls);
    }
}
