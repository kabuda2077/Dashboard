using System.Net;

namespace Dashboard.Tests;

public sealed class ProxyGroupIconCacheTests : TemporaryDirectoryTest
{
    private sealed class IconHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? response = null) : HttpMessageHandler
    {
        public int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Downloads);
            return response?.Invoke(request, token) ?? Task.FromResult(IconResponse());
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private static HttpResponseMessage IconResponse(HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent("icon") };

    private static readonly Uri DashboardUri = new("http://127.0.0.1:33291/");
    private ProxyGroupIconCache CreateCache(HttpClient? client = null, TimeProvider? timeProvider = null) =>
        new(Path.Combine(TestRoot, "icon-cache"), client, timeProvider: timeProvider);

    [Fact]
    public async Task PublishesNormalizedDistinctWebIconsFromFinalInput()
    {
        var handler = new IconHandler();
        using var client = new HttpClient(handler);
        var cache = CreateCache(client);
        await cache.RefreshAsync([
            "https://Example.TEST/a/../custom.svg#fragment",
            "https://example.test/custom.svg#fragment",
            "http://example.test/new-group.png",
            "file:///tmp/local.png",
            "data:image/png;base64,aWNvbg==",
            "/relative.png", "", "not a URL"
        ]);

        Assert.Equal(new[] { "https://example.test/custom.svg#fragment", "http://example.test/new-group.png" },
            cache.GetDashboardMap(DashboardUri).Keys);
        Assert.Equal(2, handler.Downloads);
    }

    [Theory]
    [InlineData("https://example.test/icon.png")]
    [InlineData("https://example.test/a/../icon.png")]
    [InlineData("https://Example.TEST/icon.png")]
    public async Task UrlsPublishCanonicalKeysOnDownloadAndColdCacheScan(string iconUrl)
    {
        var handler = new IconHandler();
        using var client = new HttpClient(handler);
        var cache = CreateCache(client);
        await cache.RefreshAsync([iconUrl]);
        var downloaded = Assert.Single(cache.GetDashboardMap(DashboardUri));
        Assert.Equal(new Uri(iconUrl).AbsoluteUri, downloaded.Key);
        Assert.StartsWith(DashboardUri + "__mihomo/icon-cache/", downloaded.Value);

        var coldCache = CreateCache(client);
        await coldCache.LoadExistingAsync([iconUrl, downloaded.Key, "file:///tmp/local.png"]);
        Assert.Equal(downloaded, Assert.Single(coldCache.GetDashboardMap(DashboardUri)));
        Assert.Equal(1, handler.Downloads);
    }

    [Fact]
    public async Task StaleOrCancelledScanCannotPublishCachedIcons()
    {
        string[] urls = ["https://example.test/a.png"];
        using var client = new HttpClient(new IconHandler());
        var cache = CreateCache(client);
        await cache.RefreshAsync(urls);
        cache.ClearPublishedMap();
        var published = false;
        cache.CacheChanged += (_, _) => published = true;
        await cache.LoadExistingAsync(urls, isCurrent: () => false);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.LoadExistingAsync(urls, cancelled.Token));
        Assert.False(published);
        Assert.Empty(cache.GetDashboardMap(DashboardUri));
    }

    [Fact]
    public async Task EmptyInputRemovesPublishedIconsAndPublishesTheRemoval()
    {
        using var client = new HttpClient(new IconHandler());
        var cache = CreateCache(client);
        await cache.RefreshAsync(["https://example.test/custom.png"]);
        var changes = 0;
        cache.CacheChanged += (_, _) => changes++;

        await cache.RefreshAsync([]);
        await cache.RefreshAsync([]);

        Assert.Empty(cache.GetDashboardMap(DashboardUri));
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedIconRetriesAfterBackoffAndThenUsesTheFreshFile(bool timeout)
    {
        var attempts = 0;
        var handler = new IconHandler((_, _) =>
        {
            if (++attempts == 1)
                return timeout ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("Download timed out."))
                    : Task.FromResult(IconResponse(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(IconResponse());
        });
        using var client = new HttpClient(handler);
        var clock = new ManualTimeProvider();
        var cache = CreateCache(client, clock);
        string[] urls = ["https://example.test/new.png"];
        var changes = 0;
        cache.CacheChanged += (_, _) => changes++;

        await cache.RefreshAsync(urls);
        clock.Advance(TimeSpan.FromSeconds(29));
        await cache.RefreshAsync(urls);
        Assert.Equal(1, handler.Downloads);
        Assert.Empty(cache.GetDashboardMap(DashboardUri));
        Assert.Equal(0, changes);

        clock.Advance(TimeSpan.FromSeconds(1));
        await cache.RefreshAsync(urls);
        await cache.RefreshAsync(urls);
        Assert.Equal(2, handler.Downloads);
        Assert.Single(cache.GetDashboardMap(DashboardUri));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task RemovingFailedUrlDiscardsItsBackoff()
    {
        var handler = new IconHandler((_, _) => Task.FromResult(IconResponse(HttpStatusCode.ServiceUnavailable)));
        using var client = new HttpClient(handler);
        var cache = CreateCache(client, new ManualTimeProvider());
        string[] urls = ["https://example.test/failed.png"];

        await cache.RefreshAsync(urls);
        await cache.RefreshAsync(urls);
        Assert.Equal(1, handler.Downloads);
        await cache.LoadExistingAsync([]);
        await cache.RefreshAsync(urls);
        Assert.Equal(2, handler.Downloads);
    }

    [Fact]
    public async Task FailedRefreshKeepsExistingStaleFileUsableEvenOnAColdCache()
    {
        var handler = new IconHandler();
        using var client = new HttpClient(handler);
        var clock = new ManualTimeProvider();
        var cache = CreateCache(client, clock);
        string[] urls = ["https://example.test/stale.png"];
        await cache.RefreshAsync(urls);
        var file = Assert.Single(Directory.GetFiles(cache.CacheDirectory));
        File.SetLastWriteTimeUtc(file, clock.GetUtcNow().UtcDateTime.AddDays(-31));

        var failingHandler = new IconHandler((_, _) => Task.FromResult(IconResponse(HttpStatusCode.ServiceUnavailable)));
        using var failingClient = new HttpClient(failingHandler);
        var coldCache = CreateCache(failingClient, clock);
        var changes = 0;
        coldCache.CacheChanged += (_, _) => changes++;
        await coldCache.RefreshAsync(urls);
        var fallback = Assert.Single(coldCache.GetDashboardMap(DashboardUri));
        await coldCache.RefreshAsync(urls);

        Assert.Equal(urls[0], fallback.Key);
        Assert.Equal(fallback, Assert.Single(coldCache.GetDashboardMap(DashboardUri)));
        Assert.Equal("icon", await File.ReadAllTextAsync(file));
        Assert.Equal(1, failingHandler.Downloads);
        Assert.Equal(1, changes);
        Assert.Empty(Directory.GetFiles(cache.CacheDirectory, "*.tmp"));
    }

    [Fact]
    public async Task CancelledSessionCannotPruneTheNextSessionsStaleFallback()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualTimeProvider();
        var blockDownload = false;
        var handler = new IconHandler(async (_, token) =>
        {
            if (blockDownload)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return IconResponse();
        });
        using var client = new HttpClient(handler);
        var cache = CreateCache(client, clock);
        string[] urls = ["https://example.test/stale.png"];
        await cache.RefreshAsync(urls);
        var file = Assert.Single(Directory.GetFiles(cache.CacheDirectory));
        File.SetLastWriteTimeUtc(file, clock.GetUtcNow().UtcDateTime.AddDays(-31));
        blockDownload = true;
        using var cancellation = new CancellationTokenSource();
        var oldRefresh = cache.RefreshAsync(urls, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        cache.ClearPublishedMap();
        var nextScan = cache.LoadExistingAsync(urls);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldRefresh);
        await nextScan;
        Assert.True(File.Exists(file));
        Assert.Equal(urls[0], Assert.Single(cache.GetDashboardMap(DashboardUri)).Key);
    }

    [Fact]
    public async Task PublishesFastIconBeforeLaterSlowDownloadCompletes()
    {
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new IconHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/slow.png")
            {
                slowStarted.TrySetResult();
                await releaseSlow.Task.WaitAsync(token);
            }
            return IconResponse();
        });
        using var client = new HttpClient(handler);
        var cache = CreateCache(client);
        var publications = new List<string[]>();
        cache.CacheChanged += (_, _) => publications.Add(cache.GetDashboardMap(DashboardUri).Keys.ToArray());
        var refresh = cache.RefreshAsync(["https://example.test/fast.png", "https://example.test/slow.png"]);
        try
        {
            await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(refresh.IsCompleted);
            Assert.Equal(new[] { "https://example.test/fast.png" }, Assert.Single(publications));
            Assert.Equal("https://example.test/fast.png", Assert.Single(cache.GetDashboardMap(DashboardUri)).Key);
        }
        finally
        {
            releaseSlow.TrySetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(2, publications.Count);
        Assert.Equal(2, cache.GetDashboardMap(DashboardUri).Count);
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
        string[] urls = ["https://Example.TEST/icon.png"];
        using var client = new HttpClient(new IconHandler());
        var cache = CreateCache(client);
        var changes = 0;
        cache.CacheChanged += (_, _) => changes++;
        await cache.RefreshAsync(urls);
        await cache.RefreshAsync(urls);
        Assert.Equal(1, changes);
        var file = Assert.Single(Directory.GetFiles(cache.CacheDirectory));
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(1));
        await cache.RefreshAsync(urls);
        Assert.Equal(2, changes);
    }
}
