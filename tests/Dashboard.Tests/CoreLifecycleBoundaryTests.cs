namespace Dashboard.Tests;

public sealed class CoreLifecycleBoundaryTests : TemporaryDirectoryTest
{
    private string _directory => TestRoot;
    private static HostRequest Save(CoreProfile profile, long revision = 0) => new()
    {
        Type = "saveProfile", CoreType = CoreKind.Mihomo, ExpectedRevision = revision,
        Draft = new() { ExePath = profile.ExePath, ConfigPath = profile.ConfigPath, ApiUrl = profile.ApiUrl }
    };

    [Fact]
    public async Task BusyGateRejectsInsteadOfQueueingAnotherCoreCommand()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var block = false;
        var store = new SettingsStore(_directory, persist: (path, json) =>
        {
            if (block) { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); }
            SettingsStore.WriteAtomically(path, json);
        });
        using var process = new CoreProcessManager();
        using var lifecycle = new CoreLifecycleController(store, process, () => true);
        block = true;
        var first = lifecycle.ExecuteAsync(Save(store.Current.ActiveProfile));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            var rejected = await lifecycle.ExecuteAsync(new() { Type = "stop", CoreType = CoreKind.Mihomo });
            Assert.Equal("busy", rejected.Code);
            Assert.False(first.IsCompleted);
        }
        finally { release.Set(); }
        Assert.Equal("completed", (await first).Status);
        Assert.True(await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task FailedSavePreventsCoreActionAndLeavesProfileUnchanged()
    {
        var fail = false;
        var store = new SettingsStore(_directory, persist: (path, json) =>
        { if (fail) throw new IOException("test persistence failure"); SettingsStore.WriteAtomically(path, json); });
        using var process = new CoreProcessManager();
        using var lifecycle = new CoreLifecycleController(store, process, () => true);
        fail = true;
        var result = await lifecycle.ExecuteAsync(Save(store.Current.ActiveProfile) with { Type = "start" });
        Assert.Equal("failed", result.Status);
        Assert.False(result.Saved);
        Assert.Equal(0, store.Current.ActiveProfile.Revision);
        Assert.False(process.IsRunning);
        Assert.True(await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ClosingRejectsNewWorkAndSetupCannotCompleteBeforeReady()
    {
        var store = new SettingsStore(_directory);
        using var process = new CoreProcessManager();
        using var lifecycle = new CoreLifecycleController(store, process, () => true);
        Assert.Equal("apiNotReady", (await lifecycle.ExecuteAsync(new() { Type = "completeSetup", CoreType = CoreKind.Mihomo })).Code);
        Assert.False(store.Current.SetupCompleted);
        await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("closing", (await lifecycle.ExecuteAsync(Save(store.Current.ActiveProfile))).Code);
    }

    [Fact]
    public async Task ElevationIsAnExplicitResultNotAFalseStartedState()
    {
        var store = new SettingsStore(_directory);
        var profile = store.Current.ActiveProfile;
        Directory.CreateDirectory(Path.GetDirectoryName(profile.ExePath)!);
        File.WriteAllText(profile.ExePath, "fixture, not executed");
        File.WriteAllText(profile.ConfigPath, "fixture");
        using var process = new CoreProcessManager();
        using var lifecycle = new CoreLifecycleController(store, process, () => false);
        var result = await lifecycle.ExecuteAsync(new() { Type = "start", CoreType = CoreKind.Mihomo });
        Assert.Equal("elevationRequired", result.Status);
        Assert.False(process.IsRunning);
        await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SwitchPersistsOnlyTargetDraftAndStaleRevisionHasNoSideEffects()
    {
        var store = new SettingsStore(_directory);
        var source = store.Current.ActiveProfile;
        var target = store.Current.Profile(CoreKind.SingBox);
        using var process = new CoreProcessManager();
        using var lifecycle = new CoreLifecycleController(store, process, () => false);
        var edit = new CoreProfileEdit
        {
            ExePath = Path.Combine(_directory, "target.exe"), ConfigPath = target.ConfigPath,
            ApiUrl = target.ApiUrl
        };
        Directory.CreateDirectory(Path.GetDirectoryName(edit.ConfigPath)!);
        File.WriteAllText(edit.ExePath, "fixture, never executed without elevation");
        File.WriteAllText(edit.ConfigPath, "{}");
        var request = new HostRequest { Type = "switchCore", CoreType = CoreKind.SingBox, ExpectedRevision = target.Revision, Draft = edit };
        var stale = await lifecycle.ExecuteAsync(request with { ExpectedRevision = target.Revision + 1 });
        Assert.Equal("staleRevision", stale.Code);
        Assert.False(stale.Saved);
        Assert.Equal(target, store.Current.Profile(CoreKind.SingBox));
        Assert.Equal(CoreKind.Mihomo, store.Current.ActiveCoreKind);
        var switched = await lifecycle.ExecuteAsync(request);
        Assert.True(switched.Saved);
        Assert.Equal("elevationRequired", switched.Status);
        Assert.Equal(edit.ExePath, store.Current.Profile(CoreKind.SingBox).ExePath);
        Assert.Equal(target.Revision + 1, store.Current.Profile(CoreKind.SingBox).Revision);
        Assert.Equal(source, store.Current.Profile(CoreKind.Mihomo));
        Assert.False(process.IsRunning);
        await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(2));
    }

}
