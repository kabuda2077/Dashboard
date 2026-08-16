using System.Net;
using Dashboard;

namespace Dashboard.Tests;

public sealed class MihomoApiUpdaterTests
{
    [Theory]
    [InlineData("http://127.0.0.1:9090", "http://127.0.0.1:9090/upgrade")]
    [InlineData("http://127.0.0.1:9090/", "http://127.0.0.1:9090/upgrade")]
    [InlineData("https://example.test/api?old=1", "https://example.test/api/upgrade")]
    public void BuildsUpgradeEndpointFromConfiguredApiUrl(string apiUrl, string expected)
    {
        Assert.Equal(expected, MihomoApiUpdater.BuildUpgradeUri(apiUrl).AbsoluteUri);
    }

    [Fact]
    public async Task PostsUpgradeRequestWithBearerSecret()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new DelegateHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var client = new HttpClient(handler);

        var result = await MihomoApiUpdater.UpgradeAsync(
            client,
            "http://127.0.0.1:9090",
            " dashboard-secret ");

        Assert.False(result.IsAlreadyLatest);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("http://127.0.0.1:9090/upgrade", capturedRequest.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("dashboard-secret", capturedRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task TreatsAlreadyLatestResponseAsNormalResult()
    {
        var handler = new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{"message":"update error: already using latest version v1.19.29"}""")
        });
        using var client = new HttpClient(handler);

        var result = await MihomoApiUpdater.UpgradeAsync(
            client,
            "http://127.0.0.1:9090",
            "");

        Assert.True(result.IsAlreadyLatest);
        Assert.Equal("v1.19.29", result.Version);
    }

    [Fact]
    public async Task ProvidesFriendlyMessageAndKeepsDiagnosticResponseWhenUpgradeFails()
    {
        var handler = new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{"message":"upgrade unavailable"}""")
        });
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<MihomoApiUpgradeException>(() =>
            MihomoApiUpdater.UpgradeAsync(client, "http://127.0.0.1:9090", ""));

        Assert.Equal("升级失败：upgrade unavailable", exception.UserMessage);
        Assert.Contains("HTTP 500", exception.Message);
        Assert.Contains("upgrade unavailable", exception.Message);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
