using System.Net;

namespace Dashboard.Tests;

public sealed class CoreApiProbeTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }

    [Fact]
    public async Task RetriesRequestTimeoutWithoutConsumingEntireBudget()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            Assert.Equal("Bearer test", request.Headers.Authorization!.ToString());
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        Assert.True(await CoreApiProbe.WaitAsync(client, "http://localhost:1", "test",
            TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(5), TimeSpan.Zero, default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TotalBudgetBoundsRepeatedUnresponsiveRequests()
    {
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        Assert.False(await CoreApiProbe.WaitAsync(client, "http://localhost:1", "",
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(30), TimeSpan.Zero, default));
    }

    [Fact]
    public async Task ShutdownCancellationIsNotReportedAsTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CoreApiProbe.WaitAsync(client,
            "http://localhost:1", "", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.Zero, cancellation.Token));
    }
}
