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
        var output = Path.Combine(directory.FullName, ".tmp", "bridge-fixtures-v2");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "bootstrap.json"), json);
        File.WriteAllText(Path.Combine(output, "stopped.json"), HostBridgeJson.Serialize(HostOutboundMessage.Runtime(new() { CoreType = CoreKind.Mihomo, ProcessId = null, RuntimeEpoch = 2 })));
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
