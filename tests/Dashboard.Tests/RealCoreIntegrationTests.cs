using System.Net;
using System.Net.Sockets;

namespace Dashboard.Tests;

public sealed class RealCoreIntegrationTests : TemporaryDirectoryTest
{
    [Fact]
    [Trait("Category", "RealCoreIntegration")]
    public async Task TwoVerifiedCoresRunSwitchRestartAndAuthenticateAtTheSameEndpoint()
    {
        var sources = Environment.GetEnvironmentVariable("DASHBOARD_TEST_CORES_DIR")
            ?? throw new InvalidOperationException("Run PrepareValidationCores.ps1 and set DASHBOARD_TEST_CORES_DIR before explicitly selecting this suite.");
        var root = TestRoot;
        Directory.CreateDirectory(root);
        var portListener = new TcpListener(IPAddress.Loopback, 0);
        portListener.Start();
        var port = ((IPEndPoint)portListener.LocalEndpoint).Port;
        portListener.Stop();
        const string secret = "integration-only-secret";
        var store = new SettingsStore(root);
        foreach (var kind in Enum.GetValues<CoreKind>())
        {
            var name = AppSettings.WireKind(kind);
            var directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            var executable = Path.Combine(directory, name + ".exe");
            File.Copy(Path.Combine(sources, name, name + ".exe"), executable);
            var config = Path.Combine(directory, kind == CoreKind.Mihomo ? "config.yaml" : "config.json");
            await File.WriteAllTextAsync(config, kind == CoreKind.Mihomo
                ? $"mixed-port: 0\nexternal-controller: 127.0.0.1:{port}\nsecret: {secret}\nmode: rule\nlog-level: info\nproxies: []\nproxy-groups: []\nrules: ['MATCH,DIRECT']\n"
                : System.Text.Json.JsonSerializer.Serialize(new
                {
                    log = new { level = "info" }, inbounds = Array.Empty<object>(),
                    outbounds = new[] { new { type = "direct", tag = "direct" } },
                    route = new { final = "direct" },
                    experimental = new { clash_api = new { external_controller = $"127.0.0.1:{port}", secret, default_mode = "rule" } }
                }));
            await store.UpdateProfileAsync(kind, 0, new() { ExePath = executable, ConfigPath = config, ApiUrl = $"http://127.0.0.1:{port}", Secret = new() { Action = "replace", Value = kind == CoreKind.Mihomo ? "incorrect-first" : secret } });
        }
        using var process = new CoreProcessManager();
        using var controller = new CoreLifecycleController(store, process, () => true);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        async Task Ready()
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (controller.ApiStatus != "ready")
            {
                if (!process.IsRunning || DateTime.UtcNow > deadline) throw new InvalidOperationException($"Core not ready: {controller.ApiStatus}\n{process.GetLogTail(8000)}");
                await Task.Delay(100);
            }
        }
        async Task ValidateApi()
        {
            using var unauthorized = await client.GetAsync(store.Current.ActiveProfile.ApiUrl + "/version");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            foreach (var route in new[] { "/version", "/configs", "/proxies", "/rules", "/connections" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, store.Current.ActiveProfile.ApiUrl + route);
                request.Headers.Authorization = new("Bearer", secret);
                using var response = await client.SendAsync(request);
                Assert.True(response.IsSuccessStatusCode, $"{controller.Kind} {route}: {response.StatusCode}");
            }
            using var socket = new System.Net.WebSockets.ClientWebSocket();
            using var streamBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/connections?token={secret}"), streamBudget.Token);
            var bytes = new byte[65536];
            var message = await socket.ReceiveAsync(bytes.AsMemory(), streamBudget.Token);
            Assert.Equal(System.Net.WebSockets.WebSocketMessageType.Text, message.MessageType);
            Assert.Contains("connections", System.Text.Encoding.UTF8.GetString(bytes, 0, message.Count));
            // Some Clash stream implementations do not read/respond to close frames.
            // Send our close, then dispose the test socket rather than waiting for a peer handshake.
            await socket.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "test complete", streamBudget.Token);
        }
        try
        {
            var start = await controller.ExecuteAsync(new() { Type = "start", CoreType = CoreKind.Mihomo });
            Assert.Equal("completed", start.Status);
            var firstProfile = store.Current.Profiles.Mihomo;
            await controller.ExecuteAsync(new()
            {
                Type = "saveProfile", CoreType = CoreKind.Mihomo, ExpectedRevision = firstProfile.Revision,
                Draft = new() { ExePath = firstProfile.ExePath, ConfigPath = firstProfile.ConfigPath, ApiUrl = firstProfile.ApiUrl, Secret = new() { Action = "replace", Value = secret } }
            });
            await Ready(); await ValidateApi();
            var targetProfile = store.Current.Profiles.SingBox;
            var broken = Path.Combine(root, "broken.exe");
            await File.WriteAllTextAsync(broken, "not an executable");
            await controller.ExecuteAsync(new()
            {
                Type = "saveProfile", CoreType = CoreKind.SingBox, ExpectedRevision = targetProfile.Revision,
                Draft = new() { ExePath = broken, ConfigPath = targetProfile.ConfigPath, ApiUrl = targetProfile.ApiUrl }
            });
            var failedSwitch = await controller.ExecuteAsync(new() { Type = "switchCore", CoreType = CoreKind.SingBox });
            Assert.Equal("failed", failedSwitch.Status);
            Assert.Equal(CoreKind.Mihomo, controller.Kind);
            await Ready(); await ValidateApi();
            await controller.ExecuteAsync(new()
            {
                Type = "saveProfile", CoreType = CoreKind.SingBox, ExpectedRevision = store.Current.Profiles.SingBox.Revision,
                Draft = new() { ExePath = targetProfile.ExePath, ConfigPath = targetProfile.ConfigPath, ApiUrl = targetProfile.ApiUrl }
            });
            var firstPid = process.ProcessId;
            var firstEpoch = controller.Epoch;
            var switched = await controller.ExecuteAsync(new() { Type = "switchCore", CoreType = CoreKind.SingBox });
            Assert.Equal("completed", switched.Status);
            await Ready(); await ValidateApi();
            Assert.NotEqual(firstPid, process.ProcessId);
            Assert.True(controller.Epoch > firstEpoch);
            Assert.Equal(CoreKind.SingBox, controller.Kind);
            var restart = await controller.ExecuteAsync(new() { Type = "restart", CoreType = CoreKind.SingBox });
            Assert.Equal("completed", restart.Status);
            await Ready(); await ValidateApi();
            var profile = store.Current.ActiveProfile;
            await controller.ExecuteAsync(new()
            {
                Type = "saveProfile", CoreType = CoreKind.SingBox, ExpectedRevision = profile.Revision,
                Draft = new() { ExePath = profile.ExePath, ConfigPath = profile.ConfigPath, ApiUrl = profile.ApiUrl, Secret = new() { Action = "replace", Value = "incorrect" } }
            });
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (controller.ApiStatus != "unauthorized" && DateTime.UtcNow < deadline) await Task.Delay(50);
            Assert.Equal("unauthorized", controller.ApiStatus);
            profile = store.Current.ActiveProfile;
            await controller.ExecuteAsync(new()
            {
                Type = "saveProfile", CoreType = CoreKind.SingBox, ExpectedRevision = profile.Revision,
                Draft = new() { ExePath = profile.ExePath, ConfigPath = profile.ConfigPath, ApiUrl = profile.ApiUrl, Secret = new() { Action = "replace", Value = secret } }
            });
            await Ready();
            Assert.Equal("completed", (await controller.ExecuteAsync(new() { Type = "completeSetup", CoreType = CoreKind.SingBox })).Status);
            Assert.True(store.Current.SetupCompleted);
            Assert.Equal("completed", (await controller.ExecuteAsync(new() { Type = "stop", CoreType = CoreKind.SingBox })).Status);
            Assert.False(process.IsRunning);
            Assert.Null(process.ProcessId);
        }
        finally
        {
            await controller.ShutdownAsync(TimeSpan.FromSeconds(10));
            CleanupTestDirectory();
        }
    }
}
