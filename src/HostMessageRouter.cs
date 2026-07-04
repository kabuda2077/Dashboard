using System.Text.Json;

namespace Dashboard;

internal sealed class HostMessageRouter
{
    private readonly HostMessageHandlers _handlers;

    public HostMessageRouter(HostMessageHandlers handlers)
    {
        _handlers = handlers;
    }

    public async Task RouteAsync(string messageJson)
    {
        using var document = JsonDocument.Parse(messageJson);
        var root = document.RootElement;
        var type = HostBridgeJson.GetString(root, "type", string.Empty);

        switch (type)
        {
            case HostBridgeCommand.WindowDrag:
                _handlers.WindowDrag();
                return;
            case HostBridgeCommand.WindowResize:
                _handlers.WindowResize(root);
                return;
            case HostBridgeCommand.WindowToggleMaximize:
                _handlers.WindowToggleMaximize();
                _handlers.SendWindowChromeState();
                return;
            case HostBridgeCommand.WindowMinimize:
                _handlers.WindowMinimize();
                return;
            case HostBridgeCommand.WindowClose:
                _handlers.WindowClose();
                return;
            case HostBridgeCommand.RequestWindowState:
                _handlers.SendWindowChromeState();
                return;
            case HostBridgeCommand.Performance:
            {
                var name = HostBridgeJson.GetString(root, "name", "unknown");
                var durationMs = HostBridgeJson.GetDouble(root, "durationMs", 0);
                HostOperationLogger.Info("performance", $"frontend:{name} durationMs={durationMs:0}");
                return;
            }
            case HostBridgeCommand.RequestState:
                break;
            case HostBridgeCommand.SaveDashboardSettings:
                _handlers.SaveDashboardSettings(root);
                return;
            case HostBridgeCommand.Save:
                _handlers.SaveSettings(root, true);
                break;
            case HostBridgeCommand.CompleteSetup:
                _handlers.SaveSettings(root, false);
                _handlers.CompleteSetup();
                await _handlers.ShowNoticeAsync("首次启动设置已完成。");
                break;
            case HostBridgeCommand.Start:
                _handlers.SaveSettings(root, false);
                _handlers.StartCore();
                break;
            case HostBridgeCommand.Restart:
                _handlers.SaveSettings(root, false);
                _handlers.RestartCore();
                break;
            case HostBridgeCommand.SwitchCore:
                _handlers.SaveSettings(root, false);
                await _handlers.SwitchCoreAsync(HostBridgeJson.GetString(root, "targetCoreType", string.Empty));
                return;
            case HostBridgeCommand.Stop:
                _handlers.StopCore();
                break;
            case HostBridgeCommand.UpgradeCore:
                _handlers.SaveSettings(root, false);
                await _handlers.UpgradeCoreAsync();
                break;
            case HostBridgeCommand.BrowseCore:
                _handlers.SaveSettings(root, false);
                _handlers.BrowseCorePath();
                break;
            case HostBridgeCommand.BrowseConfig:
                _handlers.SaveSettings(root, false);
                _handlers.BrowseConfigPath();
                break;
            case HostBridgeCommand.OpenCoreLocation:
                _handlers.SaveSettings(root, false);
                await _handlers.OpenCoreLocationAsync();
                break;
            case HostBridgeCommand.OpenConfigLocation:
                _handlers.SaveSettings(root, false);
                await _handlers.OpenConfigLocationAsync();
                break;
        }

        _handlers.SendState();
    }
}

internal sealed class HostMessageHandlers
{
    public required Action WindowDrag { get; init; }
    public required Action<JsonElement> WindowResize { get; init; }
    public required Action WindowToggleMaximize { get; init; }
    public required Action WindowMinimize { get; init; }
    public required Action WindowClose { get; init; }
    public required Action<JsonElement, bool> SaveSettings { get; init; }
    public required Action<JsonElement> SaveDashboardSettings { get; init; }
    public required Action CompleteSetup { get; init; }
    public required Action StartCore { get; init; }
    public required Action StopCore { get; init; }
    public required Action RestartCore { get; init; }
    public required Func<string, Task> SwitchCoreAsync { get; init; }
    public required Func<Task> UpgradeCoreAsync { get; init; }
    public required Action BrowseCorePath { get; init; }
    public required Action BrowseConfigPath { get; init; }
    public required Func<Task> OpenCoreLocationAsync { get; init; }
    public required Func<Task> OpenConfigLocationAsync { get; init; }
    public required Func<string, Task> ShowNoticeAsync { get; init; }
    public required Action SendState { get; init; }
    public required Action SendWindowChromeState { get; init; }
}
