using System.Text.Json;

namespace Dashboard.Tests;

public sealed class HostBridgeMessagesTests
{
    [Fact]
    public void BootstrapHasRequiredEmptyPreferencesAndExplicitNullableRuntimeFields()
    {
        var state = new DashboardState
        {
            Runtime = new() { CoreType = CoreKind.Mihomo, ProcessId = null, ApiUrl = "http://127.0.0.1:9090" },
            Profiles = Enum.GetValues<CoreKind>().ToDictionary(AppSettings.WireKind, kind =>
                new CoreProfileState(0, $"C:/Dashboard/{AppSettings.WireKind(kind)}.exe", "C:/Dashboard/config", "http://127.0.0.1:9090", "", false)),
            AppVersion = "2.0.0"
        };
        var json = HostBridgeJson.Serialize(new BootstrapReply("fixture-bootstrap", state, new Dictionary<string, string>()));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("preferences").ValueKind);
        Assert.Empty(root.GetProperty("preferences").EnumerateObject());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("state").GetProperty("runtime").GetProperty("processId").ValueKind);
        Assert.Equal("mihomo", root.GetProperty("state").GetProperty("runtime").GetProperty("coreType").GetString());
        // Frontend integration tests consume output of the production serializer, not hand-written JSON.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dashboard.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var output = TestDirectory.ReportDirectory("bridge-fixtures-v2");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "bootstrap.json"), json);
        File.WriteAllText(Path.Combine(output, "stopped.json"), HostBridgeJson.Serialize(HostOutboundMessage.Runtime(new() { CoreType = CoreKind.Mihomo, ProcessId = null, RuntimeEpoch = 2 })));
    }

    [Fact]
    public void RoutingReplyUsesProductionSerializerAndOmitsUnknownAndDirectNodeFields()
    {
        var state = new DashboardState {
            Profiles = Enum.GetValues<CoreKind>().ToDictionary(AppSettings.WireKind, kind =>
                new CoreProfileState(0, $"C:/Dashboard/{AppSettings.WireKind(kind)}.exe", "C:/Dashboard/config", "http://127.0.0.1:9090", "", false)),
            Runtime = new() {
                CoreType = CoreKind.Mihomo, IsRunning = true, ProcessId = 12345, RuntimeEpoch = 1,
                ApiStatus = "ready", ApiUrl = "http://127.0.0.1:9090"
            }
        };
        RoutingProbeResult[] rows = [
            new("alibaba", "matched", Direct: true),
            new("youtube", "matched", Node: "observed-leaf", Direct: false),
            new("google", "unconfirmed", Reason: "connectionNotConfirmed"),
            new("telegram", "unavailable", Reason: "noProxyInbound"),
            new("cloudflare", "failed", Reason: "tlsValidationFailed"),
            new("chatgpt", "matched", Node: "another-leaf", Direct: false),
            new("bilibili", "matched", Node: "observed-leaf", Direct: false),
            new("github", "unconfirmed", Reason: "connectionNotConfirmed")
        ];
        var json = HostBridgeJson.Serialize(new HostReply("fixture-routing", new("completed", "routingTested", Routing: rows), state));
        using var document = JsonDocument.Parse(json);
        var result = document.RootElement.GetProperty("result");
        Assert.Equal("routingTested", result.GetProperty("code").GetString());
        var routing = result.GetProperty("routing");
        Assert.Equal(8, routing.GetArrayLength());
        Assert.False(routing[0].TryGetProperty("node", out _));
        Assert.True(routing[0].GetProperty("direct").GetBoolean());
        Assert.Equal("observed-leaf", routing[1].GetProperty("node").GetString());
        Assert.False(routing[1].GetProperty("direct").GetBoolean());
        Assert.False(routing[2].TryGetProperty("direct", out _));
        var output = TestDirectory.ReportDirectory("bridge-fixtures-v2");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "routing.json"), json);
    }

    [Fact]
    public void IncrementalPayloadDoesNotCarryProfilesOrCredentialsFromOtherCore()
    {
        using var json = JsonDocument.Parse(HostBridgeJson.Serialize(HostOutboundMessage.LogAppend("line")));
        Assert.Equal("logAppend", json.RootElement.GetProperty("type").GetString());
        Assert.False(json.RootElement.TryGetProperty("state", out _));
        Assert.False(json.RootElement.TryGetProperty("profiles", out _));
    }
}
