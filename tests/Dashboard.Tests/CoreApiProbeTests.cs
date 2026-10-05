using System.Net;

namespace Dashboard.Tests;

public sealed class CoreApiProbeTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    [Fact]
    public async Task RetriesTransientTimeoutAndReturnsApiVersion()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            Assert.Equal("Bearer test", request.Headers.Authorization!.ToString());
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.Infinite, token);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"version\":\"v2.0.0\"}") };
        }));
        var result = await CoreApiProbe.ProbeAsync(client, "http://localhost:1", "test", TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(5), TimeSpan.Zero, default);
        Assert.Equal(new CoreApiStatus("ready", "v2.0.0"), result);
        Assert.Equal(2, calls);
    }
    [Fact]
    public async Task BadCredentialsAreNotRetriedForThirtySeconds()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); }));
        var result = await CoreApiProbe.ProbeAsync(client, "http://localhost:1", "", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30), TimeSpan.Zero, default);
        Assert.Equal("unauthorized", result.Status);
        Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("{\"version\":0}")]
    [InlineData("{\"version\":\"\"}")]
    public async Task UnexpectedJsonShapesProduceAFiniteFailure(string body)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) })));
        var status = await CoreApiProbe.ProbeAsync(client, "http://localhost:1", "", TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(1), default);
        Assert.Equal("unreachable", status.Status);
    }

    [Fact]
    public async Task InvalidHeaderCredentialDoesNotThrowOrLeakItsValue()
    {
        using var client = new HttpClient(new Handler((_, _) => throw new Exception("must not send")));
        var status = await CoreApiProbe.ProbeAsync(client, "http://localhost:1", "secret\r\nvalue", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.Zero, default);
        Assert.Equal("unauthorized", status.Status);
    }

    [Fact]
    public async Task BudgetAndExternalCancellationRemainDifferent()
    {
        using var client = new HttpClient(new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); }));
        var result = await CoreApiProbe.ProbeAsync(client, "http://localhost:1", "", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(30), TimeSpan.Zero, default);
        Assert.Equal("unreachable", result.Status);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CoreApiProbe.ProbeAsync(client, "http://localhost:1", "", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30), TimeSpan.Zero, cancelled.Token));
    }
}
