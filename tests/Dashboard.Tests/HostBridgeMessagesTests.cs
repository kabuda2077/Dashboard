using System.Text.Json;
using Dashboard;

namespace Dashboard.Tests;

public sealed class HostBridgeMessagesTests
{
    [Fact]
    public void RuntimeStateMessageKeepsCamelCaseWireShape()
    {
        var json = HostBridgeJson.Serialize(HostOutboundMessage.Runtime(new DashboardRuntimeState
        {
            IsRunning = true,
            ProcessId = 123,
            CoreTitle = "Mihomo Core",
            CoreVersion = "v1.2.3",
            CanUpgradeCore = true,
            IsCoreUpgrading = false,
            IsCoreSwitching = false,
            IsWindowMaximized = true
        }));

        using var document = JsonDocument.Parse(json);

        Assert.Equal("runtimeState", document.RootElement.GetProperty("type").GetString());
        var runtimeState = document.RootElement.GetProperty("runtimeState");
        Assert.True(runtimeState.GetProperty("isRunning").GetBoolean());
        Assert.Equal(123, runtimeState.GetProperty("processId").GetInt32());
        Assert.Equal("v1.2.3", runtimeState.GetProperty("coreVersion").GetString());
        Assert.True(runtimeState.GetProperty("isWindowMaximized").GetBoolean());
    }

    [Fact]
    public void LogAppendMessageUsesLogTextField()
    {
        var json = HostBridgeJson.Serialize(HostOutboundMessage.LogAppend("line\n"));

        using var document = JsonDocument.Parse(json);

        Assert.Equal("logAppend", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("line\n", document.RootElement.GetProperty("logText").GetString());
    }

    [Fact]
    public void StateMessageIncludesDashboardSettingsWithCamelCaseName()
    {
        var json = HostBridgeJson.Serialize(HostOutboundMessage.StateMessage(new DashboardState
        {
            IsRunning = false,
            ProcessId = null,
            CoreType = AppSettings.CoreTypeMihomo,
            CoreTitle = "Mihomo Core",
            CoreVersion = "",
            CorePath = "mihomo.exe",
            ConfigPath = "config.yaml",
            ApiUrl = "http://127.0.0.1:9090",
            Secret = "",
            MihomoCorePath = "mihomo.exe",
            MihomoConfigPath = "config.yaml",
            MihomoApiUrl = "http://127.0.0.1:9090",
            MihomoSecret = "",
            SingBoxCorePath = "sing-box.exe",
            SingBoxConfigPath = "config.json",
            SingBoxApiUrl = "http://127.0.0.1:9090",
            SingBoxSecret = "",
            SetupCompleted = true,
            ReadOnlyTunEnabled = null,
            StartCoreOnLaunch = false,
            MinimizeToTray = true,
            LightweightMode = true,
            Autostart = false,
            CanUpgradeCore = true,
            IsCoreUpgrading = false,
            IsCoreSwitching = false,
            IsWindowMaximized = false,
            LogText = "",
            IconCacheMap = new Dictionary<string, string>(),
            DashboardSettings = new Dictionary<string, string>
            {
                ["config/default-theme"] = "\"light\""
            }
        }));

        using var document = JsonDocument.Parse(json);

        var dashboardSettings = document.RootElement
            .GetProperty("state")
            .GetProperty("dashboardSettings");
        Assert.Equal("\"light\"", dashboardSettings.GetProperty("config/default-theme").GetString());
    }
}
