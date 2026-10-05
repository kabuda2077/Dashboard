using System.Text.Json;

namespace Dashboard.Tests;

public sealed class HostMessageRouterTests
{
    private static HostMessageRouter Router(List<string> calls, Func<HostRequest, Task<CommandResult>>? execute = null) => new(new()
    {
        ExecuteAsync = execute ?? (request => { calls.Add(request.Type); return Task.FromResult(CommandResult.Completed()); }),
        BuildState = () => new(), Preferences = () => new Dictionary<string, string>(),
        ChooseFile = (_, _) => { calls.Add("choose"); return "C:/chosen.exe"; },
        OpenLocation = (_, _) => calls.Add("location"),
        WindowCommand = (type, _) => calls.Add(type),
        CheckAppUpdateAsync = () => { calls.Add("check"); return Task.CompletedTask; },
        OpenAppRelease = () => calls.Add("release"), OpenCoreRepository = () => calls.Add("repository"),
        RequestElevation = () => calls.Add("elevation")
    });

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

    [Fact]
    public async Task FileSelectionIsNotAnImplicitSaveOrCoreOperation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Dashboard.Tests", Guid.NewGuid().ToString("N"));
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
        finally { Directory.Delete(directory, true); }
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
    [InlineData("{\"protocolVersion\":2,\"type\":\"upgradeCore\",\"coreType\":\"sing-box\",\"requestId\":\"u\",\"confirmUnverified\":true}")]
    [InlineData("{\"protocolVersion\":2,\"type\":\"upgradeCore\",\"coreType\":\"sing-box\",\"requestId\":\"u\",\"expectedRuntimeEpoch\":-1}")]
    public void UpgradeConfirmationRequiresBoundedContext(string json) => Assert.Throws<ArgumentException>(() => HostMessageRouter.Parse(json));

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
