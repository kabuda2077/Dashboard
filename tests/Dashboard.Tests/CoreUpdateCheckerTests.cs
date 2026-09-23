using System.Net;

namespace Dashboard.Tests;

public sealed class CoreUpdateCheckerTests
{
    [Theory]
    [InlineData("v1.19.28", true)]
    [InlineData("v1.19.29", false)]
    public async Task ComparesMihomoStableRelease(string installedVersion, bool expected)
    {
        var handler = new StubHandler("""{"tag_name":"v1.19.29","assets":[]}""");
        using var client = new HttpClient(handler);

        var result = await CoreUpdateChecker.CheckReleaseAsync(
            client,
            $"Mihomo Meta {installedVersion} windows amd64",
            isSingBox: false);

        Assert.Equal(expected, result.UpdateAvailable);
        Assert.Contains("MetaCubeX/mihomo/releases/latest", handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task MatchesMihomoAlphaBuildFromReleaseAssets()
    {
        var handler = new StubHandler("""
            {
              "tag_name":"Prerelease-Alpha",
              "assets":[{"name":"mihomo-windows-amd64-alpha-abc123.zip"}]
            }
            """);
        using var client = new HttpClient(handler);

        var result = await CoreUpdateChecker.CheckReleaseAsync(
            client,
            "Mihomo Meta alpha-abc123 windows amd64",
            isSingBox: false);

        Assert.False(result.UpdateAvailable);
        Assert.Contains("Prerelease-Alpha", handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task UsesMatchingSingBoxPrereleaseChannel()
    {
        using var client = new HttpClient(new StubHandler("""
            [
              {
                "tag_name":"v1.14.0-beta.14-reF1nd",
                "prerelease":true,
                "published_at":"2026-08-13T00:00:00Z"
              }
            ]
            """));

        var result = await CoreUpdateChecker.CheckReleaseAsync(
            client,
            "sing-box version 1.14.0-beta.13-reF1nd",
            isSingBox: true);

        Assert.True(result.UpdateAvailable);
        Assert.Equal("v1.14.0-beta.14-reF1nd", result.LatestVersion);
    }

    [Fact]
    public async Task TreatsMatchingSingBoxPrereleaseAsCurrent()
    {
        using var client = new HttpClient(new StubHandler("""
            [
              {
                "tag_name":"v1.14.0-beta.15-reF1nd",
                "prerelease":true,
                "published_at":"2026-08-16T17:25:32Z"
              }
            ]
            """));

        var result = await CoreUpdateChecker.CheckReleaseAsync(
            client,
            "sing-box version 1.14.0-beta.15-reF1nd",
            isSingBox: true);

        Assert.False(result.UpdateAvailable);
        Assert.Equal("1.14.0-beta.15-reF1nd", result.CurrentVersion);
        Assert.Equal("v1.14.0-beta.15-reF1nd", result.LatestVersion);
    }

    [Fact]
    public async Task ExternalCancellationTerminatesVersionProbeProcess()
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            "/d /c ping -t 127.0.0.1 > nul")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        try
        {
            using var cancellation = new CancellationTokenSource();
            var read = CoreUpdateChecker.ReadVersionProcessAsync(process, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(process.HasExited);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private sealed class StubHandler(string content) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
