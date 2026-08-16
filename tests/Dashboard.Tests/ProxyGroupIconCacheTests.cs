using System.Reflection;
using Dashboard;

namespace Dashboard.Tests;

public sealed class ProxyGroupIconCacheTests
{
    [Fact]
    public async Task ExtractsProxyGroupIconsFromYamlOnlyInsideProxyGroups()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Dashboard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var configPath = Path.Combine(tempRoot, "config.yaml");
        await File.WriteAllTextAsync(configPath, """
        mixed-port: 7890
        proxy-groups:
          - name: Auto
            type: select
            icon: "https://example.test/auto.png"
          - name: Fallback
            type: fallback
            icon: 'https://example.test/fallback.svg' # trailing comment
          - name: LocalFile
            type: select
            icon: file:///tmp/local.png
        proxies:
          - name: outside
            icon: https://example.test/outside.png
        """);

        var icons = ExtractIconUrls(configPath);

        Assert.Equal(
            new[] { "https://example.test/auto.png", "https://example.test/fallback.svg" },
            icons);
    }

    [Fact]
    public void MissingConfigProducesNoIconUrls()
    {
        var icons = ExtractIconUrls(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.yaml"));

        Assert.Empty(icons);
    }

    private static string[] ExtractIconUrls(string configPath)
    {
        var method = typeof(ProxyGroupIconCache).GetMethod(
            "ExtractProxyGroupIconUrls",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(ProxyGroupIconCache).FullName, "ExtractProxyGroupIconUrls");

        var values = (IEnumerable<string>)method.Invoke(null, new object[] { configPath })!;
        return values.ToArray();
    }
}
