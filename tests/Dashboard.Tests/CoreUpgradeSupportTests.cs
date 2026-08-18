using System.Net;

namespace Dashboard.Tests;

public sealed class CoreUpgradeSupportTests
{
    [Fact]
    public async Task RetriesTransientReleaseMetadataFailure()
    {
        var handler = new RetryHandler();
        using var client = new HttpClient(handler);

        using var document = await CoreUpgradeSupport.GetReleaseJsonAsync(
            client,
            "https://api.github.com/repos/example/core/releases/latest",
            CancellationToken.None);

        Assert.Equal("v1.2.3", document.RootElement.GetProperty("tag_name").GetString());
        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class RetryHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                return Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("response ended prematurely"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v1.2.3"}""")
            });
        }
    }
}
