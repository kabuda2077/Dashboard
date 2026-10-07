using System.Reflection;
using Dashboard;

namespace Dashboard.Tests;

public sealed class ProxyGroupIconCacheTests : TemporaryDirectoryTest
{
    [Fact]
    public async Task ExtractsProxyGroupIconsFromYamlOnlyInsideProxyGroups()
    {
        var tempRoot = TestRoot;
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
    public async Task StaleExistingCacheScanDoesNotPublishOrMutateMap()
    {
        var root = TestRoot;
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.yaml");
        const string iconUrl = "https://example.test/a.png";
        await File.WriteAllTextAsync(configPath, $"proxy-groups:\n  - name: A\n    icon: {iconUrl}\n");
        var cache = new ProxyGroupIconCache(Path.Combine(root, "icon-cache"));
        var fileName = InvokeGetCacheFileName(new Uri(iconUrl));
        await File.WriteAllTextAsync(Path.Combine(cache.CacheDirectory, fileName), "cached");
        var published = false;
        cache.CacheChanged += (_, _) => published = true;

        await cache.LoadExistingAsync(configPath, isCurrent: () => false);

        Assert.False(published);
        Assert.Empty(GetCachedFileKeys(cache));
    }

    [Fact]
    public async Task ExistingCacheScanHonorsCancellationWithoutPublishing()
    {
        var root = TestRoot;
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.yaml");
        await File.WriteAllTextAsync(configPath, "proxy-groups:\n  - name: A\n    icon: https://example.test/a.png\n");
        var cache = new ProxyGroupIconCache(Path.Combine(root, "icon-cache"));
        var published = false;
        cache.CacheChanged += (_, _) => published = true;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.LoadExistingAsync(configPath, cancellation.Token));
        Assert.False(published);
    }

    [Fact]
    public void MissingConfigProducesNoIconUrls()
    {
        var icons = ExtractIconUrls(Path.Combine(TestRoot, "missing.yaml"));

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
    // Host normalization must survive the frontend's case-sensitive object lookup.
    [InlineData("https://Example.TEST/icon.png", 2)]
    public void NonNormalizedIconUrlsAreRecordedUnderBothForms(string iconUrl, int expectedKeyCount)
    {
        var cache = CreateCache();

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

    // Both spellings are valid lookup keys in the browser.
    [Fact]
    public void NormalizedHostCaseIsStoredSeparately()
    {
        var cache = CreateCache();
        const string iconUrl = "https://Example.TEST/icon.png";

        InvokeTryRecordCacheFile(cache, iconUrl, "cached.png");
        var keys = GetCachedFileKeys(cache);

        Assert.Equal(2, keys.Count);
        Assert.Contains(iconUrl, keys);
        Assert.Contains(new Uri(iconUrl).AbsoluteUri, keys);
    }

    [Fact]
    public void CachePrunesExpiredFilesAndCapsItsFileCount()
    {
        var cache = CreateCache();
        for (var index = 0; index < ProxyGroupIconCache.MaxCacheFiles + 10; index++)
            File.WriteAllText(Path.Combine(cache.CacheDirectory, $"{index}.png"), "icon");
        var expired = Path.Combine(cache.CacheDirectory, "expired.png");
        File.WriteAllText(expired, "expired");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-31));
        cache.PruneCache();
        Assert.False(File.Exists(expired));
        Assert.Equal(ProxyGroupIconCache.MaxCacheFiles, Directory.GetFiles(cache.CacheDirectory).Length);
    }

    [Fact]
    public void RecordingTheSameIconTwiceReportsNoChange()
    {
        var cache = CreateCache();
        const string iconUrl = "https://Example.TEST/icon.png";

        Assert.True(InvokeTryRecordCacheFile(cache, iconUrl, "cached.png"));
        Assert.False(InvokeTryRecordCacheFile(cache, iconUrl, "cached.png"));
        Assert.True(InvokeTryRecordCacheFile(cache, iconUrl, "different.png"));
    }

    private ProxyGroupIconCache CreateCache() => new(Path.Combine(TestRoot, "icon-cache"));

    private static string InvokeGetCacheFileName(Uri uri)
    {
        var method = typeof(ProxyGroupIconCache).GetMethod(
            "GetCacheFileName",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(ProxyGroupIconCache).FullName, "GetCacheFileName");
        return (string)method.Invoke(null, [uri])!;
    }

    private static bool InvokeTryRecordCacheFile(ProxyGroupIconCache cache, string iconUrl, string fileName)
    {
        var method = typeof(ProxyGroupIconCache).GetMethod(
            "TryRecordCacheFile",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(typeof(ProxyGroupIconCache).FullName, "TryRecordCacheFile");

        return (bool)method.Invoke(cache, new object?[] { iconUrl, fileName, null })!;
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
