namespace Dashboard.Tests;

public sealed class CoreLifecycleBoundaryTests
{
    private static CoreLifecycleController Create(
        CoreProcessManager process,
        List<string> calls,
        AppSettings? settings = null,
        Func<string, Action?, CancellationToken, Task<CoreUpgradeResult>>? upgradeSingBox = null) => new(
        settings ?? new AppSettings(), process, new CoreLifecycleServices
        {
            IsRunningAsAdministrator = () => { calls.Add("elevationCheck"); return true; },
            ShouldKeepMinimizedForRelaunch = () => false,
            RelaunchAsAdministrator = (_, _, _) => calls.Add("relaunch"),
            ShowNotice = message => calls.Add("notice:" + message), PublishState = () => calls.Add("state"),
            RefreshIconCache = () => calls.Add("icons"), ShowTrayNotification = _ => calls.Add("tray"),
            ShowMessage = (_, _, _) => calls.Add("dialog"), RunOnUiThread = action => action(),
            UpgradeSingBoxAsync = upgradeSingBox ?? DefaultUpgradeSingBoxAsync
        });

    private static Task<CoreUpgradeResult> DefaultUpgradeSingBoxAsync(string _, Action? __, CancellationToken ___) =>
        Task.FromResult(new CoreUpgradeResult("1.0.0", "test.zip", "backup", IsAlreadyLatest: false));

    [Fact]
    public async Task UnreadableCredentialBlocksUpgradeBeforeNetworkOrProcessOperations()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        var settings = new AppSettings { CoreType = AppSettings.CoreTypeSingBox };
        settings.RestoreSecretPersistenceState(true, "", "dpapi:original", true);
        var upgraded = false;
        using var lifecycle = Create(process, calls, settings, (_, _, _) =>
        {
            upgraded = true;
            return DefaultUpgradeSingBoxAsync("", null, CancellationToken.None);
        });
        await lifecycle.UpgradeAsync();
        Assert.False(upgraded);
        Assert.Contains(calls, call => call.Contains("Secret 无法解密"));
        Assert.True(await lifecycle.WaitForShutdownAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ConfigurationEditAndEveryCoreOperationShareTheSameGate()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        var lifecycle = Create(process, calls);
        using (var configuration = lifecycle.TryEnterConfigurationChange())
        {
            Assert.NotNull(configuration);
            lifecycle.Start();
            lifecycle.Stop();
            Assert.False(lifecycle.Restart());
            await lifecycle.SwitchAsync("sing-box");
            await lifecycle.UpgradeAsync();
            Assert.Equal(5, calls.Count(call => call.StartsWith("notice:", StringComparison.Ordinal)));
            Assert.Null(lifecycle.TryEnterConfigurationChange());
        }
        using var retry = lifecycle.TryEnterConfigurationChange();
        Assert.NotNull(retry);
    }

    [Fact]
    public async Task CombinedSaveAndStartHoldOneLeaseWithoutReentry()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        var saveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = Create(process, calls);

        var combined = lifecycle.ExecuteConfigurationCommandAsync(async () =>
        {
            calls.Add("save:begin");
            saveEntered.SetResult();
            await releaseSave.Task;
            calls.Add("save:end");
        }, CoreConfigurationCommand.Start);
        await saveEntered.Task;

        Assert.Null(lifecycle.TryEnterConfigurationChange());
        lifecycle.Stop();
        Assert.Contains(calls, call => call.Contains("正在进行", StringComparison.Ordinal));
        releaseSave.SetResult();
        Assert.Equal(ConfigurationCommandResult.Executed, await combined);

