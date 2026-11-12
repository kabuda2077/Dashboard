using System.Diagnostics;
using System.Reflection;

namespace Dashboard.Tests;

public sealed class CoreManualUpdateTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData(CoreKind.Mihomo, "Mihomo Meta TanakaLun-Alpha-9fc52a02 windows amd64", "manual", "TanakaLun/mihomo", "manualCoreUpdate")]
    [InlineData(CoreKind.Mihomo, "Mihomo Meta alpha-smart-ab12 windows amd64", "manual", "vernesong/mihomo", "manualCoreUpdate")]
    [InlineData(CoreKind.Mihomo, "Mihomo Meta custom-v1.19.29 windows amd64", "unknown", "", "unknownCoreSource")]
    [InlineData(CoreKind.SingBox, "sing-box version 1.14.0-beta.15-reF1nd", "manual", "reF1nd/sing-box-releases", "manualCoreUpdate")]
    [InlineData(CoreKind.SingBox, "sing-box version 1.14.0-rc.1", "manual", "SagerNet/sing-box", "manualCoreUpdate")]
    [InlineData(CoreKind.SingBox, "sing-box version 1.14.0-custom", "unknown", "", "unknownCoreSource")]
    public async Task LocalSourceControlsRuntimeAndUpgradeEvenWhenApiReportsOfficialVersion(
        CoreKind kind, string raw, string mode, string repository, string code)
    {
        await using var api = new SyntheticClashApi();
        var store = await PrepareAsync(kind, api.Origin);
        var original = await File.ReadAllBytesAsync(store.Current.Profile(kind).ExePath);
        var kills = 0;
        using var core = new CoreProcessManager(_ => kills++, (_, _) => false);
        using var observed = StartObservedProcess();
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true, () => true,
            readCoreVersion: (_, _, _) => Task.FromResult(raw), core: core);
        var processField = typeof(CoreProcessManager).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!;
        processField.SetValue(core, observed);
        try
        {
            var lifecycle = (CoreLifecycleController)typeof(DashboardHost).GetField("_lifecycle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            lifecycle.ProbeTiming = (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(20));
            Assert.Equal("completed", lifecycle.RefreshConnection().Status);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            while (lifecycle.ApiStatus == "checking") await Task.Delay(20, budget.Token);
            Assert.Equal("ready", lifecycle.ApiStatus);
            Assert.False(lifecycle.ApiIsManaged);
            await host.RefreshMetadataAsync();
            var state = host.BuildRuntimeState(false);
            Assert.Contains("1.19.31", state.CoreVersion);
            Assert.Equal(mode, state.CoreUpdateMode);
            Assert.Equal(repository.Length == 0 ? "" : "https://github.com/" + repository + "/releases", state.CoreReleaseUrl);
            Assert.False(state.CanUpgradeCore);
            await host.CheckForCoreUpdateAsync();
            Assert.False(host.BuildState(false).CoreUpdateAvailable);
            Assert.Empty(host.BuildState(false).LatestCoreVersion);
            var result = await host.ExecuteAsync(new() { Type = "upgradeCore", CoreType = kind });
            Assert.Equal("rejected", result.Status);
            Assert.Equal(code, result.Code);
            Assert.Equal(observed.Id, core.ProcessId);
            Assert.Equal(0, kills);
            Assert.Equal(original, await File.ReadAllBytesAsync(store.Current.Profile(kind).ExePath));
            Assert.False(Directory.Exists(Path.Combine(TestRoot, "backups")));
            Assert.False(api.Requests.ContainsKey("/upgrade"));
            if (mode == "unknown")
            {
                host.OpenCoreRepositoryPage(); // Both links must be no-ops.
                host.OpenCoreReleasePage();
            }
        }
        finally
        {
            processField.SetValue(core, null);
            await host.ShutdownAsync();
            observed.Kill(entireProcessTree: true);
            await observed.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task ChangedExecutableCannotReuseCachedAutomaticSourceAndUpgradeRereadsVersion()
    {
        var store = await PrepareAsync(CoreKind.Mihomo, "http://127.0.0.1:1");
        var raw = "Mihomo Meta v1.19.29 windows amd64";
        var reads = 0;
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true, () => true,
            readCoreVersion: (_, _, _) => { Interlocked.Increment(ref reads); return Task.FromResult(raw); });
        try
        {
            await host.RefreshMetadataAsync();
            Assert.Equal("automatic", host.BuildRuntimeState(false).CoreUpdateMode);
            raw = "Mihomo Meta TanakaLun-Alpha-9fc52a02 windows amd64";
            await File.AppendAllTextAsync(store.Current.ActiveProfile.ExePath, " changed");
            var stale = host.BuildRuntimeState(false);
            Assert.Equal("unknown", stale.CoreUpdateMode);
            Assert.Empty(stale.CoreReleaseUrl);
            Assert.False(stale.CanUpgradeCore);
            var before = reads;
            Assert.Equal("manualCoreUpdate", (await host.ExecuteAsync(new() { Type = "upgradeCore", CoreType = CoreKind.Mihomo })).Code);
            Assert.True(reads > before);
            await host.RefreshMetadataAsync();
            Assert.Equal("manual", host.BuildRuntimeState(false).CoreUpdateMode);
            Assert.Equal("https://github.com/TanakaLun/mihomo/releases", host.BuildRuntimeState(false).CoreReleaseUrl);
            raw = "unrecognized go1.26.8";
            Assert.Equal("unknownCoreSource", (await host.ExecuteAsync(new() { Type = "upgradeCore", CoreType = CoreKind.Mihomo })).Code);
        }
        finally { await host.ShutdownAsync(); }
    }

    [Theory]
    [InlineData("Mihomo Meta alpha-smart-ab12 windows amd64", "manualCoreUpdate")]
    [InlineData("Mihomo Meta custom-v1.19.29 windows amd64", "unknownCoreSource")]
    public async Task ManagedOfficialApiCannotAuthorizeAnExecutableThatChangesSource(string changedVersion, string code)
    {
        await using var api = new SyntheticClashApi();
        var store = await PrepareAsync(CoreKind.Mihomo, api.Origin);
        var raw = "Mihomo Meta v1.19.29 windows amd64";
        var kills = 0;
        using var observed = Process.GetCurrentProcess();
        using var core = new CoreProcessManager(_ => kills++, (_, _) => false);
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true, () => true,
            readCoreVersion: (_, _, _) => Task.FromResult(raw), core: core);
        var processField = typeof(CoreProcessManager).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!;
        processField.SetValue(core, observed);
        try
        {
            var lifecycle = (CoreLifecycleController)typeof(DashboardHost).GetField("_lifecycle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            lifecycle.RefreshConnection();
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (lifecycle.ApiStatus == "checking") await Task.Delay(20, budget.Token);
            Assert.True(lifecycle.ApiIsManaged);
            await host.RefreshMetadataAsync();
            Assert.True(host.BuildRuntimeState(false).CanUpgradeCore);
            raw = changedVersion; // Even an unchanged metadata key cannot authorize the command.
            var result = await host.ExecuteAsync(new() { Type = "upgradeCore", CoreType = CoreKind.Mihomo });
            Assert.Equal(code, result.Code);
            Assert.Equal("rejected", result.Status);
            Assert.False(api.Requests.ContainsKey("/upgrade"));
            Assert.Equal(observed.Id, core.ProcessId);
            Assert.Equal(0, kills);
            await host.RefreshMetadataAsync(force: true);
            Assert.False(host.BuildRuntimeState(false).CanUpgradeCore);
        }
        finally
        {
            processField.SetValue(core, null); // Never terminate the observed testhost.
            await host.ShutdownAsync();
        }
    }

    [Theory]
    [InlineData(1L, 0L, "staleRevision")]
    [InlineData(0L, 1L, "staleRuntime")]
    public async Task OfficialUpgradeStillRejectsStaleContextBeforeReadingExecutable(long revision, long epoch, string code)
    {
        var store = new SettingsStore(TestRoot);
        using var core = new CoreProcessManager();
        using var lifecycle = new CoreLifecycleController(store, core, () => true,
            readCoreVersion: (_, _, _) => throw new InvalidOperationException("Stale request must not read executable."));
        try
        {
            var result = await lifecycle.ExecuteAsync(new() { Type = "upgradeCore", CoreType = CoreKind.Mihomo,
                ExpectedRevision = revision, ExpectedRuntimeEpoch = epoch });
            Assert.Equal("rejected", result.Status);
            Assert.Equal(code, result.Code);
        }
        finally { await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(2)); }
    }

    [Theory]
    [InlineData(CoreKind.Mihomo, "automatic", "MetaCubeX/mihomo", "TanakaLun/mihomo")]
    [InlineData(CoreKind.SingBox, "manual", "SagerNet/sing-box", "reF1nd/sing-box-releases")]
    public async Task SavedPathsRemainPendingUntilTheRunningCoreStops(
        CoreKind kind, string runningMode, string runningRepository, string pendingRepository)
    {
        await using var api = new SyntheticClashApi();
        var prepared = await PrepareAsync(kind, api.Origin);
        var runningProfile = prepared.Current.Profile(kind);
        await File.WriteAllTextAsync(runningProfile.ConfigPath, """{"inbounds":[{"type":"tun"}]}""");
        var pendingExe = Path.Combine(TestRoot, "pending.exe");
        var pendingConfig = Path.Combine(TestRoot, "pending.json");
        await File.WriteAllTextAsync(pendingExe, "pending binary, never executed");
        await File.WriteAllTextAsync(pendingConfig, """{"inbounds":[]}""");
        var reads = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var observed = Process.GetCurrentProcess();
        using var core = new CoreProcessManager(_ => { }, (_, _) => false);
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true, () => true,
            readCoreVersion: (path, coreKind, _) =>
            {
                reads.Enqueue(path);
                return Task.FromResult(coreKind == CoreKind.Mihomo
                    ? path == pendingExe ? "Mihomo Meta TanakaLun-Alpha-9fc52a02 windows amd64" : "Mihomo Meta v1.19.29 windows amd64"
                    : path == pendingExe ? "sing-box version 1.14.0-reF1nd" : "sing-box version 1.14.0");
            }, core: core);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var processField = typeof(CoreProcessManager).GetField("_process", fields)!;
        var lifecycle = (CoreLifecycleController)typeof(DashboardHost).GetField("_lifecycle", fields)!.GetValue(host)!;
        var settings = (SettingsStore)typeof(DashboardHost).GetField("_settings", fields)!.GetValue(host)!;
        typeof(CoreLifecycleController).GetField("_running", fields)!.SetValue(lifecycle, new CoreLaunchSpec(kind, runningProfile));
        processField.SetValue(core, observed);
        try
        {
            lifecycle.RefreshConnection();
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (lifecycle.ApiStatus == "checking") await Task.Delay(20, budget.Token);
            Assert.True(lifecycle.ApiIsManaged);
            await host.RefreshMetadataAsync();
            Assert.Equal(kind == CoreKind.Mihomo, host.BuildRuntimeState(false).CanUpgradeCore);
            await settings.UpdateProfileAsync(kind, runningProfile.Revision,
                new() { ExePath = pendingExe, ConfigPath = pendingConfig, ApiUrl = api.Origin });
            await host.RefreshMetadataAsync(force: true);
            var runtime = host.BuildRuntimeState(false);
            Assert.True(runtime.RequiresRestart);
            Assert.False(runtime.CanUpgradeCore);
            Assert.Equal(runningMode, runtime.CoreUpdateMode);
            Assert.Equal("https://github.com/" + runningRepository + "/releases", runtime.CoreReleaseUrl);
            Assert.Equal(kind == CoreKind.SingBox ? (bool?)true : null, runtime.ReadOnlyTunEnabled);
            Assert.Contains("1.19.31", runtime.CoreVersion);
            Assert.DoesNotContain(pendingExe, reads);
            Assert.Equal("restartRequired", (await host.ExecuteAsync(new() { Type = "upgradeCore", CoreType = kind })).Code);
            Assert.False(api.Requests.ContainsKey("/upgrade"));

            if (kind == CoreKind.Mihomo)
            {
                // Seed a completed check without contacting GitHub: the badge must use the same runtime file key.
                var key = typeof(DashboardHost).GetMethod("MetadataKey", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [runningProfile]);
                typeof(DashboardHost).GetField("_coreUpdateMetadataKey", fields)!.SetValue(host, key);
                typeof(DashboardHost).GetField("<CoreUpdateAvailable>k__BackingField", fields)!.SetValue(host, true);
                typeof(DashboardHost).GetField("<LatestCoreVersion>k__BackingField", fields)!.SetValue(host, "v1.19.30");
                Assert.True(host.BuildState(false).CoreUpdateAvailable);
                Assert.Equal("v1.19.30", host.BuildState(false).LatestCoreVersion);
            }

            processField.SetValue(core, null);
            await host.RefreshMetadataAsync();
            var stopped = host.BuildRuntimeState(false);
            Assert.False(stopped.RequiresRestart);
            Assert.Equal("manual", stopped.CoreUpdateMode);
            Assert.Equal("https://github.com/" + pendingRepository + "/releases", stopped.CoreReleaseUrl);
            Assert.Equal(kind == CoreKind.SingBox ? (bool?)false : null, stopped.ReadOnlyTunEnabled);
            Assert.Contains(pendingExe, reads);
            Assert.False(host.BuildState(false).CoreUpdateAvailable);
        }
        finally
        {
            processField.SetValue(core, null); // Never terminate the observed testhost.
            await host.ShutdownAsync();
        }
    }

    private async Task<SettingsStore> PrepareAsync(CoreKind kind, string apiUrl)
    {
        Directory.CreateDirectory(TestRoot);
        var exe = Path.Combine(TestRoot, "fixture.exe");
        var config = Path.Combine(TestRoot, "config.json");
        await File.WriteAllTextAsync(exe, "fixture binary, read through injected version reader only");
        await File.WriteAllTextAsync(config, "{}");
        var store = new SettingsStore(TestRoot);
        await store.UpdateProfileAsync(kind, 0, new() { ExePath = exe, ConfigPath = config, ApiUrl = apiUrl });
        await store.SetActiveCoreAsync(kind);
        return store;
    }

    private static Process StartObservedProcess() => Process.Start(new ProcessStartInfo(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/d /c ping -t 127.0.0.1 > nul")
    { UseShellExecute = false, CreateNoWindow = true })!;
}
