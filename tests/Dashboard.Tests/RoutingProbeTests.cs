using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard.Tests;

public sealed class RoutingProbeTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData("")]
    [InlineData(",\"coreType\":\"mihomo\"")]
    [InlineData(",\"expectedRuntimeEpoch\":1")]
    [InlineData(",\"coreType\":\"mihomo\",\"expectedRuntimeEpoch\":-1")]
    [InlineData(",\"coreType\":\"mihomo\",\"expectedRuntimeEpoch\":1,\"draft\":{}")]
    [InlineData(",\"coreType\":\"mihomo\",\"expectedRuntimeEpoch\":1,\"option\":\"url\"")]
    [InlineData(",\"coreType\":\"mihomo\",\"expectedRuntimeEpoch\":1,\"url\":\"https://example.com\"")]
    [InlineData(",\"coreType\":\"mihomo\",\"expectedRuntimeEpoch\":1,\"secret\":\"bad\"")]
    public async Task MalformedProbeCannotExecuteOrDiscloseState(string fields)
    {
        var called = false;
        var router = new HostMessageRouter(new()
        {
            ExecuteAsync = _ => { called = true; return Task.FromResult(CommandResult.Completed()); },
            BuildState = () => { called = true; return new(); }, Preferences = () => new Dictionary<string, string>(),
            ChooseFile = (_, _) => null, OpenLocation = (_, _) => { }, WindowCommand = (_, _) => { },
            CheckAppUpdateAsync = () => Task.CompletedTask, OpenAppRelease = () => { }, OpenCoreRepository = () => { }
        });
        var error = await Record.ExceptionAsync(() => router.RouteAsync("{\"protocolVersion\":2,\"type\":\"testRouting\",\"requestId\":\"probe\"" + fields + "}", _ => called = true));
        Assert.True(error is ArgumentException or JsonException);
        Assert.False(called);
    }

    [Theory]
    [InlineData("Socks5", "leaf", false, true)]
    [InlineData("Selector", "leaf", false, false)]
    [InlineData("URLTest", "leaf", false, false)]
    [InlineData("Fallback", "leaf", false, false)]
    [InlineData("Reject", "leaf", false, false)]
    [InlineData("Unknown", "leaf", false, false)]
    [InlineData("LeafWithAll", "leaf", false, false)]
    [InlineData("Direct", "leaf", false, true)]
    [InlineData("Socks5", "leaf", true, false)]
    [InlineData("Socks5", "old", false, false)]
    public async Task EvidenceRequiresFreshTupleHostAndLeafType(string type, string id, bool conflictingHost, bool matched)
    {
        using var handler = new ProxyMapHandler(type);
        using var api = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1/") };
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(new { connections = new[] { new {
            id, metadata = new { sourceIP = "127.0.0.1", sourcePort = "12345", destinationPort = "443", host = "localhost", sniffHost = conflictingHost ? "other.test" : "localhost" }, chains = new[] { "leaf" }
        } } }));
        var result = await RoutingProbe.MatchAsync(api, snapshot, new(IPAddress.Loopback, 12345), new("test", "localhost", "/"), new() { "old" }, default);
        Assert.Equal(matched, result is not null);
        if (result is not null) Assert.Equal(type == "Direct", result.Direct);
    }

    private sealed class ProxyMapHandler(string type) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { proxies = new Dictionary<string, object> { ["leaf"] = type == "LeafWithAll" ? new { type = "Http", all = new[] { "other" } } : new { type } } })) });
    }

    [Theory]
    [InlineData("single", true)]
    [InlineData("pinned", true)]
    [InlineData("duplicateUnqualified", false)]
    [InlineData("duplicatePinned", true)]
    [InlineData("conflictingPinned", true)]
    [InlineData("conflictingUnqualified", false)]
    [InlineData("duplicateWithinProvider", false)]
    [InlineData("duplicateProviderName", false)]
    [InlineData("missingProvider", false)]
    [InlineData("ownerMismatch", false)]
    [InlineData("unknownType", false)]
    [InlineData("groupOnly", false)]
    [InlineData("invalidProvider", false)]
    [InlineData("mapConflict", false)]
    [InlineData("mapLeaf", true)]
    [InlineData("mapGroup", false)]
    [InlineData("mapNameMismatch", false)]
    [InlineData("malformedProviderChain", false)]
    public async Task ProviderMetadataRequiresUnambiguousKnownLeafAndConsistentIdentity(string scenario, bool matched)
    {
        var leaf = new Dictionary<string, object> { ["name"] = "leaf", ["type"] = scenario == "unknownType" ? "FutureProtocol" : "Http" };
        if (scenario == "groupOnly") leaf["all"] = new[] { "other" };
        if (scenario == "ownerMismatch") leaf["provider-name"] = "other";
        var providers = new Dictionary<string, object> { ["one"] = new { proxies = scenario == "invalidProvider" ? null : scenario == "duplicateWithinProvider" ? new[] { leaf, leaf } : new[] { leaf } } };
        if (scenario is "duplicateUnqualified" or "duplicatePinned" or "conflictingPinned" or "conflictingUnqualified")
            providers["two"] = new { proxies = new[] { new { name = "leaf", type = scenario is "conflictingPinned" or "conflictingUnqualified" ? "Socks5" : "HTTP" } } };
        var map = new Dictionary<string, object>();
        if (scenario.StartsWith("map", StringComparison.Ordinal))
            map["leaf"] = scenario == "mapGroup" ? new { type = "Selector", all = new[] { "leaf" } }
                : new { name = scenario == "mapNameMismatch" ? "other" : "leaf", type = scenario == "mapConflict" ? "Socks5" : "Http" };
        var providerJson = scenario == "duplicateProviderName"
            ? "{\"providers\":{\"one\":{\"proxies\":[{\"name\":\"leaf\",\"type\":\"Http\"}]},\"one\":{\"proxies\":[]}}}"
            : JsonSerializer.Serialize(new { providers });
        using var handler = new MetadataHandler(JsonSerializer.Serialize(new { proxies = map }), providerJson);
        using var api = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1/") };
        var provider = scenario is "single" or "duplicateUnqualified" or "conflictingUnqualified" or "mapLeaf" or "mapGroup" or "mapNameMismatch" ? "" : scenario == "missingProvider" ? "absent" : "one";
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(new { connections = new[] { new {
            id = "fresh", metadata = new { sourceIP = "127.0.0.1", sourcePort = "12345", destinationPort = "443", host = "localhost" },
            chains = new[] { "leaf" }, providerChains = scenario == "malformedProviderChain" ? Array.Empty<string>() : new[] { provider }
        } } }));
        var result = await RoutingProbe.MatchAsync(api, snapshot, new(IPAddress.Loopback, 12345), new("test", "localhost", "/"), [], default);
        Assert.Equal(matched, result is not null);
        if (result is not null) { Assert.Equal("leaf", result.Node); Assert.False(result.Direct); }
        Assert.Equal(scenario == "malformedProviderChain" ? Array.Empty<string>()
            : scenario is "mapLeaf" or "mapGroup" or "mapNameMismatch" ? new[] { "/proxies" }
            : new[] { "/proxies", "/providers/proxies" }, handler.Paths);
    }

    private sealed class MetadataHandler(string proxies, string providers) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(path switch { "/proxies" => proxies, "/providers/proxies" => providers, _ => throw new InvalidOperationException(path) })
            });
        }
    }

    [Theory]
    [InlineData("stable", "matched")]
    [InlineData("providerChanged", "unconfirmed")]
    [InlineData("chainChanged", "unconfirmed")]
    [InlineData("idChanged", "unconfirmed")]
    [InlineData("providerUnavailable", "unconfirmed")]
    [InlineData("providerCancelled", "cancelled")]
    public async Task CompletedTlsRequiresStableConnectionAndProviderEvidence(string scenario, string status)
    {
        await using var origin = new OwnedOrigin();
        await using var relay = new OwnedHttpRelay(origin.Port);
        using var lifetime = new CancellationTokenSource();
        using var api = new HttpClient(new ChangingEvidenceHandler(relay, origin.Port, scenario, lifetime)) { BaseAddress = new("http://127.0.0.1/") };
        var target = new RoutingProbeTarget("test", "localhost", "/", origin.Port);
        var probe = new RoutingProbe([target], host => new() {
            TargetHost = host, CertificateChainPolicy = new() {
                TrustMode = X509ChainTrustMode.CustomRootTrust, CustomTrustStore = { origin.Certificate }, RevocationMode = X509RevocationMode.NoCheck
            }
        });
        var operation = probe.ProbeAsync(api, new(IPAddress.Loopback, relay.Port, false, false), target, Environment.ProcessId, lifetime.Token);
        if (status == "cancelled") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        else
        {
            var result = await operation;
            Assert.Equal(status, result.Status);
            if (status == "matched") { Assert.Equal("leaf", result.Node); Assert.False(result.Direct); }
            else { Assert.Null(result.Node); Assert.Null(result.Direct); Assert.Equal("connectionNotConfirmed", result.Reason); }
        }
        Assert.Single(origin.Requests);
    }

    private sealed class ChangingEvidenceHandler(OwnedHttpRelay relay, int port, string scenario, CancellationTokenSource lifetime) : HttpMessageHandler
    {
        private int _snapshots;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/providers/proxies" && scenario == "providerCancelled") { lifetime.Cancel(); throw new OperationCanceledException(token); }
            if (path == "/providers/proxies" && scenario == "providerUnavailable") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            object body;
            if (path == "/connections")
            {
                var round = _snapshots++;
                body = new { connections = round == 0 ? Array.Empty<object>() : new object[] { new {
                    id = round > 1 && scenario == "idChanged" ? "other" : "fresh",
                    metadata = new { sourceIP = "127.0.0.1", sourcePort = relay.Sources.Single().Port.ToString(), destinationPort = port.ToString(), host = "localhost" },
                    chains = new[] { "leaf", round > 1 && scenario == "chainChanged" ? "other-group" : "group" },
                    providerChains = new[] { round > 1 && scenario == "providerChanged" ? "two" : "one", "" }
                } } };
            }
            else if (path == "/proxies") body = new { proxies = new { group = new { type = "Selector", all = new[] { "leaf" }, now = "leaf" } } };
            else
            {
                Assert.Equal("/providers/proxies", path);
                body = new { providers = new { one = new { proxies = new[] { new { name = "leaf", type = "Http" } } }, two = new { proxies = new[] { new { name = "leaf", type = "Http" } } } } };
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) });
        }
    }

    [Theory]
    [InlineData("http")]
    [InlineData("json")]
    [InlineData("timeout")]
    [InlineData("cancelled")]
    public async Task CompletedHttpsWithUnavailableObservationIsUnconfirmedUnlessLifecycleCancelled(string failure)
    {
        await using var origin = new OwnedOrigin();
        await using var relay = new OwnedHttpRelay(origin.Port);
        using var lifetime = new CancellationTokenSource();
        using var api = new HttpClient(new ObservationFailureHandler(failure, lifetime)) { BaseAddress = new("http://127.0.0.1/") };
        var target = new RoutingProbeTarget("test", "localhost", "/", origin.Port);
        var probe = new RoutingProbe([target], host => new() {
            TargetHost = host, CertificateChainPolicy = new() {
                TrustMode = X509ChainTrustMode.CustomRootTrust, CustomTrustStore = { origin.Certificate }, RevocationMode = X509RevocationMode.NoCheck
            }
        });
        var operation = probe.ProbeAsync(api, new(IPAddress.Loopback, relay.Port, false, false), target, Environment.ProcessId, lifetime.Token);
        if (failure == "cancelled") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        else
        {
            var result = await operation;
            Assert.Equal("unconfirmed", result.Status);
            Assert.Equal("connectionNotConfirmed", result.Reason);
            Assert.Null(result.Node); Assert.Null(result.Direct);
        }
        Assert.Single(origin.Requests);
    }

    private sealed class ObservationFailureHandler(string failure, CancellationTokenSource lifetime) : HttpMessageHandler
    {
        private int _requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("/connections", request.RequestUri!.AbsolutePath);
            if (++_requests == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"connections\":[]}") });
            if (failure == "cancelled") lifetime.Cancel();
            if (failure is "timeout" or "cancelled") throw new OperationCanceledException(token);
            return Task.FromResult(new HttpResponseMessage(failure == "http" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) {
                Content = new StringContent("not-json")
            });
        }
    }

    [Fact]
    public void WindowsListenerOwnershipSupportsIpv6WithoutAdoptingOtherProcesses()
    {
        if (!Socket.OSSupportsIPv6) return;
        var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Assert.True(ManagedCoreListener.Owns(endpoint, Environment.ProcessId));
            Assert.False(ManagedCoreListener.Owns(endpoint, -1));
        }
        finally { listener.Stop(); }
    }

    [Theory]
    [InlineData("wrongSourceAddress")]
    [InlineData("wrongSourcePort")]
    [InlineData("wrongDestinationPort")]
    [InlineData("noHostEvidence")]
    [InlineData("duplicateTuple")]
    [InlineData("emptyChain")]
    public async Task IncompleteOrAmbiguousSnapshotCannotConfirm(string scenario)
    {
        using var api = new HttpClient(new ProxyMapHandler("Http")) { BaseAddress = new("http://127.0.0.1/" ) };
        var connection = new {
            id = "fresh", metadata = new {
                sourceIP = scenario == "wrongSourceAddress" ? "127.0.0.2" : "127.0.0.1",
                sourcePort = scenario == "wrongSourcePort" ? "54321" : "12345",
                destinationPort = scenario == "wrongDestinationPort" ? "80" : "443",
                host = scenario == "noHostEvidence" ? "" : "localhost"
            }, chains = scenario == "emptyChain" ? Array.Empty<string>() : new[] { "leaf" }
        };
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(new { connections = scenario == "duplicateTuple" ? new[] { connection, connection } : new[] { connection } }));
        Assert.Null(await RoutingProbe.MatchAsync(api, snapshot, new(IPAddress.Loopback, 12345), new("test", "localhost", "/"), [], default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"inbounds\":[]}")]
    [InlineData("{\"inbounds\":[{\"type\":\"tun\"}]}")]
    [InlineData("{\"inbounds\":[{\"type\":\"mixed\",\"listen_port\":1234,\"listen\":\"203.0.113.1\"}]}")]
    [InlineData("{\"inbounds\":[{\"type\":\"mixed\",\"listen_port\":1234,\"tls\":{\"enabled\":true}}]}")]
    [InlineData("{\"inbounds\":[{\"type\":\"mixed\",\"listen_port\":1234,\"detour\":\"other\"}]}")]
    [InlineData("{\"inbounds\":[{\"type\":\"mixed\",\"listen_port\":1234},{\"type\":\"http\",\"listen_port\":1235}]}")]
    public void UnsupportedOrAmbiguousLaunchInputHasNoRoutingCapability(string json) => Assert.Null(RoutingProbe.CaptureSingBoxInbound(json));

    [Theory]
    [InlineData("http", false)]
    [InlineData("mixed", false)]
    [InlineData("socks", true)]
    public void LaunchInputRetainsOnlyListenerCapabilityAndAuthRequirement(string type, bool socks)
    {
        var inbound = RoutingProbe.CaptureSingBoxInbound("// comments accepted by core\n" + JsonSerializer.Serialize(new {
            inbounds = new[] { new { type, listen = "127.0.0.1", listen_port = 1234, users = new[] { new { username = "private-user", password = "private-password" } } } },
            outbounds = new[] { new { type = "trojan", password = "outbound-secret" } }
        }));
        Assert.NotNull(inbound);
        Assert.Equal(new RoutingProbeInbound(IPAddress.Loopback, 1234, socks, true), inbound);
        Assert.DoesNotContain("private", inbound.ToString());
        Assert.DoesNotContain("outbound-secret", inbound.ToString());
    }

    [Theory]
    [InlineData(CoreKind.Mihomo, false)]
    [InlineData(CoreKind.Mihomo, true)]
    [InlineData(CoreKind.SingBox, false)]
    [InlineData(CoreKind.SingBox, true)]
    [Trait("Category", "RealCoreIntegration")]
    public Task ProductionHostUsesOwnedTlsAndRunningConfigAndCancelsStaleWork(CoreKind kind, bool leaf)
        => RunProductionHostAsync(kind, leaf, webView: false);

    [Theory]
    [InlineData(CoreKind.Mihomo)]
    [InlineData(CoreKind.SingBox)]
    [Trait("Category", "RealCoreIntegration")]
    public Task ProductionHostConfirmsProviderOnlyLeafAndSelectorFromRealMetadata(CoreKind kind)
        => RunProductionHostAsync(kind, leaf: true, webView: false, selector: true);

    [Fact]
    [Trait("Category", "WebViewCoreIntegration")]
    public Task ProductionWebViewRoutesSerializesAndCancelsOwnedRoutingCommand()
        => RunProductionHostAsync(CoreKind.Mihomo, leaf: true, webView: true);

    private async Task RunProductionHostAsync(CoreKind kind, bool leaf, bool webView, bool selector = false)
    {
        await using var origin = new OwnedOrigin();
        await using var relay = new OwnedHttpRelay(origin.Port);
        var root = TestRoot;
        Directory.CreateDirectory(root);
        var name = AppSettings.WireKind(kind);
        var sources = Environment.GetEnvironmentVariable("DASHBOARD_TEST_CORES_DIR") ?? throw new InvalidOperationException("Prepare validation cores first.");
        var exe = Path.Combine(root, name + ".exe");
        File.Copy(Path.Combine(sources, name, name + ".exe"), exe);
        var apiPort = IsolatedCore.FreePort(); var proxyPort = IsolatedCore.FreePort();
        var configPath = Path.Combine(root, kind == CoreKind.Mihomo ? "config.yaml" : "config.json");
        const string secret = "owned-api-only";
        var outbound = selector ? "observed-selector" : leaf ? "observed-leaf" : "DIRECT";
        var mihomoProxies = selector
            ? "proxies: []\nproxy-providers: {owned-provider: {type: file, path: ./provider.yaml}}\nproxy-groups: [{name: observed-selector, type: select, use: [owned-provider]}]"
            : $"proxies: [{{name: observed-leaf, type: http, server: 127.0.0.1, port: {relay.Port}}}]\nproxy-groups: []";
        var providerPath = Path.Combine(root, "provider.yaml");
        var providerConfig = $"proxies: [{{name: observed-leaf, type: http, server: 127.0.0.1, port: {relay.Port}}}]\n";
        if (selector && kind == CoreKind.Mihomo) await File.WriteAllTextAsync(providerPath, providerConfig);
        var config = kind == CoreKind.Mihomo
            ? $"{(leaf ? "port" : "socks-port")}: {proxyPort}\nexternal-controller: 127.0.0.1:{apiPort}\nsecret: {secret}\nmode: rule\nlog-level: info\nhosts: {{'localhost': 127.0.0.1}}\n{mihomoProxies}\nrules: ['MATCH,{outbound}']\n"
            : "// owned fixture with comments\n" + JsonSerializer.Serialize(new {
                log = new { level = "info" }, dns = new { servers = new[] { new { type = "hosts", tag = "local", predefined = new Dictionary<string, string[]> { ["localhost"] = ["127.0.0.1"] } } }, final = "local" },
                inbounds = new[] { new { type = leaf ? "mixed" : "socks", listen = "127.0.0.1", listen_port = proxyPort } },
                outbounds = new object[] { new { type = "direct", tag = "direct" }, new { type = "http", tag = "observed-leaf", server = "127.0.0.1", server_port = relay.Port } }
                    .Concat(selector ? [new { type = "selector", tag = "observed-selector", outbounds = new[] { "observed-leaf" } }] : Array.Empty<object>()).ToArray(),
                route = new { final = leaf ? outbound : "direct", default_domain_resolver = "local" },
                experimental = new { clash_api = new { external_controller = $"127.0.0.1:{apiPort}", secret } }
            });
        await File.WriteAllTextAsync(configPath, config);
        var store = new SettingsStore(root);
        await store.UpdateProfileAsync(kind, 0, new() { ExePath = exe, ConfigPath = configPath, ApiUrl = $"http://127.0.0.1:{apiPort}", Secret = new() { Action = "replace", Value = secret } });
        await store.SetActiveCoreAsync(kind);
        if (webView)
        {
            await store.CompleteSetupAsync();
            await store.SavePreferencesAsync(new Dictionary<string, string> {
                ["config/auto-ip-check"] = "false", ["config/auto-connection-check"] = "false"
            });
        }
        string[] ids = ["alibaba", "youtube", "google", "telegram", "cloudflare", "chatgpt", "bilibili", "github"];
        var probe = new RoutingProbe(ids.Select(id => new RoutingProbeTarget(id, "localhost", "/", origin.Port)).ToArray(), host => new() {
            TargetHost = host, CertificateChainPolicy = new() { TrustMode = X509ChainTrustMode.CustomRootTrust, CustomTrustStore = { origin.Certificate }, RevocationMode = X509RevocationMode.NoCheck }
        });
        using var process = new CoreProcessManager();
        using var host = new DashboardHost(root, ephemeralPort: true, isAdministrator: () => true, core: process, routingProbe: probe);
        async Task Ready()
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (host.BuildRuntimeState(false).ApiStatus != "ready")
            {
                Assert.True(host.IsRunning, process.GetLogTail(8000));
                await Task.Delay(50, budget.Token);
            }
        }
        HostRequest Request(long? epoch = null) => new() { Type = "testRouting", CoreType = kind, ExpectedRuntimeEpoch = epoch ?? host.RuntimeEpoch };
        try
        {
            Assert.Equal("apiNotReady", (await host.ExecuteAsync(Request())).Code);
            Assert.Equal("completed", (await host.ExecuteAsync(new() { Type = "start", CoreType = kind })).Status);
            await Ready();
            Assert.True(ManagedCoreListener.Owns(new(IPAddress.Loopback, proxyPort), process.ProcessId!.Value));
            Assert.False(ManagedCoreListener.Owns(new(IPAddress.Loopback, origin.Port), process.ProcessId.Value));
            if (!leaf)
            {
                // Ordinary platform trust must reject the fixture CA. The later
                // successful run trusts only that CA through the fixture seam.
                var untrusted = new RoutingProbe(ids.Select(id => new RoutingProbeTarget(id, "localhost", "/", origin.Port)).ToArray(),
                    targetHost => new() { TargetHost = targetHost });
                var session = new RoutingProbeSession(kind, host.Settings.Profile(kind), host.Settings.Profile(kind), host.RuntimeEpoch,
                    process.ProcessId.Value, kind == CoreKind.SingBox ? RoutingProbe.CaptureSingBoxInbound(config) : null);
                Assert.All(await untrusted.RunAsync(session, default), row => {
                    Assert.Equal("failed", row.Status); Assert.Equal("tlsValidationFailed", row.Reason);
                });
            }
            var settingsBefore = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"));
            using var evidenceApi = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new($"http://127.0.0.1:{apiPort}/") };
            evidenceApi.DefaultRequestHeaders.Authorization = new("Bearer", secret);
            var proxies = selector ? await evidenceApi.GetStringAsync("proxies") : "{}";
            var providers = selector && kind == CoreKind.Mihomo ? await evidenceApi.GetStringAsync("providers/proxies") : "{}";
            if (selector)
            {
                using var map = JsonDocument.Parse(proxies);
                Assert.Equal("Selector", map.RootElement.GetProperty("proxies").GetProperty("observed-selector").GetProperty("type").GetString());
                if (kind == CoreKind.Mihomo)
                {
                    Assert.False(map.RootElement.GetProperty("proxies").TryGetProperty("observed-leaf", out _));
                    using var providerMap = JsonDocument.Parse(providers);
                    var node = Assert.Single(providerMap.RootElement.GetProperty("providers").GetProperty("owned-provider").GetProperty("proxies").EnumerateArray());
                    Assert.Equal("observed-leaf", node.GetProperty("name").GetString());
                    Assert.Equal("Http", node.GetProperty("type").GetString());
                }
            }
            var operation = host.ExecuteAsync(Request());
            var observedConnections = "{}";
            if (selector)
            {
                while (!operation.IsCompleted)
                {
                    var snapshot = await evidenceApi.GetStringAsync("connections");
                    using var document = JsonDocument.Parse(snapshot);
                    if (document.RootElement.GetProperty("connections") is { ValueKind: JsonValueKind.Array } connections && connections.GetArrayLength() > 0) observedConnections = snapshot;
                    await Task.Delay(5);
                }
            }
            var completed = await operation;
            if (selector)
            {
                var report = Path.Combine(TestDirectory.ReportDirectory("routing-provider-evidence"), $"{name}-{completed.Routing!.First().Status}.json");
                using var proxyDocument = JsonDocument.Parse(proxies);
                using var providerDocument = JsonDocument.Parse(providers);
                using var observed = JsonDocument.Parse(observedConnections);
                await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { proxies = proxyDocument.RootElement, providers = providerDocument.RootElement,
                    connections = observed.RootElement, routing = completed.Routing }, new JsonSerializerOptions { WriteIndented = true }));
                var connections = observed.RootElement.GetProperty("connections").EnumerateArray();
                Assert.Contains(connections, c => c.GetProperty("chains").EnumerateArray().Select(v => v.GetString())
                    .SequenceEqual(new[] { "observed-leaf", "observed-selector" }));
                if (kind == CoreKind.Mihomo)
                {
                    Assert.All(connections, c => Assert.Equal(new[] { "owned-provider", "" }, c.GetProperty("providerChains").EnumerateArray().Select(v => v.GetString())));
                    Assert.Equal(providerConfig, await File.ReadAllTextAsync(providerPath));
                }
                else Assert.All(connections, c => Assert.False(c.TryGetProperty("providerChains", out _)));
            }
            Assert.Equal("routingTested", completed.Code);
            Assert.Equal(ids, completed.Routing!.Select(r => r.Id));
            Assert.All(completed.Routing!, row => { Assert.Equal("matched", row.Status); Assert.Equal(!leaf, row.Direct); Assert.Equal(leaf ? "observed-leaf" : null, row.Node); });
            if (leaf) Assert.True(relay.Requests.Count >= ids.Length);
            Assert.All(relay.Requests, request => { Assert.DoesNotContain(secret, request); Assert.DoesNotContain("Authorization:", request, StringComparison.OrdinalIgnoreCase); });
            Assert.Equal(settingsBefore, await File.ReadAllBytesAsync(Path.Combine(root, "settings.json")));
            Assert.Equal(config, await File.ReadAllTextAsync(configPath));
            Assert.All(origin.Requests, request => Assert.DoesNotContain(secret, request));
            Assert.Equal("staleRuntime", (await host.ExecuteAsync(Request(host.RuntimeEpoch - 1))).Code);

            // Saved path and original file can change after launch; neither is discovery input.
            var draftPath = Path.Combine(root, "edited.json"); await File.WriteAllTextAsync(draftPath, "{}");
            var profile = host.Settings.Profile(kind);
            await host.ExecuteAsync(new() { Type = "saveProfile", CoreType = kind, ExpectedRevision = profile.Revision,
                Draft = new() { ExePath = exe, ConfigPath = draftPath, ApiUrl = profile.ApiUrl } });
            await File.WriteAllTextAsync(configPath, "{}");
            Assert.All((await host.ExecuteAsync(Request())).Routing!, row => Assert.Equal("matched", row.Status));

            if (webView) await CheckWebViewBridgeAsync(host, origin);

            // Hold origin headers so an accepted operation stays live across refresh/exit.
            origin.Hold = true;
            var count = origin.Requests.Count;
            var pending = host.ExecuteAsync(Request());
            await origin.WaitForCountAsync(count + ids.Length);
            Assert.Equal("busy", (await host.ExecuteAsync(Request())).Code);
            await host.ExecuteAsync(new() { Type = "refreshCoreMetadata" });
            Assert.Equal("cancelled", (await pending.WaitAsync(TimeSpan.FromSeconds(3))).Status);
            await Ready();
            count = origin.Requests.Count;
            pending = host.ExecuteAsync(Request());
            await origin.WaitForCountAsync(count + ids.Length);
            if (leaf)
            {
                // Unexpected/externally observed owned-core exit must cancel too,
                // not merely host shutdown's tracker cancellation.
                await process.StopAsync();
                Assert.Equal("cancelled", (await pending.WaitAsync(TimeSpan.FromSeconds(3))).Status);
                Assert.True(await host.ShutdownAsync());
            }
            else
            {
                Assert.True(await host.ShutdownAsync());
                Assert.Equal("cancelled", (await pending).Status);
            }
            Assert.False(process.IsRunning);
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(root, "core-test.log"), process.GetLogTail(100000));
            Assert.True(await host.ShutdownAsync());
        }
    }

    [Theory]
    [InlineData(CoreKind.Mihomo, "none", "noProxyInbound")]
    [InlineData(CoreKind.Mihomo, "auth", "proxyAuthenticationRequired")]
    [InlineData(CoreKind.SingBox, "none", "noProxyInbound")]
    [InlineData(CoreKind.SingBox, "auth", "proxyAuthenticationRequired")]
    [InlineData(CoreKind.SingBox, "multiple", "noProxyInbound")]
    [Trait("Category", "RealCoreIntegration")]
    public async Task ProductionCoreRejectsUnsupportedAmbiguousAndAuthenticatedListeners(CoreKind kind, string mode, string reason)
    {
        await using var fixture = await IsolatedCore.CreateAsync(kind);
        var path = fixture.Store.Current.Profile(kind).ConfigPath;
        var port = IsolatedCore.FreePort();
        if (kind == CoreKind.Mihomo)
        {
            var config = await File.ReadAllTextAsync(path);
            config = config.Replace("mixed-port: 0", $"mixed-port: {(mode == "auth" ? port : 0)}");
            if (mode == "auth") config += "authentication: ['private-user:private-password']\n";
            await File.WriteAllTextAsync(path, config);
        }
        else
        {
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var api = config.RootElement.GetProperty("experimental").Clone();
            object[] inbounds = mode switch {
                "auth" => [new { type = "mixed", listen = "127.0.0.1", listen_port = port, users = new[] { new { username = "private-user", password = "private-password" } } }],
                "multiple" => [new { type = "http", listen = "127.0.0.1", listen_port = port }, new { type = "socks", listen = "127.0.0.1", listen_port = IsolatedCore.FreePort() }],
                _ => []
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new {
                inbounds, outbounds = new[] { new { type = "direct", tag = "direct" } }, route = new { final = "direct" }, experimental = api
            }));
        }
        Assert.Equal("completed", (await fixture.Controller.ExecuteAsync(new() { Type = "start", CoreType = kind })).Status);
        await fixture.ReadyAsync();
        Assert.Null(fixture.Controller.CaptureRoutingSession(kind, fixture.Controller.Epoch, out var session));
        var probe = new RoutingProbe();
        Assert.All(await probe.RunAsync(session!, default), row => {
            Assert.Equal("unavailable", row.Status); Assert.Equal(reason, row.Reason); Assert.Null(row.Node); Assert.Null(row.Direct);
        });
        // A local port controlled by anything except this managed PID is not an API capability.
        Assert.All(await probe.RunAsync(session! with { ProcessId = Environment.ProcessId }, default), row => {
            Assert.Equal("unavailable", row.Status); Assert.Equal("apiNotManaged", row.Reason);
        });
    }

    private static async Task CheckWebViewBridgeAsync(DashboardHost host, OwnedOrigin origin)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
            using var form = new MainForm(host, WebViewDataMaintenance.PlanForCurrentContent(host.DataDirectory,
                dashboardDirectory: Path.Combine(AppSettings.ResourceDirectory, "dashboard")));
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new(-15000, -15000);
            WebView2? View() => form.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<WebView2>()).SingleOrDefault();
            async Task<string> Script(string script) => View()?.CoreWebView2 is { } view ? await view.ExecuteScriptAsync(script) : "null";
            async Task Until(Func<Task<bool>> condition)
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                while (!await condition()) await Task.Delay(25, budget.Token);
            }
            async Task Send(string id)
            {
                await Script("window.__routingReply=null; window.__routingReceive ??= (e) => { if(e.data.requestId?.startsWith('webview-routing')) window.__routingReply=e.data; }; chrome.webview.removeEventListener('message',window.__routingReceive); chrome.webview.addEventListener('message',window.__routingReceive); chrome.webview.postMessage(" + HostBridgeJson.Serialize(new {
                    ProtocolVersion = 2, Type = "testRouting", RequestId = id, CoreType = CoreKind.Mihomo, ExpectedRuntimeEpoch = host.RuntimeEpoch
                }) + "); true");
            }
            form.Shown += async (_, _) =>
            {
                try
                {
                    await Until(async () => await Script("!!document.querySelector('#app')") == "true");
                    await Script("location.hash='#/overview'; true");
                    await Until(async () => await Script("document.querySelectorAll('[data-routing-target]').length === 8") == "true");
                    await Until(async () => await Script("!document.querySelector('[data-routing-target]').closest('section').querySelector('button').disabled") == "true");
                    await Script("document.querySelector('[data-routing-target]').closest('section').querySelector('button').click(); true");
                    await Until(async () => await Script("Array.from(document.querySelectorAll('[data-routing-target]')).every(el => el.innerText.includes('observed-leaf'))") == "true");
                    Assert.Equal("true", await Script("!document.querySelector('[data-routing-target]').closest('section').querySelector('button').disabled"));
                    origin.Hold = true;
                    var count = origin.Requests.Count;
                    await Send("webview-routing-cancelled");
                    await origin.WaitForCountAsync(count + 8);
                    await host.ExecuteAsync(new() { Type = "refreshCoreMetadata" });
                    await Until(async () => await Script("window.__routingReply?.result.status === 'cancelled'") == "true");
                    Assert.Equal("\"webview-routing-cancelled\"", await Script("window.__routingReply.requestId"));
                    completion.TrySetResult();
                }
                catch (Exception error) { completion.TrySetException(error); }
                finally
                {
                    origin.Hold = false;
                    try { await TestBrowser.DisposeAsync(form); }
                    catch (Exception error) { completion.TrySetException(error); }
                    form.CloseForApplicationExit();
                }
            };
            using var watchdog = new System.Windows.Forms.Timer { Interval = 60000 };
            watchdog.Tick += (_, _) => { completion.TrySetException(new TimeoutException("Routing WebView bridge deadline.")); form.CloseForApplicationExit(); };
            watchdog.Start(); Application.Run(form);
            }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(70)); }
        finally { Assert.True(thread.Join(TimeSpan.FromSeconds(30))); }
    }

    private sealed class OwnedHttpRelay : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<Task> _connections = [];
        private readonly Task _accept;
        private readonly int _originPort;
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Requests = new();
        public readonly System.Collections.Concurrent.ConcurrentQueue<IPEndPoint> Sources = new();
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public OwnedHttpRelay(int originPort) { _originPort = originPort; _listener.Start(); _accept = AcceptAsync(); }
        private async Task AcceptAsync()
        {
            try { while (true) _connections.Add(ServeAsync(await _listener.AcceptTcpClientAsync(_stop.Token))); }
            catch (OperationCanceledException) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            using (var upstream = new TcpClient())
            {
                try
                {
                    Sources.Enqueue((IPEndPoint)client.Client.RemoteEndPoint!);
                    var stream = client.GetStream();
                    var bytes = new byte[8192]; var count = 0;
                    while (count < bytes.Length)
                    {
                        await stream.ReadExactlyAsync(bytes.AsMemory(count++, 1), _stop.Token);
                        if (count >= 4 && Encoding.ASCII.GetString(bytes, count - 4, 4) == "\r\n\r\n") break;
                    }
                    var headers = Encoding.ASCII.GetString(bytes, 0, count);
                    Requests.Enqueue(headers);
                    Assert.True(headers.StartsWith($"CONNECT localhost:{_originPort} HTTP/1.1\r\n", StringComparison.Ordinal)
                        || headers.StartsWith($"CONNECT 127.0.0.1:{_originPort} HTTP/1.1\r\n", StringComparison.Ordinal), headers);
                    await upstream.ConnectAsync(IPAddress.Loopback, _originPort, _stop.Token);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"), _stop.Token);
                    using var connection = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    var send = stream.CopyToAsync(upstream.GetStream(), connection.Token);
                    var receive = upstream.GetStream().CopyToAsync(stream, connection.Token);
                    await Task.WhenAny(send, receive);
                    connection.Cancel();
                    try { await Task.WhenAll(send, receive); } catch (OperationCanceledException) { }
                }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException) { }
            }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel(); _listener.Stop(); await _accept; await Task.WhenAll(_connections); _stop.Dispose();
        }
    }

    private sealed class OwnedOrigin : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<Task> _connections = [];
        private readonly Task _accept;
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Requests = new();
        public bool Hold;
        public X509Certificate2 Certificate { get; }
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public OwnedOrigin()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
            // Schannel needs an imported persisted key for server credentials.
            Certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
            _listener.Start(); _accept = AcceptAsync();
        }
        private async Task AcceptAsync()
        {
            try { while (true) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _connections.Add(ServeAsync(client)); } }
            catch (OperationCanceledException) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            using (var tls = new SslStream(client.GetStream()))
            {
                try
                {
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Certificate }, _stop.Token);
                    var bytes = new byte[8192]; var count = 0;
                    while (count < bytes.Length)
                    {
                        await tls.ReadExactlyAsync(bytes.AsMemory(count++, 1), _stop.Token);
                        if (count >= 4 && Encoding.ASCII.GetString(bytes, count - 4, 4) == "\r\n\r\n") break;
                    }
                    Requests.Enqueue(Encoding.ASCII.GetString(bytes, 0, count));
                    if (!Hold) await tls.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 10000\r\nConnection: keep-alive\r\n\r\n"), _stop.Token);
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException) { }
            }
        }
        public async Task WaitForCountAsync(int count)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Requests.Count < count) await Task.Delay(20, budget.Token);
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel(); _listener.Stop(); await _accept; await Task.WhenAll(_connections);
            _stop.Dispose(); Certificate.Dispose();
        }
    }
}