        Assert.True(calls.IndexOf("save:end") < calls.IndexOf("elevationCheck"));
    }

    [Fact]
    public async Task CombinedCommandReturnsRejectedWithoutSavingWhenGateIsBusy()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        using var lifecycle = Create(process, calls);
        using var lease = lifecycle.TryEnterConfigurationChange();
        var saved = false;

        var result = await lifecycle.ExecuteConfigurationCommandAsync(
            () => { saved = true; return Task.CompletedTask; },
            CoreConfigurationCommand.SaveOnly);

        Assert.Equal(ConfigurationCommandResult.Rejected, result);
        Assert.False(saved);
    }

    [Fact]
    public async Task CombinedCommandReturnsRejectedWithoutSavingAfterTrackerCloses()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        var lifecycle = Create(process, calls);
        Assert.True(await lifecycle.WaitForShutdownAsync(TimeSpan.FromSeconds(2)));
        var saved = false;

        var result = await lifecycle.ExecuteConfigurationCommandAsync(
            () => { saved = true; return Task.CompletedTask; },
            CoreConfigurationCommand.SaveOnly);

        Assert.Equal(ConfigurationCommandResult.Rejected, result);
        Assert.False(saved);
    }

    [Fact]
    public async Task ShutdownCancelsUpgradeAndWaitsForOwnedTask()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new AppSettings { CoreType = AppSettings.CoreTypeSingBox, SingBoxCorePath = "captured.exe" };
        var lifecycle = Create(process, calls, settings, async (path, _, token) =>
        {
            Assert.Equal("captured.exe", path);
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });

        var upgrade = lifecycle.UpgradeAsync();
        await entered.Task;
        lifecycle.BeginShutdown();

        Assert.True(await lifecycle.WaitForShutdownAsync(TimeSpan.FromSeconds(2)));
        await upgrade;
        Assert.False(lifecycle.IsUpgradeInProgress);
        Assert.Null(lifecycle.TryEnterConfigurationChange());
    }

    [Fact]
    public async Task LateUpgradeResultAfterShutdownCannotPublishOrRestart()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        var result = new TaskCompletionSource<CoreUpgradeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new AppSettings { CoreType = AppSettings.CoreTypeSingBox, SingBoxCorePath = "captured.exe" };
        var lifecycle = Create(process, calls, settings, (_, _, _) => result.Task);

        var upgrade = lifecycle.UpgradeAsync();
        while (!lifecycle.IsUpgradeInProgress) await Task.Yield();
        lifecycle.BeginShutdown();
        var countAtShutdown = calls.Count;
        Assert.False(await lifecycle.WaitForShutdownAsync(TimeSpan.FromMilliseconds(50)));

        result.SetResult(new CoreUpgradeResult("2.0.0", "test.zip", "backup", IsAlreadyLatest: false));
        await upgrade;
        Assert.Equal(countAtShutdown, calls.Count);
        Assert.False(process.IsRunning);
    }

    [Fact]
    public async Task SuccessfulDrainDisposesShutdownTokenBeforeReturning()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CoreUpgradeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new AppSettings { CoreType = AppSettings.CoreTypeSingBox };
        CancellationToken ownedToken = default;
        var lifecycle = Create(process, calls, settings, (_, _, token) =>
        {
            ownedToken = token;
            entered.SetResult();
            return release.Task;
        });

        var upgrade = lifecycle.UpgradeAsync();
        await entered.Task;
        lifecycle.BeginShutdown();
        var shutdown = lifecycle.WaitForShutdownAsync(TimeSpan.FromSeconds(2));
        release.SetResult(new CoreUpgradeResult("2.0.0", "test.zip", "backup", IsAlreadyLatest: false));

        Assert.True(await shutdown);
        await upgrade;
        Assert.Throws<ObjectDisposedException>(() => { _ = ownedToken.WaitHandle; });
        lifecycle.BeginShutdown();
        lifecycle.Dispose();
        lifecycle.Dispose();
    }

    [Fact]
    public async Task TimedOutDrainKeepsTokenAliveUntilOwnedTaskCompletes()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CoreUpgradeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new AppSettings { CoreType = AppSettings.CoreTypeSingBox };
        CancellationToken ownedToken = default;
        var lifecycle = Create(process, calls, settings, (_, _, token) =>
        {
            ownedToken = token;
            entered.SetResult();
            return release.Task;
        });

        var upgrade = lifecycle.UpgradeAsync();
        await entered.Task;
        lifecycle.BeginShutdown();

        Assert.False(await lifecycle.WaitForShutdownAsync(TimeSpan.FromMilliseconds(50)));
        lifecycle.Dispose();
        lifecycle.BeginShutdown();
        lifecycle.Dispose();
        _ = ownedToken.WaitHandle;

        release.SetResult(new CoreUpgradeResult("2.0.0", "test.zip", "backup", IsAlreadyLatest: false));
        await upgrade;
        Assert.True(await lifecycle.WaitForShutdownAsync(TimeSpan.FromSeconds(2)));
        Assert.Throws<ObjectDisposedException>(() => { _ = ownedToken.WaitHandle; });
    }

    [Fact]
    public async Task UpgradeAndSwitchCannotInterleave()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        var result = new TaskCompletionSource<CoreUpgradeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new AppSettings { CoreType = AppSettings.CoreTypeSingBox };
        var lifecycle = Create(process, calls, settings, (_, _, _) => result.Task);

        var upgrade = lifecycle.UpgradeAsync();
        while (!lifecycle.IsUpgradeInProgress) await Task.Yield();
        await lifecycle.SwitchAsync(AppSettings.CoreTypeMihomo);
        Assert.Equal(AppSettings.CoreTypeSingBox, settings.CoreType);
        Assert.Contains(calls, call => call.Contains("正在进行", StringComparison.Ordinal));
        result.SetResult(new CoreUpgradeResult("2.0.0", "test.zip", "backup", IsAlreadyLatest: false));
        await upgrade;
    }

    [Fact]
    public async Task ShutdownRejectsOperationsWithoutElevationOrStatePublication()
    {
        using var process = new CoreProcessManager();
        var calls = new List<string>();
        var lifecycle = Create(process, calls);
        lifecycle.BeginShutdown();
        lifecycle.Start();
        lifecycle.Stop();
        Assert.False(lifecycle.Restart());
        await lifecycle.SwitchAsync("sing-box");
        await lifecycle.UpgradeAsync();
        Assert.Null(lifecycle.TryEnterConfigurationChange());
        Assert.Empty(calls);
    }
}
