using System.Reflection;

namespace Dashboard.Tests;

public sealed class HostIconLifecycleTests : TemporaryDirectoryTest
{
    [Fact]
    public async Task RuntimeIconListsLoadDiskCacheWithoutYamlAndRejectOtherSessions()
    {
        const string icon = "https://fixture.example/google.png";
        var icons = Path.Combine(TestRoot, "resources", "icon-cache");
        Directory.CreateDirectory(icons);
        var file = (string)typeof(ProxyGroupIconCache).GetMethod("GetCacheFileName", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new Uri(icon)])!;
        await File.WriteAllTextAsync(Path.Combine(icons, file), "cached icon");
        // No configuration file is needed: these URLs come from the displayed groups.
        using var host = new DashboardHost(TestRoot, Path.Combine(TestRoot, "ui"), true, () => false);
        try
        {
            var command = new HostRequest
            {
                Type = "cacheProxyIcons", CoreType = CoreKind.Mihomo,
                ExpectedRuntimeEpoch = host.RuntimeEpoch, IconUrls = [icon]
            };
            Assert.Equal("staleSession", (await host.ExecuteAsync(command with { ExpectedRuntimeEpoch = host.RuntimeEpoch + 1 })).Code);
            Assert.Equal("staleSession", (await host.ExecuteAsync(command with { CoreType = CoreKind.SingBox })).Code);
            Assert.Empty(host.GetIconCacheMap());
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            host.IconCacheChanged += (_, _) =>
            {
                if (host.GetIconCacheMap().ContainsKey(icon)) published.TrySetResult();
            };
            Assert.Equal("completed", (await host.ExecuteAsync(command)).Status);
            await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("__mihomo/icon-cache/", host.GetIconCacheMap()[icon]);
            foreach (var kind in new[] { CoreKind.Mihomo, CoreKind.SingBox })
            {
                var profile = host.Settings.Profile(kind);
                var saved = await host.ExecuteAsync(new() { Type = "saveProfile", CoreType = kind,
                    ExpectedRevision = profile.Revision, Draft = new() { ExePath = Path.Combine(TestRoot, "pending.exe"),
                        ConfigPath = profile.ConfigPath, ApiUrl = profile.ApiUrl } });
                Assert.Equal("completed", saved.Status);
                Assert.Equal(command.ExpectedRuntimeEpoch, host.RuntimeEpoch);
                Assert.True(host.GetIconCacheMap().ContainsKey(icon));
            }
            var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            host.IconCacheChanged += (_, _) =>
            {
                if (host.GetIconCacheMap().Count == 0) cleared.TrySetResult();
            };
            await host.ExecuteAsync(command with { IconUrls = [] });
            await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(File.Exists(Path.Combine(icons, file)));
        }
        finally { await host.ShutdownAsync(); }
    }

    [Fact]
    [Trait("Category", "RealCoreIntegration")]
    public async Task SwitchingCoreLoadsAndClearsThePublishedIconMap()
    {
        await using var mihomo = await IsolatedCore.CreateAsync(CoreKind.Mihomo);
        await using var singBox = await IsolatedCore.CreateAsync(CoreKind.SingBox);
        var other = singBox.Store.Current.ActiveProfile;
        await mihomo.Store.UpdateProfileAsync(CoreKind.SingBox, 0, new() { ExePath = other.ExePath, ConfigPath = other.ConfigPath, ApiUrl = other.ApiUrl });
        var config = mihomo.Store.Current.Profiles.Mihomo.ConfigPath;
        const string icon = "https://fixture.example/icon.png";
        await File.WriteAllTextAsync(config, (await File.ReadAllTextAsync(config)).Replace("proxy-groups: []",
            "proxy-groups:\n  - name: Fixture\n    type: select\n    proxies: [DIRECT]\n    icon: " + icon));
        var icons = Path.Combine(mihomo.Root, "resources", "icon-cache"); Directory.CreateDirectory(icons);
        var file = (string)typeof(ProxyGroupIconCache).GetMethod("GetCacheFileName", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [new Uri(icon)])!;
        await File.WriteAllBytesAsync(Path.Combine(icons, file), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/O9cAAAAASUVORK5CYII="));
        await mihomo.Store.SetActiveCoreAsync(CoreKind.SingBox);
        using var host = new DashboardHost(mihomo.Root, Path.Combine(mihomo.Root, "ui"), true, () => true);
        try
        {
            Assert.Empty(host.GetIconCacheMap());
            Assert.Equal("completed", (await host.ExecuteAsync(new() { Type = "switchCore", CoreType = CoreKind.Mihomo })).Status);
            var cache = new HostRequest { Type = "cacheProxyIcons", CoreType = CoreKind.Mihomo,
                ExpectedRuntimeEpoch = host.RuntimeEpoch, IconUrls = [icon] };
            Assert.Equal("completed", (await host.ExecuteAsync(cache)).Status);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!host.GetIconCacheMap().ContainsKey(icon) && DateTime.UtcNow < deadline) await Task.Delay(25);
            Assert.True(host.GetIconCacheMap().ContainsKey(icon));
            Assert.Equal("completed", (await host.ExecuteAsync(new() { Type = "switchCore", CoreType = CoreKind.SingBox })).Status);
            Assert.Empty(host.GetIconCacheMap());
            Assert.Equal("staleSession", (await host.ExecuteAsync(cache)).Code);
            Assert.True(File.Exists(Path.Combine(icons, file))); // published state is cleared, reusable files are not deleted
        }
        finally { await host.ShutdownAsync(); }
    }
}
