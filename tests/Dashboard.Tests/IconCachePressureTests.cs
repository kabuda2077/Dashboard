using System.Net;

namespace Dashboard.Tests;

public sealed class IconCachePressureTests : TemporaryDirectoryTest
{
    private string _root => TestRoot;
    private sealed class Handler(Func<HttpRequestMessage, HttpContent> content) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content(request) }); }
    }
    private sealed class Body(int bytes, int delayMs = 0, Task? release = null) : Stream
    {
        private int _remaining = bytes;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (release is not null) await release.WaitAsync(cancellationToken);
            if (delayMs > 0) await Task.Delay(delayMs, cancellationToken);
            var size = Math.Min(buffer.Length, _remaining);
            buffer.Span[..size].Fill(1); _remaining -= size; return size;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private ProxyGroupIconCache Cache(HttpClient client, TimeSpan? download = null, TimeSpan? overall = null) =>
        new(Path.Combine(_root, "icons"), client, download, overall);

    [Fact]
    public async Task RepeatedRefreshDoesNotDownloadAgainAndChangingInputsCannotGrowAliasMap()
    {
        var handler = new Handler(_ => new ByteArrayContent([1, 2, 3]));
        using var client = new HttpClient(handler);
        var cache = Cache(client);
        string[] urls = ["https://Example.test/icon.png"];
        for (var i = 0; i < 100; i++) await cache.RefreshAsync(urls);
        Assert.Equal(1, handler.Calls);
        for (var i = 0; i < 300; i++)
        {
            urls = [$"https://example.test/{i}.png"];
            await cache.RefreshAsync(urls);
            Assert.Single(cache.GetDashboardMap(new Uri("http://127.0.0.1/")));
        }
        Assert.True(Directory.GetFiles(cache.CacheDirectory).Length <= ProxyGroupIconCache.MaxCacheFiles);
        Assert.Empty(Directory.GetFiles(cache.CacheDirectory, "*.tmp"));
    }

    [Fact]
    public async Task FinalInputIsCappedAfterFilteringAndCanonicalDeduplication()
    {
        var handler = new Handler(_ => new ByteArrayContent([1]));
        using var client = new HttpClient(handler);
        var cache = Cache(client);
        var urls = Enumerable.Range(0, ProxyGroupIconCache.MaxCacheFiles + 10)
            .SelectMany(index => new[] { "file:///tmp/icon.png", $"https://Example.test/{index}.png", $"https://example.test/a/../{index}.png" });

        await cache.RefreshAsync(urls);

        Assert.Equal(ProxyGroupIconCache.MaxCacheFiles, handler.Calls);
        Assert.Equal(ProxyGroupIconCache.MaxCacheFiles, cache.GetDashboardMap(new Uri("http://127.0.0.1/")).Count);
        Assert.Equal(ProxyGroupIconCache.MaxCacheFiles, Directory.GetFiles(cache.CacheDirectory).Length);
    }

    [Fact]
    public async Task ConcurrentRefreshesOfTheSameUrlDownloadOnlyOnce()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var body = new Body(1, release: release.Task);
        var handler = new Handler(_ => new StreamContent(body));
        using var client = new HttpClient(handler);
        var cache = Cache(client);
        string[] urls = ["https://example.test/shared.png"];
        var first = cache.RefreshAsync(urls);
        Task second = Task.CompletedTask;
        try
        {
            await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second = cache.RefreshAsync(urls);
            Assert.False(second.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(1, handler.Calls);
        Assert.Single(cache.GetDashboardMap(new Uri("http://127.0.0.1/")));
        Assert.Empty(Directory.GetFiles(cache.CacheDirectory, "*.tmp"));
    }

    [Fact]
    public async Task UnknownLengthOversizedBodyLeavesNoFileOrMapping()
    {
        using var body = new Body(3 * 1024 * 1024);
        using var client = new HttpClient(new Handler(_ => new StreamContent(body)));
        var cache = Cache(client);
        await cache.RefreshAsync(["https://example.test/large.png"]);
        Assert.True(body.Disposed);
        Assert.Empty(cache.GetDashboardMap(new Uri("http://127.0.0.1/")));
        Assert.Empty(Directory.GetFiles(cache.CacheDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowBodyObeysPerFileAndOverallDeadlinesAndDeletesItsTemporaryFile(bool overall)
    {
        using var body = new Body(100, 30000);
        var handler = new Handler(_ => new StreamContent(body));
        using var client = new HttpClient(handler);
        var cache = Cache(client,
            overall ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(100),
            overall ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(10));
        string[] urls = ["https://example.test/slow.png"];
        var refresh = cache.RefreshAsync(urls);
        if (overall) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        else await refresh;
        await cache.RefreshAsync(urls);
        Assert.Equal(1, handler.Calls);
        Assert.True(body.Disposed);
        Assert.Empty(Directory.GetFiles(cache.CacheDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledOldRefreshReleasesGateWithoutPublishingOrBackingOff(bool retryCancelledUrl)
    {
        using var body = new Body(100, 30000);
        var requests = 0;
        using var client = new HttpClient(new Handler(_ => ++requests == 1 ? new StreamContent(body) : new ByteArrayContent([1])));
        var cache = Cache(client);
        var publications = 0;
        cache.CacheChanged += (_, _) => publications++;
        using var cancellation = new CancellationTokenSource();
        var first = cache.RefreshAsync(["https://example.test/slow.png"], cancellation.Token);
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var nextUrl = retryCancelledUrl ? "https://example.test/slow.png" : "https://example.test/new.png";
        var second = cache.RefreshAsync([nextUrl]);
        Assert.False(second.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(nextUrl, Assert.Single(cache.GetDashboardMap(new Uri("http://127.0.0.1/"))).Key);
        Assert.Equal(1, publications);
        Assert.Empty(Directory.GetFiles(cache.CacheDirectory, "*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveIconSurvivesNewerInactiveFilesWithinBothLimits(bool bytePressure)
    {
        using var client = new HttpClient(new Handler(_ => new ByteArrayContent([1, 2, 3])));
        var cache = Cache(client);
        await cache.RefreshAsync(["https://example.test/active.png"]);
        var dashboard = new Uri("http://127.0.0.1/");
        var published = Assert.Single(cache.GetDashboardMap(dashboard));
        var activeFile = Assert.Single(Directory.GetFiles(cache.CacheDirectory));
        File.SetLastWriteTimeUtc(activeFile, DateTime.UtcNow.AddDays(-31));
        var inactiveCount = bytePressure ? 32 : ProxyGroupIconCache.MaxCacheFiles;
        for (var index = 0; index < inactiveCount; index++)
        {
            using var file = File.Create(Path.Combine(cache.CacheDirectory, $"inactive-{index}.png"));
            file.SetLength(bytePressure ? 2 * 1024 * 1024 : 1);
        }

        Assert.False(cache.PruneCache());

        Assert.True(File.Exists(activeFile));
        Assert.Equal(published, Assert.Single(cache.GetDashboardMap(dashboard)));
        var retained = new DirectoryInfo(cache.CacheDirectory).GetFiles();
        Assert.Equal(bytePressure ? 32 : ProxyGroupIconCache.MaxCacheFiles, retained.Length);
        Assert.True(retained.Sum(file => file.Length) <= ProxyGroupIconCache.MaxCacheBytes);
    }

    [Fact]
    public void TotalByteLimitIsIndependentOfTheFileCountLimit()
    {
        using var client = new HttpClient(new Handler(_ => new ByteArrayContent([1])));
        var cache = Cache(client);
        for (var i = 0; i < 40; i++)
        {
            using var file = File.Create(Path.Combine(cache.CacheDirectory, i + ".png"));
            file.SetLength(2 * 1024 * 1024);
        }
        cache.PruneCache();
        var retained = new DirectoryInfo(cache.CacheDirectory).GetFiles();
        Assert.Equal(32, retained.Length);
        Assert.True(retained.Sum(file => file.Length) <= ProxyGroupIconCache.MaxCacheBytes);
    }
}
