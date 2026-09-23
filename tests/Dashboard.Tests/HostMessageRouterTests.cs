using System.Text.Json;

namespace Dashboard.Tests;

public sealed class HostMessageRouterTests
{
    private static HostMessageRouter CreateRouter(
        List<string> calls,
        bool failSave = false,
        Func<JsonElement, CoreConfigurationCommand, string, bool, Task<ConfigurationCommandResult>>? executeSettingsCommandAsync = null) => new(new HostMessageHandlers
    {
        RequestDashboardSettings = id => calls.Add("snapshot:" + id),
        WindowDrag = () => calls.Add("drag"),
        WindowResize = _ => calls.Add("resize"),
        WindowToggleMaximize = () => calls.Add("maximize"),
        WindowMinimize = () => calls.Add("minimize"),
        WindowClose = () => calls.Add("close"),
        SaveSettingsAsync = (_, _) => { calls.Add("save"); return Task.CompletedTask; },
        ExecuteSettingsCommandAsync = executeSettingsCommandAsync ?? ((_, command, target, completeSetup) =>
        {
            calls.Add("transaction:begin");
            calls.Add("save");
            if (completeSetup) calls.Add("completeSetup");
            calls.Add(command switch
            {
                CoreConfigurationCommand.Start => "start",
                CoreConfigurationCommand.Restart => "restart",
                CoreConfigurationCommand.Switch => "switch:" + target,
                CoreConfigurationCommand.Upgrade => "upgrade",
                _ => "commandOnly"
            });
            calls.Add("transaction:end");
            return Task.FromResult(ConfigurationCommandResult.Executed);
        }),
        ExecuteSettingsUiCommandAsync = (_, command) =>
        {
            calls.Add("save");
            calls.Add(command switch
            {
                HostSettingsUiCommand.BrowseCore => "browseCore",
                HostSettingsUiCommand.BrowseConfig => "browseConfig",
                HostSettingsUiCommand.OpenCoreLocation => "openCoreLocation",
                _ => "openConfigLocation"
            });
            return Task.CompletedTask;
        },
        SaveDashboardSettings = _ =>
        {
            calls.Add("dashboardSettings");
            if (failSave) throw new IOException("test write failure");
        },
        DashboardSettingsSaved = (id, success) => calls.Add($"ack:{id}:{success}"),
        StopCore = () => calls.Add("stop"),
        CheckAppUpdateAsync = () => { calls.Add("checkUpdate"); return Task.CompletedTask; },
        OpenAppRelease = () => calls.Add("release"),
        OpenCoreRepository = () => calls.Add("repository"),
        ShowNotice = _ => calls.Add("notice"),
        SendState = () => calls.Add("state"),
        SendWindowChromeState = () => calls.Add("windowState")
    });

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"type\":\"unknown\"}")]
    [InlineData("{\"type\":42}")]
    public async Task UnknownCommandsDoNotDiscloseState(string json)
    {
        var calls = new List<string>();
        await CreateRouter(calls).RouteAsync(json);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("{\"type\":\"requestDashboardSettings\"}")]
    [InlineData("{\"type\":\"requestDashboardSettings\",\"requestId\":42}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"type\":\"start\",\"mihomoSecret\":false}")]
    [InlineData("{\"type\":\"save\",\"replaceMihomoSecret\":\"true\"}")]
    [InlineData("{\"type\":\"save\",\"replaceSingBoxSecret\":1}")]
    [InlineData("{\"type\":\"save\",\"autostart\":\"true\"}")]
    [InlineData("{\"type\":\"start\",\"coreType\":\"other\"}")]
    [InlineData("{\"type\":\"switchCore\"}")]
    [InlineData("{\"type\":\"switchCore\",\"targetCoreType\":\"other\"}")]
    [InlineData("{\"type\":\"windowResize\",\"edge\":\"unknown\"}")]
    [InlineData("{\"type\":\"saveDashboardSettings\",\"settings\":[]}")]
    [InlineData("{\"type\":\"saveDashboardSettings\",\"settings\":{\"setup/api-list\":\"[]\"}}")]
    [InlineData("{\"type\":\"saveDashboardSettings\",\"settings\":{\"config/test\":true}}")]
    [InlineData("{\"type\":\"saveDashboardSettings\",\"settings\":{\"config/a\":\"1\",\"config/a\":\"2\"}}")]
    [InlineData("{\"type\":\"stop\",\"type\":\"start\"}")]
    [InlineData("{\"type\":\"performance\",\"name\":\"line\\nforged\"}")]
    [InlineData("{\"type\":\"performance\",\"name\":\"mount\",\"durationMs\":1e400}")]
    [InlineData("{\"type\":\"performance\",\"name\":\"mount\",\"durationMs\":-1}")]
    public async Task InvalidPayloadHasNoSideEffects(string json)
    {
        var calls = new List<string>();
        await Assert.ThrowsAsync<ArgumentException>(() => CreateRouter(calls).RouteAsync(json));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task MalformedJsonHasNoSideEffects()
    {
        var calls = new List<string>();
        await Assert.ThrowsAnyAsync<JsonException>(() => CreateRouter(calls).RouteAsync("{"));
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("{\"type\":\"requestDashboardSettings\",\"requestId\":\"doc-1\"}", "snapshot:doc-1")]
    [InlineData("{\"type\":\"requestState\"}", "state")]
    [InlineData("{\"type\":\"requestWindowState\"}", "windowState")]
    [InlineData("{\"type\":\"windowDrag\"}", "drag")]
    [InlineData("{\"type\":\"windowResize\",\"edge\":\"bottomRight\"}", "resize")]
    [InlineData("{\"type\":\"windowToggleMaximize\"}", "maximize,windowState")]
    [InlineData("{\"type\":\"windowMinimize\"}", "minimize")]
    [InlineData("{\"type\":\"windowClose\"}", "close")]
    [InlineData("{\"type\":\"save\",\"coreType\":\"mihomo\",\"autostart\":false}", "save,state")]
    [InlineData("{\"type\":\"start\",\"mihomoCorePath\":\"C:/mihomo.exe\"}", "transaction:begin,save,start,transaction:end,state")]
    [InlineData("{\"type\":\"stop\"}", "stop,state")]
    [InlineData("{\"type\":\"restart\"}", "transaction:begin,save,restart,transaction:end,state")]
    [InlineData("{\"type\":\"switchCore\",\"targetCoreType\":\"sing-box\"}", "transaction:begin,save,switch:sing-box,transaction:end")]
    [InlineData("{\"type\":\"upgradeCore\"}", "transaction:begin,save,upgrade,transaction:end,state")]
    [InlineData("{\"type\":\"browseCore\"}", "save,browseCore,state")]
    [InlineData("{\"type\":\"browseConfig\"}", "save,browseConfig,state")]
    [InlineData("{\"type\":\"openCoreLocation\"}", "save,openCoreLocation,state")]
    [InlineData("{\"type\":\"openConfigLocation\"}", "save,openConfigLocation,state")]
    [InlineData("{\"type\":\"completeSetup\"}", "transaction:begin,save,completeSetup,commandOnly,transaction:end,notice,state")]
    [InlineData("{\"type\":\"checkAppUpdate\"}", "checkUpdate,state")]
    [InlineData("{\"type\":\"openAppRelease\"}", "release")]
    [InlineData("{\"type\":\"openCoreRepository\"}", "repository")]
    public async Task ValidCommandsRetainTheirDispatchOrder(string json, string expected)
    {
        var calls = new List<string>();
        await CreateRouter(calls).RouteAsync(json);
        Assert.Equal(expected, string.Join(',', calls));
    }

    [Fact]
    public async Task HeldLifecycleGateRejectsCompleteSetupWithoutSavingOrSuccessNotice()
    {
        var calls = new List<string>();
        using var process = new CoreProcessManager();
        using var lifecycle = new CoreLifecycleController(new AppSettings(), process, new CoreLifecycleServices
        {
            IsRunningAsAdministrator = () => true,
            ShouldKeepMinimizedForRelaunch = () => false,
            RelaunchAsAdministrator = (_, _, _) => { },
            ShowNotice = _ => { },
            PublishState = () => { },
            RefreshIconCache = () => { },
            ShowTrayNotification = _ => { },
            ShowMessage = (_, _, _) => { },
            RunOnUiThread = action => action()
        });
        using var heldLease = lifecycle.TryEnterConfigurationChange();
        var saveCalled = false;
        var router = CreateRouter(
            calls,
            executeSettingsCommandAsync: (_, command, target, _) => lifecycle.ExecuteConfigurationCommandAsync(
                () =>
                {
                    saveCalled = true;
                    return Task.CompletedTask;
                },
                command,
                target));

        await router.RouteAsync("{\"type\":\"completeSetup\"}");

        Assert.False(saveCalled);
        Assert.DoesNotContain("notice", calls);
        Assert.Equal(new[] { "state" }, calls);
    }

    [Theory]
    [InlineData(false, "dashboardSettings,ack:save-1:True")]
    [InlineData(true, "dashboardSettings,ack:save-1:False")]
    public async Task AcknowledgesOnlyAfterPersistenceReturns(bool fail, string expected)
    {
        var calls = new List<string>();
        await CreateRouter(calls, fail).RouteAsync("""
            {"type":"saveDashboardSettings","requestId":"save-1","settings":{"config/theme":"light"}}
            """);
        Assert.Equal(expected, string.Join(',', calls));
    }

    [Fact]
    public async Task CustomCssIsNotRejectedByAnArbitrarySmallPayloadLimit()
    {
        var calls = new List<string>();
        var json = JsonSerializer.Serialize(new
        {
            type = "saveDashboardSettings",
            settings = new Dictionary<string, string> { ["config/custom-css"] = new string(' ', 128 * 1024) }
        });
        await CreateRouter(calls).RouteAsync(json);
        Assert.Equal(new[] { "dashboardSettings" }, calls);
    }
}
