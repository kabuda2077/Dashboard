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

    // ProxyIcon.vue looks up the icon twice: the string verbatim, then
    // `new URL(icon).href`. That second read is a case-sensitive JavaScript
    // property access, so a normalized form that differs from the config text
    // has to be present in the map as its own key.
    [Theory]
    // Already normalized: AbsoluteUri equals the input, so one key covers both.
    [InlineData("https://example.test/icon.png", 1)]
    // Path normalization changes the key under any comparer, so both are kept.
    [InlineData("https://example.test/a/../icon.png", 2)]
    // Host case differs only by case. _cachedFiles is OrdinalIgnoreCase, so the
    // normalized form collides with the raw one and a single key is stored.
    // See NormalizedHostCaseCannotBeStoredSeparately for what that costs.
    [InlineData("https://Example.TEST/icon.png", 1)]
    public void NonNormalizedIconUrlsAreRecordedUnderBothForms(string iconUrl, int expectedKeyCount)
    {
        var cache = new ProxyGroupIconCache();

        var changed = InvokeTryRecordCacheFile(cache, iconUrl, "cached.png");
        var keys = GetCachedFileKeys(cache);

        Assert.True(changed);
        Assert.Equal(expectedKeyCount, keys.Count);
        Assert.Contains(iconUrl, keys);
        if (expectedKeyCount == 2)
        {
            Assert.Contains(new Uri(iconUrl).AbsoluteUri, keys);
        }
    }

    // Documents a known limitation rather than asserting desired behavior.
    //
    // When a config writes the host in non-lowercase, the map reaches the
    // frontend keyed by the config's spelling only. If the Clash API ever
    // returns the host lowercased, ProxyIcon.vue's verbatim lookup misses on
    // case, its `new URL(icon).href` lookup misses too, and that group falls
    // back to the remote icon URL. Storing both forms would require changing
    // _cachedFiles to a case-sensitive comparer, which affects every other
    // lookup, so the gap is left in place deliberately.
    [Fact]
    public void NormalizedHostCaseCannotBeStoredSeparately()
    {
        var cache = new ProxyGroupIconCache();
        const string iconUrl = "https://Example.TEST/icon.png";

        InvokeTryRecordCacheFile(cache, iconUrl, "cached.png");
        var keys = GetCachedFileKeys(cache);

        Assert.Single(keys);
        Assert.Contains(iconUrl, keys);
        Assert.DoesNotContain(new Uri(iconUrl).AbsoluteUri, (IEnumerable<string>)keys, StringComparer.Ordinal);
    }

    [Fact]
    public void RecordingTheSameIconTwiceReportsNoChange()
    {
        var cache = new ProxyGroupIconCache();
        const string iconUrl = "https://Example.TEST/icon.png";

        Assert.True(InvokeTryRecordCacheFile(cache, iconUrl, "cached.png"));
        Assert.False(InvokeTryRecordCacheFile(cache, iconUrl, "cached.png"));
        Assert.True(InvokeTryRecordCacheFile(cache, iconUrl, "different.png"));
    }

    private static bool InvokeTryRecordCacheFile(ProxyGroupIconCache cache, string iconUrl, string fileName)
    {
        var method = typeof(ProxyGroupIconCache).GetMethod(
            "TryRecordCacheFile",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(typeof(ProxyGroupIconCache).FullName, "TryRecordCacheFile");

        return (bool)method.Invoke(cache, new object[] { iconUrl, fileName })!;
    }

    private static ICollection<string> GetCachedFileKeys(ProxyGroupIconCache cache)
    {
        var field = typeof(ProxyGroupIconCache).GetField(
            "_cachedFiles",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(typeof(ProxyGroupIconCache).FullName, "_cachedFiles");

        return ((Dictionary<string, string>)field.GetValue(cache)!).Keys;
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
