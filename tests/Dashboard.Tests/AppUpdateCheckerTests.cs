using System.Net;

namespace Dashboard.Tests;

public sealed class AppUpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.3.0+build.12", "1.3.0")]
    [InlineData("v2.0.1-beta.1", "2.0.1")]
    public void NormalizesReleaseVersions(string value, string expected)
    {
        Assert.Equal(expected, AppUpdateChecker.NormalizeVersion(value));
    }

    [Theory]
    [InlineData("1.2.1", "1.2.0", 1)]
    [InlineData("1.2.0", "1.2.0", 0)]
    [InlineData("1.1.9", "1.2.0", -1)]
    public void ComparesSemanticVersions(string left, string right, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(AppUpdateChecker.CompareVersions(left, right)));
    }

    [Fact]
    public async Task ReadsLatestStableReleaseFromGitHubResponse()
    {
        using var client = new HttpClient(new StubHandler(
            """{"tag_name":"v1.3.0","html_url":"https://github.com/kabuda2077/Dashboard/releases/tag/v1.3.0"}"""));

        var result = await AppUpdateChecker.CheckAsync("1.2.0", client);

        Assert.Equal("1.2.0", result.CurrentVersion);
        Assert.Equal("1.3.0", result.LatestVersion);
        Assert.True(result.UpdateAvailable);
    }

    [Fact]
    public async Task RejectsReleaseWithoutSemanticVersionTag()
    {
        using var client = new HttpClient(new StubHandler("""{"tag_name":"latest"}"""));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AppUpdateChecker.CheckAsync("1.2.0", client));
    }

    private sealed class StubHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
