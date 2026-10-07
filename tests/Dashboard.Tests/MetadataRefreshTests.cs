namespace Dashboard.Tests;

public sealed class MetadataRefreshTests : TemporaryDirectoryTest
{
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
