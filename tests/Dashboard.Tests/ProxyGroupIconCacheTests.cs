using System.Net;

namespace Dashboard.Tests;

public sealed class ProxyGroupIconCacheTests : TemporaryDirectoryTest
{
    private sealed class IconHandler : HttpMessageHandler
    {
        public int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Downloads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("icon") });
        }
    }

    private string ConfigPath => Path.Combine(TestRoot, "config.yaml");
    private static readonly Uri DashboardUri = new("http://127.0.0.1:33291/");
    private ProxyGroupIconCache CreateCache(HttpClient? client = null) => new(Path.Combine(TestRoot, "icon-cache"), client);

    [Fact]
    public async Task PublishesOnlyWebIconsInsideProxyGroups()
    {
        await File.WriteAllTextAsync(ConfigPath, """
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
        var handler = new IconHandler();
        using var client = new HttpClient(handler);
        var cache = CreateCache(client);
        await cache.RefreshAsync(ConfigPath);
        Assert.Equal(new[] { "https://example.test/auto.png", "https://example.test/fallback.svg" }, cache.GetDashboardMap(DashboardUri).Keys);
        Assert.Equal(2, handler.Downloads);
    }

    [Theory]
    [InlineData("https://example.test/icon.png")]
    [InlineData("https://example.test/a/../icon.png")]
    [InlineData("https://Example.TEST/icon.png")]
    public async Task ConfigUrlsPublishCanonicalKeysOnDownloadAndColdCacheScan(string iconUrl)
    {
        await File.WriteAllTextAsync(ConfigPath, $"proxy-groups:\n  - name: A\n    icon: {iconUrl}\n");
        var handler = new IconHandler();
        using var client = new HttpClient(handler);
        var cache = CreateCache(client);
        await cache.RefreshAsync(ConfigPath);
        var downloaded = Assert.Single(cache.GetDashboardMap(DashboardUri));
        Assert.Equal(new Uri(iconUrl).AbsoluteUri, downloaded.Key);
        Assert.StartsWith(DashboardUri + "__mihomo/icon-cache/", downloaded.Value);

        cache.ClearPublishedMap();
        await cache.LoadExistingAsync(ConfigPath);
        Assert.Equal(downloaded, Assert.Single(cache.GetDashboardMap(DashboardUri)));
        Assert.Equal(1, handler.Downloads);
    }

    [Fact]
    public async Task StaleOrCancelledScanCannotPublishCachedIcons()
    {
        await File.WriteAllTextAsync(ConfigPath, "proxy-groups:\n  - name: A\n    icon: https://example.test/a.png\n");
        using var client = new HttpClient(new IconHandler());
        var cache = CreateCache(client);
        await cache.RefreshAsync(ConfigPath);
        cache.ClearPublishedMap();
        var published = false;
        cache.CacheChanged += (_, _) => published = true;
        await cache.LoadExistingAsync(ConfigPath, isCurrent: () => false);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.LoadExistingAsync(ConfigPath, cancelled.Token));
        Assert.False(published);
        Assert.Empty(cache.GetDashboardMap(DashboardUri));
    }

    [Fact]
    public async Task MissingConfigProducesNoIconUrls()
    {
        using var client = new HttpClient(new IconHandler());
        var cache = CreateCache(client);
        await cache.RefreshAsync(ConfigPath);
        Assert.Empty(cache.GetDashboardMap(DashboardUri));
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
    public async Task RefreshPublishesOnlyWhenTheCachedFileChanges()
    {
        await File.WriteAllTextAsync(ConfigPath, "proxy-groups:\n  - name: A\n    icon: https://Example.TEST/icon.png\n");
        using var client = new HttpClient(new IconHandler());
        var cache = CreateCache(client);
        var changes = 0;
        cache.CacheChanged += (_, _) => changes++;
        await cache.RefreshAsync(ConfigPath);
        await cache.RefreshAsync(ConfigPath);
        Assert.Equal(1, changes);
        var file = Assert.Single(Directory.GetFiles(cache.CacheDirectory));
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(1));
        await cache.RefreshAsync(ConfigPath);
        Assert.Equal(2, changes);
    }
}
