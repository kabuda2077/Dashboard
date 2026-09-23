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

    [Theory]
    [InlineData(AppUpdateResultKind.Available, true, "1.2.0", "1.3.0")]
    [InlineData(AppUpdateResultKind.UpToDate, true, "1.2.0", "1.2.0")]
    [InlineData(AppUpdateResultKind.Failed, false, null, null)]
    [InlineData(AppUpdateResultKind.Busy, true, null, null)]
    public void AppUpdateResultMessageKeepsStructuredWireShape(
        string result,
        bool manual,
        string? currentVersion,
        string? latestVersion)
    {
        var json = HostBridgeJson.Serialize(HostOutboundMessage.AppUpdateResult(
            result,
            manual,
            currentVersion,
            latestVersion));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("appUpdateResult", root.GetProperty("type").GetString());
        Assert.Equal(result, root.GetProperty("result").GetString());
        Assert.Equal(manual, root.GetProperty("manual").GetBoolean());
        Assert.Equal(currentVersion, root.TryGetProperty("currentVersion", out var current)
            ? current.GetString()
            : null);
        Assert.Equal(latestVersion, root.TryGetProperty("latestVersion", out var latest)
            ? latest.GetString()
            : null);
        Assert.False(root.TryGetProperty("message", out _));
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
            AppVersion = "1.2.0",
            LatestAppVersion = "1.3.0",
            IsAppUpdateChecking = false,
            AppUpdateAvailable = true,
            LatestCoreVersion = "v1.19.30",
            IsCoreUpdateChecking = false,
            CoreUpdateAvailable = true,
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
        var state = document.RootElement.GetProperty("state");
        Assert.Equal("1.2.0", state.GetProperty("appVersion").GetString());
        Assert.Equal("1.3.0", state.GetProperty("latestAppVersion").GetString());
        Assert.True(state.GetProperty("appUpdateAvailable").GetBoolean());
        Assert.Equal("v1.19.30", state.GetProperty("latestCoreVersion").GetString());
        Assert.True(state.GetProperty("coreUpdateAvailable").GetBoolean());
    }
}
