using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
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
    public async Task DoesNotFollowUpgradeRedirectToSuccessfulLoginPage()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var serve = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var socket = await listener.AcceptTcpClientAsync(stop.Token);
                    using var stream = socket.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var request = await reader.ReadLineAsync(stop.Token);
                    requests.Enqueue(request!);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) { }
                    var response = request!.Contains("/upgrade ", StringComparison.Ordinal)
                        ? "HTTP/1.1 302 Found\r\nLocation: /login\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                        : "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: 5\r\nConnection: close\r\n\r\nlogin";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (SocketException) when (stop.IsCancellationRequested) { }
        });
        try
        {
            var error = await Assert.ThrowsAsync<MihomoApiUpgradeException>(() =>
                MihomoApiUpdater.UpgradeAsync(origin, "", Environment.ProcessId, stop.Token));
            Assert.Contains("HTTP 302", error.Message);
            Assert.Equal("POST /upgrade HTTP/1.1", Assert.Single(requests));
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
            await serve;
        }
    }

    [Fact]
    public async Task RejectsAnExternalConnectionBeforeSendingTheUpgradeRequest()
    {
        await using var api = new SyntheticClashApi();
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => MihomoApiUpdater.UpgradeAsync(api.Origin, "", 0));
        Assert.IsType<CoreApiOwnershipException>(error.InnerException);
        Assert.False(api.Requests.ContainsKey("/upgrade"));
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
        using var lifecycle = new CoreLifecycleController(store, process, () => true,
            readCoreVersion: (_, _, _) => Task.FromResult("Mihomo Meta v1.19.29 windows amd64"));
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
