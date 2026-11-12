namespace Dashboard.Tests;

public sealed class MetadataRefreshTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData(CoreKind.Mihomo, "Mihomo Meta TanakaLun-Alpha-9fc52a02 windows amd64", "TanakaLun/mihomo")]
    [InlineData(CoreKind.SingBox, "sing-box version 1.14.0-beta.15-reF1nd", "reF1nd/sing-box-releases")]
    public async Task ConfigChangesPreserveTheReleaseLinkAndRefreshTunWithoutRereadingTheExecutable(
        CoreKind kind, string raw, string repository)
    {
        var exe = Path.Combine(TestRoot, "fixture.exe");
        var config = Path.Combine(TestRoot, "config.json");
        await File.WriteAllTextAsync(exe, "Version-reader fixture; never executed.");
        await File.WriteAllTextAsync(config, """{"inbounds":[{"type":"tun"}]}""");
        var store = new SettingsStore(TestRoot);
        await store.UpdateProfileAsync(kind, 0, new() { ExePath = exe, ConfigPath = config, ApiUrl = "http://127.0.0.1:1" });
        await store.SetActiveCoreAsync(kind);
        var reads = 0;
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true, () => false,
            readCoreVersion: (_, _, _) => { Interlocked.Increment(ref reads); return Task.FromResult(raw); });
        try
        {
            await host.RefreshMetadataAsync();
            var initial = host.BuildRuntimeState(false);
            var releaseUrl = "https://github.com/" + repository + "/releases";
            Assert.Equal("manual", initial.CoreUpdateMode);
            Assert.Equal(releaseUrl, initial.CoreReleaseUrl);
            Assert.Equal(kind == CoreKind.SingBox ? (bool?)true : null, initial.ReadOnlyTunEnabled);
            Assert.Equal(1, reads);

            await File.WriteAllTextAsync(config, """{"inbounds":[]}""");
            var beforeRefresh = host.BuildRuntimeState(false);
            Assert.Equal("manual", beforeRefresh.CoreUpdateMode);
            Assert.Equal(releaseUrl, beforeRefresh.CoreReleaseUrl);
            Assert.Equal(initial.CoreVersion, beforeRefresh.CoreVersion);
            Assert.Null(beforeRefresh.ReadOnlyTunEnabled);

            await host.RefreshMetadataAsync();
            var refreshed = host.BuildRuntimeState(false);
            Assert.Equal(releaseUrl, refreshed.CoreReleaseUrl);
            Assert.Equal(kind == CoreKind.SingBox ? (bool?)false : null, refreshed.ReadOnlyTunEnabled);
            Assert.Equal(1, reads);
        }
        finally { await host.ShutdownAsync(); }
    }

    [Fact]
    public async Task ExplicitRefreshRetriesFailedUnchangedFilesAndSuccessfulCacheIsReusable()
    {
        var root = TestRoot;
        Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "fixture.exe"); var config = Path.Combine(root, "config.yaml");
        await File.WriteAllTextAsync(exe, "test dependency, never executed");
        await File.WriteAllTextAsync(config, "proxy-groups: []\n");
        var store = new SettingsStore(root);
        await store.UpdateProfileAsync(CoreKind.Mihomo, 0, new() { ExePath = exe, ConfigPath = config, ApiUrl = "http://127.0.0.1:1" });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        using var host = new DashboardHost(root, Path.Combine(root, "ui"), true, () => false, readCoreVersion: async (_, _, token) =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            { entered.TrySetResult(); await release.Task.WaitAsync(token); throw new TimeoutException("transient first read"); }
            return "Mihomo Meta v9.9.9";
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var retry = host.RefreshMetadataAsync();
            release.SetResult();
            await retry.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("9.9.9", host.BuildRuntimeState(false).CoreVersion);
            Assert.Equal(2, reads);
            await host.RefreshMetadataAsync();
            Assert.Equal(2, reads);
            await host.RefreshMetadataAsync(force: true);
            Assert.Equal(3, reads);
        }
        finally { release.TrySetResult(); await host.ShutdownAsync(); CleanupTestDirectory(); }
    }
}
