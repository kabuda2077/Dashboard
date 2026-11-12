using System.Text.Json;

namespace Dashboard.Tests;

public sealed class HostMessageRouterTests : TemporaryDirectoryTest
{
    private static HostMessageRouter Router(List<string> calls, Func<HostRequest, Task<CommandResult>>? execute = null,
        Func<IReadOnlyDictionary<string, string>>? preferences = null) => new(new()
    {
        ExecuteAsync = execute ?? (request => { calls.Add(request.Type); return Task.FromResult(CommandResult.Completed()); }),
        BuildState = () => new(), Preferences = preferences ?? (() => new Dictionary<string, string>()),
        ChooseFile = (_, _) => { calls.Add("choose"); return "C:/chosen.exe"; },
        OpenLocation = (_, _) => calls.Add("location"),
        WindowCommand = (type, _) => calls.Add(type),
        CheckAppUpdateAsync = () => { calls.Add("check"); return Task.CompletedTask; },
        OpenAppRelease = () => calls.Add("release"), OpenCoreRepository = () => calls.Add("repository"),
        OpenCoreRelease = () => calls.Add("core-release"),
        RequestElevation = () => calls.Add("elevation")
    });

    [Fact]
    public async Task BootstrapWaitsForEveryPreviouslyAcceptedSaveButNotLaterSaves()
    {
        var writes = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var nextWrite = 0;
        var router = Router(new(), _ => writes[nextWrite++].Task);
        Task Save(string id) => router.RouteAsync(JsonSerializer.Serialize(new
        {
            protocolVersion = 2, type = "saveDashboardPreferences", requestId = id,
            preferences = new Dictionary<string, string> { ["config/theme"] = id }
        }), _ => { });
        var first = Save("first");
        var second = Save("second");
        var replies = new List<object>();
        var stages = new List<(string Name, double DurationMs)>();
        var bootstrap = router.RouteAsync("""
            {"protocolVersion":2,"type":"bootstrap","requestId":"reload"}
            """, replies.Add, (name, durationMs) => stages.Add((name, durationMs)));
        var later = Save("later");
        Assert.False(bootstrap.IsCompleted);
        Assert.Empty(replies);
        Assert.Equal("bootstrap:received", Assert.Single(stages).Name);
        writes[1].SetResult(CommandResult.Completed());
        await second;
        Assert.False(bootstrap.IsCompleted);
        writes[0].SetResult(CommandResult.Completed());
        await first;
        await bootstrap.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<BootstrapReply>(Assert.Single(replies));
        Assert.Equal(new[] { "bootstrap:received", "bootstrap:preferenceBarrier", "bootstrap:snapshot", "bootstrap:replied" },
            stages.Select(stage => stage.Name));
        Assert.All(stages, stage => Assert.True(double.IsFinite(stage.DurationMs) && stage.DurationMs >= 0));
        Assert.True(stages[^1].DurationMs >= stages[1].DurationMs);
        Assert.False(later.IsCompleted);
        writes[2].SetResult(CommandResult.Completed());
        await later;
    }

