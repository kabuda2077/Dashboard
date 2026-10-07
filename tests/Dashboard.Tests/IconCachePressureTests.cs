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
    private sealed class Body(int bytes, int delayMs = 0) : Stream
    {
        private int _remaining = bytes;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
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
    private string Config(string name, params string[] urls)
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, name + ".yaml");
        File.WriteAllText(file, "proxy-groups:\n" + string.Join("\n", urls.Select((url, i) => $"  - name: g{i}\n    icon: {url}")));
        return file;
    }
    private ProxyGroupIconCache Cache(HttpClient client, TimeSpan? download = null, TimeSpan? overall = null) =>
        new(Path.Combine(_root, "icons"), client, download, overall);

    [Fact]
    public async Task RepeatedRefreshDoesNotDownloadAgainAndChangingConfigsCannotGrowAliasMap()
    {
        var handler = new Handler(_ => new ByteArrayContent([1, 2, 3]));
        using var client = new HttpClient(handler);
        var cache = Cache(client);
        var config = Config("current", "https://Example.test/icon.png");
        for (var i = 0; i < 100; i++) await cache.RefreshAsync(config);
        Assert.Equal(1, handler.Calls);
        for (var i = 0; i < 300; i++)
        {
            config = Config("current", $"https://example.test/{i}.png");
            await cache.RefreshAsync(config);
            Assert.Single(cache.GetDashboardMap(new Uri("http://127.0.0.1/")));
        }
        Assert.True(Directory.GetFiles(cache.CacheDirectory).Length <= ProxyGroupIconCache.MaxCacheFiles);
        Assert.Empty(Directory.GetFiles(cache.CacheDirectory, "*.tmp"));
    }

    [Fact]
    public async Task UnknownLengthOversizedBodyLeavesNoFileOrMapping()
    {
        using var body = new Body(3 * 1024 * 1024);
        using var client = new HttpClient(new Handler(_ => new StreamContent(body)));
        var cache = Cache(client);
        await cache.RefreshAsync(Config("large", "https://example.test/large.png"));
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
        using var client = new HttpClient(new Handler(_ => new StreamContent(body)));
        var cache = Cache(client,
            overall ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(100),
            overall ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(10));
        var refresh = cache.RefreshAsync(Config("slow", "https://example.test/slow.png"));
        if (overall) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        else await refresh;
        Assert.True(body.Disposed);
        Assert.Empty(Directory.GetFiles(cache.CacheDirectory));
    }

    [Fact]
    public async Task CancelledOldRefreshReleasesGateAndCannotPublishOverNewConfiguration()
    {
        using var body = new Body(100, 30000);
        using var client = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.Contains("slow") ? new StreamContent(body) : new ByteArrayContent([1])));
        var cache = Cache(client);
        using var cancellation = new CancellationTokenSource();
        var first = cache.RefreshAsync(Config("slow", "https://example.test/slow.png"), cancellation.Token);
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = cache.RefreshAsync(Config("new", "https://example.test/new.png"));
        Assert.False(second.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("https://example.test/new.png", Assert.Single(cache.GetDashboardMap(new Uri("http://127.0.0.1/"))).Key);
        Assert.Empty(Directory.GetFiles(cache.CacheDirectory, "*.tmp"));
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
