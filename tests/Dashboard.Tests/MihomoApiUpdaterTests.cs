using System.Diagnostics;
using System.Net;
using System.Reflection;
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

    [Theory]
    [InlineData(401, "升级失败：API 密钥不正确，请检查面板设置。")]
    [InlineData(404, "升级失败：当前 mihomo 版本不支持在线升级接口。")]
    [InlineData(500, "升级失败：upgrade unavailable")]
    public async Task LifecycleCommandReturnsFriendlyUpgradeFailure(int status, string expected)
    {
        using var directory = new TestDirectory();
        await using var api = new SyntheticClashApi { UpgradeStatusCode = status };
        var store = new SettingsStore(directory.Path);
        var profile = store.Current.ActiveProfile;
        await store.UpdateProfileAsync(CoreKind.Mihomo, profile.Revision, new()
        {
            ExePath = profile.ExePath, ConfigPath = profile.ConfigPath, ApiUrl = api.Origin
        });
        using var observed = Process.GetCurrentProcess();
        using var process = new CoreProcessManager(_ => { }, (_, _) => false);
        using var lifecycle = new CoreLifecycleController(store, process, () => true);
        var ownedProcess = typeof(CoreProcessManager).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ownedProcess.SetValue(process, observed);
        try
        {
            Assert.Equal("completed", lifecycle.RefreshConnection().Status);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (lifecycle.ApiStatus == "checking") await Task.Delay(20, timeout.Token);
            Assert.Equal("ready", lifecycle.ApiStatus);
            var result = await lifecycle.ExecuteAsync(new() { Type = "upgradeCore", CoreType = CoreKind.Mihomo });
            Assert.Equal("failed", result.Status);
            Assert.Equal(expected, result.Message);
            Assert.Equal(1, api.Requests["/upgrade"]);
        }
        finally
        {
            // The observed testhost process is never owned or terminated by cleanup.
            ownedProcess.SetValue(process, null);
            await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(2));
        }
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