    [Fact]
    public async Task FailedSaveReleasesBootstrapAndDoesNotPoisonFutureSaves()
    {
        var write = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyDictionary<string, string> committed = new Dictionary<string, string> { ["config/theme"] = "old" };
        var router = Router(new(), _ => write.Task, () => committed);
        Task Save() => router.RouteAsync("""
            {"protocolVersion":2,"type":"saveDashboardPreferences","requestId":"save","preferences":{"config/theme":"new"}}
            """, _ => { });
        var save = Save();
        var replies = new List<object>();
        var bootstrap = router.RouteAsync("""
            {"protocolVersion":2,"type":"bootstrap","requestId":"reload"}
            """, replies.Add);
        Assert.False(bootstrap.IsCompleted);
        write.SetException(new IOException("Disk unavailable."));
        await save;
        await bootstrap.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(committed, Assert.IsType<BootstrapReply>(Assert.Single(replies)).Preferences);

        write = new(TaskCreationOptions.RunContinuationsAsynchronously);
        save = Save();
        replies.Clear();
        bootstrap = router.RouteAsync("""
            {"protocolVersion":2,"type":"bootstrap","requestId":"reload-again"}
            """, replies.Add);
        Assert.False(bootstrap.IsCompleted);
        committed = new Dictionary<string, string> { ["config/theme"] = "new" };
        write.SetResult(CommandResult.Completed());
        await save;
        await bootstrap.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(committed, Assert.IsType<BootstrapReply>(Assert.Single(replies)).Preferences);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"protocolVersion\":1,\"type\":\"requestState\",\"requestId\":\"x\"}")]
    [InlineData("{\"protocolVersion\":2,\"type\":\"unknown\",\"requestId\":\"x\"}")]
    [InlineData("{\"protocolVersion\":2,\"type\":\"start\",\"requestId\":\"x\",\"coreType\":\"other\"}")]
    [InlineData("{\"protocolVersion\":2,\"type\":\"start\",\"requestId\":\"x\",\"coreType\":1}")]
    [InlineData("{\"protocolVersion\":2,\"type\":\"requestState\",\"requestId\":\"x\",\"unknown\":true}")]
    [InlineData("{\"protocolVersion\":2,\"type\":\"stop\",\"type\":\"start\",\"requestId\":\"x\"}")]
    [InlineData("{\"protocolVersion\":2,\"type\":\"saveDashboardPreferences\",\"requestId\":\"x\",\"preferences\":{\"setup/a\":\"bad\"}}")]
    [InlineData("{\"protocolVersion\":2,\"type\":\"saveDashboardPreferences\",\"requestId\":\"x\",\"preferences\":{\"config/a\":\"1\",\"config/a\":\"2\"}}")]
    [InlineData("{\"protocolVersion\":2,\"type\":\"setDesktopOption\",\"requestId\":\"x\",\"option\":\"secret\",\"value\":true}")]
    public async Task InvalidOrOldProtocolHasNoSideEffectsOrStateDisclosure(string json)
    {
        var calls = new List<string>();
        var replies = new List<object>();
        var error = await Record.ExceptionAsync(() => Router(calls).RouteAsync(json, replies.Add));
        Assert.True(error is ArgumentException or JsonException, error?.ToString());
        Assert.Empty(calls);
        Assert.Empty(replies);
    }

    [Theory]
    [InlineData("{\"coreType\":\"mihomo\",\"expectedRuntimeEpoch\":1,\"iconUrls\":[\"file:///secret.png\"]}")]
    [InlineData("{\"coreType\":\"mihomo\",\"expectedRuntimeEpoch\":1,\"iconUrls\":[null]}")]
    [InlineData("{\"coreType\":\"mihomo\",\"iconUrls\":[]}")]
    [InlineData("{\"expectedRuntimeEpoch\":1,\"iconUrls\":[]}")]
    [InlineData("{\"coreType\":\"mihomo\",\"expectedRuntimeEpoch\":1,\"iconUrls\":[],\"preferences\":{}}")]
    public async Task InvalidIconCachePayloadHasNoSideEffects(string fields)
    {
        var calls = new List<string>();
        var replies = new List<object>();
        var json = "{\"protocolVersion\":2,\"type\":\"cacheProxyIcons\",\"requestId\":\"icons\"," + fields[1..];
        await Assert.ThrowsAsync<ArgumentException>(() => Router(calls).RouteAsync(json, replies.Add));
        Assert.Empty(calls);
        Assert.Empty(replies);
    }

    [Fact]
    public async Task IconCacheAcceptsBoundedWebUrlsForOneCoreSession()
    {
        var calls = new List<string>();
        var router = Router(calls);
        var command = new HostRequest { ProtocolVersion = 2, Type = "cacheProxyIcons", RequestId = "icons",
            CoreType = CoreKind.Mihomo, ExpectedRuntimeEpoch = 1, IconUrls = ["https://fixture.example/google.png"] };
        await router.RouteAsync(HostBridgeJson.Serialize(command), _ => { });
        Assert.Equal(["cacheProxyIcons"], calls);
        Assert.Throws<ArgumentException>(() => HostMessageRouter.Parse(HostBridgeJson.Serialize(command with
        { IconUrls = Enumerable.Repeat("https://fixture.example/icon.png", ProxyGroupIconCache.MaxCacheFiles + 1).ToArray() })));
        Assert.Throws<ArgumentException>(() => HostMessageRouter.Parse(HostBridgeJson.Serialize(command with { Type = "requestState" })));
    }

    [Theory]
    [InlineData("openCoreRepository", "repository")]
    [InlineData("openCoreRelease", "core-release")]
    public async Task CoreProjectAndReleaseLinksUseSeparateHandlers(string command, string expected)
    {
        var calls = new List<string>();
        var replies = new List<object>();
        await Router(calls).RouteAsync(JsonSerializer.Serialize(new
        {
            protocolVersion = 2, type = command, requestId = "core-link"
        }), replies.Add);
        Assert.Equal([expected], calls);
        Assert.Equal("completed", Assert.IsType<HostReply>(Assert.Single(replies)).Result.Status);
    }

    [Fact]
    public async Task FileSelectionIsNotAnImplicitSaveOrCoreOperation()
    {
        var directory = TestRoot;
        try
        {
            var store = new SettingsStore(directory);
            using var process = new CoreProcessManager();
            using var lifecycle = new CoreLifecycleController(store, process, () => true);
            var calls = new List<string>();
            var replies = new List<object>();
            await Router(calls, lifecycle.ExecuteAsync).RouteAsync("""
                {"protocolVersion":2,"type":"chooseCoreFile","coreType":"mihomo","requestId":"choose-1"}
                """, replies.Add);
            Assert.Equal(new[] { "choose" }, calls);
            Assert.Equal(0, store.Current.ActiveProfile.Revision);
            Assert.Equal("C:/chosen.exe", Assert.IsType<HostReply>(Assert.Single(replies)).Result.Path);
            var profile = store.Current.ActiveProfile;
            var result = await lifecycle.ExecuteAsync(new()
            {
                Type = "saveProfile", CoreType = CoreKind.Mihomo, ExpectedRevision = 0,
                Draft = new() { ExePath = profile.ExePath, ConfigPath = profile.ConfigPath, ApiUrl = profile.ApiUrl }
            });
            Assert.Equal("completed", result.Status);
            await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(2));
        }
        finally { CleanupTestDirectory(); }
    }

    [Fact]
    public async Task RequestAckWaitsForRealOperationResult()
    {
        var completion = new TaskCompletionSource<CommandResult>();
        var replies = new List<object>();
        var request = Router(new(), _ => completion.Task).RouteAsync("""
            {"protocolVersion":2,"type":"setDesktopOption","option":"lightweightMode","value":false,"requestId":"save-1"}
            """, replies.Add);
        Assert.Empty(replies);
        completion.SetResult(CommandResult.Failed("saveFailed"));
        await request;
        var reply = Assert.IsType<HostReply>(Assert.Single(replies));
        Assert.Equal("save-1", reply.RequestId);
        Assert.Equal("failed", reply.Result.Status);
    }

    [Fact]
    public async Task ElevationIsRequestedOnlyAfterTheCommandAcknowledgement()
    {
        var calls = new List<string>();
        await Router(calls, _ => Task.FromResult(new CommandResult("elevationRequired", "elevationRequired", Saved: true)))
            .RouteAsync("""{"protocolVersion":2,"type":"start","coreType":"mihomo","requestId":"start"}""", reply =>
            {
                Assert.True(Assert.IsType<HostReply>(reply).Result.Saved);
                calls.Add("reply");
            });
        Assert.Equal(["reply", "elevation"], calls);
    }

    [Theory]
    [InlineData("{\"protocolVersion\":2,\"type\":\"upgradeCore\",\"coreType\":\"mihomo\",\"requestId\":\"u\",\"expectedRevision\":-1}")]
    [InlineData("{\"protocolVersion\":2,\"type\":\"upgradeCore\",\"coreType\":\"sing-box\",\"requestId\":\"u\",\"expectedRuntimeEpoch\":-1}")]
    public void UpgradeRejectsNegativeContext(string json) => Assert.Throws<ArgumentException>(() => HostMessageRouter.Parse(json));

    [Fact]
    public async Task DiagnosticsAcceptOnlyBoundContextAndWhitelistedUniqueSubsets()
    {
        var routed = new List<HostRequest>();
        var router = Router(new(), request => { routed.Add(request); return Task.FromResult(CommandResult.Completed()); });
        await router.RouteAsync("""{"protocolVersion":2,"type":"testRouting","requestId":"one","coreType":"mihomo","expectedRuntimeEpoch":1,"targetIds":["github"]}""", _ => { });
        Assert.Equal(["github"], Assert.Single(routed).TargetIds);
        Assert.Null(HostMessageRouter.Parse("""{"protocolVersion":2,"type":"testRouting","requestId":"all","coreType":"mihomo","expectedRuntimeEpoch":1}""").TargetIds);
        foreach (var targets in new[] { "null", "[]", "[\"github\",\"github\"]", "[\"\"]", "[null]", "[\"Github\"]", "[\"http://localhost:1234\"]", "[\"github\",\"google\",\"telegram\",\"youtube\",\"alibaba\",\"bilibili\",\"cloudflare\",\"chatgpt\",\"github\"]" })
            Assert.Throws<ArgumentException>(() => HostMessageRouter.Parse("{\"protocolVersion\":2,\"type\":\"testRouting\",\"requestId\":\"bad\",\"coreType\":\"mihomo\",\"expectedRuntimeEpoch\":1,\"targetIds\":" + targets + "}"));
        foreach (var type in new[] { "getNetworkDiagnostics", "setNetworkDiagnostics" })
        {
            Assert.Throws<ArgumentException>(() => HostMessageRouter.Parse(HostBridgeJson.Serialize(new { protocolVersion = 2, type, requestId = "missing" })));
            Assert.Throws<ArgumentException>(() => HostMessageRouter.Parse(HostBridgeJson.Serialize(new { protocolVersion = 2, type, requestId = "url", coreType = CoreKind.Mihomo, expectedRuntimeEpoch = 1, option = "http://localhost:1234" })));
        }
        Assert.Equal("getNetworkDiagnostics", HostMessageRouter.Parse("""{"protocolVersion":2,"type":"getNetworkDiagnostics","requestId":"get","coreType":"mihomo","expectedRuntimeEpoch":1}""").Type);
    }

    [Fact]
    public void SnapshotParserRejectsInvalidRowsTimesAndRoutingSemantics()
    {
        var now = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var latency = new DiagnosticLatencySnapshot("tencent", [0, 1, 2, 3, 60000], now - 1000);
        var routing = new DiagnosticRoutingSnapshot("github", new("github", "matched", Node: "leaf", Direct: false), now);
        var snapshot = new NetworkDiagnosticsSnapshot("{\"route\":\"opaque\"}", [latency], [routing]);
        string Command(NetworkDiagnosticsSnapshot? value) => HostBridgeJson.Serialize(new {
            protocolVersion = 2, type = "setNetworkDiagnostics", requestId = "set", coreType = CoreKind.Mihomo, expectedRuntimeEpoch = 1, networkDiagnostics = value
        });
        Assert.False(HostMessageRouter.Parse(Command(snapshot)).NetworkDiagnostics!.Latency[0].Invalidated);
        var invalidated = HostMessageRouter.Parse(Command(snapshot with {
            Latency = [latency with { Invalidated = true }], Routing = [routing with { Invalidated = true }]
        })).NetworkDiagnostics!;
        Assert.True(invalidated.Latency[0].Invalidated);
        Assert.True(invalidated.Routing[0].Invalidated);
        NetworkDiagnosticsSnapshot?[] invalid = [
            null, snapshot with { PathKey = new string('x', 65537) }, snapshot with { Latency = null! },
            snapshot with { Latency = [latency, latency] }, snapshot with { Routing = Enumerable.Repeat(routing, 9).ToArray() },
            snapshot with { Latency = [latency with { Id = "alibaba" }] }, snapshot with { Routing = [routing with { Id = "tencent" }] },
            snapshot with { Latency = [latency with { Samples = [1, 2, 3, 4] }] },
            snapshot with { Latency = [latency with { Samples = [-1, 1, 2, 3, 4] }] },
            snapshot with { Latency = [latency with { Samples = [60001, 1, 2, 3, 4] }] },
            snapshot with { Latency = [latency with { TestedAt = now + 60000 }] },
            snapshot with { Routing = [routing with { TestedAt = now - TimeSpan.FromDays(31).TotalMilliseconds }] },
            snapshot with { Routing = [routing with { Result = routing.Result with { Id = "google" } }] },
            snapshot with { Routing = [routing with { Result = routing.Result with { Status = "observed" } }] },
            snapshot with { Routing = [routing with { Result = routing.Result with { Direct = true } }] },
            snapshot with { Routing = [routing with { Result = routing.Result with { Node = "bad\nnode" } }] },
            snapshot with { Routing = [routing with { Result = new("github", "failed", Node: "leaf", Reason: "probeFailed") }] },
            snapshot with { Routing = [routing with { Result = new("github", "failed", Reason: new string('x', 129)) }] }
        ];
        foreach (var value in invalid) Assert.Throws<ArgumentException>(() => HostMessageRouter.Parse(Command(value)));
        Assert.False(NetworkDiagnosticsSnapshots.IsValid(snapshot with { Latency = [latency with { Samples = [double.NaN, 1, 2, 3, 4] }] }));
        Assert.False(NetworkDiagnosticsSnapshots.IsValid(snapshot with { Routing = [routing with { TestedAt = double.PositiveInfinity }] }));
    }

    [Fact]
    public async Task LegitimateLargeCssPreferenceIsAccepted()
    {
        var calls = new List<string>();
        var json = JsonSerializer.Serialize(new { protocolVersion = 2, type = "saveDashboardPreferences", requestId = "css",
            preferences = new Dictionary<string, string> { ["config/custom-css"] = new string(' ', 128 * 1024) } });
        await Router(calls).RouteAsync(json, _ => { });
        Assert.Equal(new[] { "saveDashboardPreferences" }, calls);
    }
}
