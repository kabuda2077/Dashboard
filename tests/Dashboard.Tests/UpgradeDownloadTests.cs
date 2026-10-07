using System.Net;

namespace Dashboard.Tests;

public sealed class UpgradeDownloadTests : TemporaryDirectoryTest
{
    private sealed class SlowStream(int delayMs) : Stream
    {
        public bool Disposed { get; private set; }
        public int Reads { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            await Task.Delay(delayMs, token);
            buffer.Span[0] = 1;
            Reads++;
            return 1;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class Handler(Stream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
    }

    [Theory]
    [InlineData("overall")]
    [InlineData("idle")]
    [InlineData("caller")]
    public async Task SlowBodiesCannotOutliveOverallIdleOrCallerBudgets(string limit)
    {
        var root = TestRoot;
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "release.zip");
        using var stream = new SlowStream(limit == "idle" ? 30000 : 10);
        using var client = new HttpClient(new Handler(stream));
        using var caller = new CancellationTokenSource();
        if (limit == "caller") caller.CancelAfter(TimeSpan.FromMilliseconds(150));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CoreUpgradeSupport.DownloadFileAsync(
                client, "https://example.test/release.zip", archive, caller.Token,
                overallTimeout: limit == "overall" ? TimeSpan.FromMilliseconds(150) : TimeSpan.FromSeconds(10),
                idleTimeout: limit == "idle" ? TimeSpan.FromMilliseconds(150) : TimeSpan.FromSeconds(10)));
            Assert.True(stream.Disposed);
            // The owner can clean its temporary archive immediately: no outstanding file handle.
            File.Delete(archive);
            Assert.False(File.Exists(archive));
        }
        finally { CleanupTestDirectory(); }
    }
}
