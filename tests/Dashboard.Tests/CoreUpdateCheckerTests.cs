using System.Net;

namespace Dashboard.Tests;

public sealed class CoreUpdateCheckerTests
{
    [Theory]
    [InlineData("Mihomo Meta alpha-smart-ab12 windows amd64 with go1.26.8", "alpha-smart-ab12")]
    [InlineData("Mihomo Meta v1.19.31 windows amd64 with go1.26.8", "v1.19.31")]
    [InlineData("unknown output with go1.26.8", "")]
    public void DisplayVersionDoesNotMistakeTheGoVersionForTheCore(string output, string expected) =>
        Assert.Equal(expected, CoreUpdateChecker.DisplayVersion(output, CoreKind.Mihomo));

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

    [Theory]
    [InlineData("alpha-abc123", false)]
    [InlineData("beta-abc123", false)]
    [InlineData("meta-abc123", false)]
    [InlineData("beta-def456", true)]
    [InlineData("meta-def456", true)]
    public async Task HashBuildsUseAlphaReleaseAssets(string installedVersion, bool expectedUpdate)
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
            $"Mihomo Meta {installedVersion} windows amd64",
            isSingBox: false);

        Assert.Equal(expectedUpdate, result.UpdateAvailable);
        Assert.Equal("https://api.github.com/repos/MetaCubeX/mihomo/releases/tags/Prerelease-Alpha", handler.RequestUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("Mihomo Meta TanakaLun-Alpha-9fc52a02 windows amd64", false, "manualCoreUpdate")]
    [InlineData("Mihomo Meta alpha-smart-ab12 windows amd64", false, "manualCoreUpdate")]
    [InlineData("Mihomo Smart v1.19.29 windows amd64", false, "manualCoreUpdate")]
    [InlineData("Mihomo Meta custom-v1.19.29 windows amd64", false, "unknownCoreSource")]
    [InlineData("unexpected output with go1.26.8", false, "unknownCoreSource")]
    [InlineData("sing-box version 1.14.0-beta.15-reF1nd", true, "manualCoreUpdate")]
    [InlineData("sing-box version 1.14.0-rc.1", true, "manualCoreUpdate")]
    [InlineData("sing-box version 1.14.0-custom", true, "unknownCoreSource")]
    public async Task NonautomaticSourcesCannotMakeReleaseRequests(string output, bool singBox, string code)
    {
        var handler = new StubHandler("{}");
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CoreUpdateChecker.CheckReleaseAsync(client, output, singBox));
        Assert.Equal(code, error.Message);
        Assert.Null(handler.RequestUri);
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
            var read = CoreVersionReader.ReadProcessAsync(process, cancellation.Token);
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
