using System.Reflection;

namespace Dashboard.Tests;

public sealed class HostIconLifecycleTests
{
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
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!host.GetIconCacheMap().ContainsKey(icon) && DateTime.UtcNow < deadline) await Task.Delay(25);
            Assert.True(host.GetIconCacheMap().ContainsKey(icon));
            Assert.Equal("completed", (await host.ExecuteAsync(new() { Type = "switchCore", CoreType = CoreKind.SingBox })).Status);
            Assert.Empty(host.GetIconCacheMap());
            Assert.True(File.Exists(Path.Combine(icons, file))); // published state is cleared, reusable files are not deleted
        }
        finally { await host.ShutdownAsync(); }
    }
}
