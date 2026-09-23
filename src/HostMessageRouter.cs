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
        var type = ValidateCommand(root);
        if (type is null) return;

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
                HostOperationLogger.Diagnostic("performance", $"frontend:{name} durationMs={durationMs:0}");
                return;
            }
            case "requestDashboardSettings":
                _handlers.RequestDashboardSettings(HostBridgeJson.GetString(root, "requestId", ""));
                return;
            case HostBridgeCommand.RequestState:
                break;
            case HostBridgeCommand.SaveDashboardSettings:
            {
                var requestId = HostBridgeJson.GetString(root, "requestId", "");
                try
                {
                    _handlers.SaveDashboardSettings(root);
                }
                catch (Exception ex)
                {
                    if (requestId.Length == 0) throw;
                    HostOperationLogger.Error("settings", "Failed to persist dashboard settings.", ex);
                    _handlers.DashboardSettingsSaved(requestId, false);
                    return;
                }
                if (requestId.Length > 0) _handlers.DashboardSettingsSaved(requestId, true);
                return;
            }
            case HostBridgeCommand.Save:
                await _handlers.SaveSettingsAsync(root, true);
                break;
            case HostBridgeCommand.CompleteSetup:
                var result = await _handlers.ExecuteSettingsCommandAsync(
                    root, CoreConfigurationCommand.SaveOnly, string.Empty, true);
                if (result == ConfigurationCommandResult.Executed)
                    _handlers.ShowNotice("首次启动设置已完成。");
                break;
            case HostBridgeCommand.Start:
                await _handlers.ExecuteSettingsCommandAsync(
                    root, CoreConfigurationCommand.Start, string.Empty, false);
                break;
            case HostBridgeCommand.Restart:
                await _handlers.ExecuteSettingsCommandAsync(
                    root, CoreConfigurationCommand.Restart, string.Empty, false);
                break;
            case HostBridgeCommand.SwitchCore:
                await _handlers.ExecuteSettingsCommandAsync(
                    root,
                    CoreConfigurationCommand.Switch,
                    HostBridgeJson.GetString(root, "targetCoreType", string.Empty),
                    false);
                return;
            case HostBridgeCommand.Stop:
                _handlers.StopCore();
                break;
            case HostBridgeCommand.UpgradeCore:
                await _handlers.ExecuteSettingsCommandAsync(
                    root, CoreConfigurationCommand.Upgrade, string.Empty, false);
                break;
            case HostBridgeCommand.BrowseCore:
                await _handlers.ExecuteSettingsUiCommandAsync(root, HostSettingsUiCommand.BrowseCore);
                break;
            case HostBridgeCommand.BrowseConfig:
                await _handlers.ExecuteSettingsUiCommandAsync(root, HostSettingsUiCommand.BrowseConfig);
                break;
            case HostBridgeCommand.OpenCoreLocation:
                await _handlers.ExecuteSettingsUiCommandAsync(root, HostSettingsUiCommand.OpenCoreLocation);
                break;
            case HostBridgeCommand.OpenConfigLocation:
                await _handlers.ExecuteSettingsUiCommandAsync(root, HostSettingsUiCommand.OpenConfigLocation);
                break;
            case HostBridgeCommand.CheckAppUpdate:
                await _handlers.CheckAppUpdateAsync();
                break;
            case HostBridgeCommand.OpenAppRelease:
                _handlers.OpenAppRelease();
                return;
            case HostBridgeCommand.OpenCoreRepository:
                _handlers.OpenCoreRepository();
                return;
        }

        _handlers.SendState();
    }

    private static string? ValidateCommand(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("宿主命令必须是 JSON 对象。");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new ArgumentException("宿主命令包含重复字段。");
        }
        var type = HostBridgeJson.GetString(root, "type", "");
        if (!KnownCommands.Contains(type)) return null; // Unknown commands must not disclose state.

        foreach (var name in StringFields)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"命令字段 {name} 必须是字符串。");
        }
        foreach (var name in BooleanFields)
        {
            if (root.TryGetProperty(name, out var value)
                && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArgumentException($"命令字段 {name} 必须是布尔值。");
        }
        if (root.TryGetProperty("coreType", out var coreType)
            && coreType.GetString() is not (AppSettings.CoreTypeMihomo or AppSettings.CoreTypeSingBox))
            throw new ArgumentException("未知的内核类型。");

        switch (type)
        {
            case "requestDashboardSettings":
                var snapshotRequestId = HostBridgeJson.GetString(root, "requestId", "");
                if (string.IsNullOrWhiteSpace(snapshotRequestId) || snapshotRequestId.Length > 128)
                    throw new ArgumentException("无效的设置读取请求标识。");
                break;
            case HostBridgeCommand.WindowResize:
                if (HostBridgeJson.GetString(root, "edge", "") is not
                    ("left" or "right" or "top" or "bottom" or "topLeft" or "topRight" or "bottomLeft" or "bottomRight"))
                    throw new ArgumentException("无效的窗口缩放边缘。");
                break;
            case HostBridgeCommand.SwitchCore:
                if (HostBridgeJson.GetString(root, "targetCoreType", "") is not
                    (AppSettings.CoreTypeMihomo or AppSettings.CoreTypeSingBox))
                    throw new ArgumentException("必须指定有效的目标内核。");
                break;
            case HostBridgeCommand.SaveDashboardSettings:
                if (root.TryGetProperty("requestId", out var requestId)
                    && (requestId.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(requestId.GetString())
                        || requestId.GetString()!.Length > 128))
                    throw new ArgumentException("无效的保存请求标识。");
                if (!root.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("界面设置必须是 JSON 对象。");
                var settingKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var setting in settings.EnumerateObject())
                {
                    if (!setting.Name.StartsWith("config/", StringComparison.Ordinal)
                        || setting.Value.ValueKind != JsonValueKind.String || !settingKeys.Add(setting.Name))
                        throw new ArgumentException("界面设置只接受不重复的 config/ 字符串字段。");
                }
                break;
            case HostBridgeCommand.Performance:
                var name = HostBridgeJson.GetString(root, "name", "");
                if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl))
                    throw new ArgumentException("无效的诊断事件名称。");
                if (root.TryGetProperty("durationMs", out var duration)
                    && (duration.ValueKind != JsonValueKind.Number || !duration.TryGetDouble(out var ms)
                        || !double.IsFinite(ms) || ms < 0))
                    throw new ArgumentException("无效的诊断耗时。");
                break;
        }
        return type;
    }

    private static readonly HashSet<string> KnownCommands = new(StringComparer.Ordinal)
    {
        "requestDashboardSettings",
        HostBridgeCommand.WindowDrag, HostBridgeCommand.WindowResize, HostBridgeCommand.WindowToggleMaximize,
        HostBridgeCommand.WindowMinimize, HostBridgeCommand.WindowClose, HostBridgeCommand.RequestWindowState,
        HostBridgeCommand.RequestState, HostBridgeCommand.Performance, HostBridgeCommand.Save,
        HostBridgeCommand.CompleteSetup, HostBridgeCommand.Start, HostBridgeCommand.Restart,
        HostBridgeCommand.SwitchCore, HostBridgeCommand.Stop, HostBridgeCommand.UpgradeCore,
        HostBridgeCommand.BrowseCore, HostBridgeCommand.BrowseConfig, HostBridgeCommand.OpenCoreLocation,
        HostBridgeCommand.OpenConfigLocation, HostBridgeCommand.CheckAppUpdate, HostBridgeCommand.OpenAppRelease,
        HostBridgeCommand.OpenCoreRepository, HostBridgeCommand.SaveDashboardSettings
    };

    private static readonly string[] StringFields =
    [
        "coreType", "mihomoCorePath", "mihomoConfigPath", "mihomoApiUrl", "mihomoSecret",
        "singBoxCorePath", "singBoxConfigPath", "singBoxApiUrl", "singBoxSecret", "targetCoreType", "edge", "name"
    ];

    private static readonly string[] BooleanFields =
    [
        "setupCompleted", "startCoreOnLaunch", "minimizeToTray", "lightweightMode", "autostart",
        "replaceMihomoSecret", "replaceSingBoxSecret"
    ];
}

internal enum HostSettingsUiCommand
{
    BrowseCore,
    BrowseConfig,
    OpenCoreLocation,
    OpenConfigLocation
}

internal sealed class HostMessageHandlers
{
    public required Action WindowDrag { get; init; }
    public required Action<JsonElement> WindowResize { get; init; }
    public required Action WindowToggleMaximize { get; init; }
    public required Action WindowMinimize { get; init; }
    public required Action WindowClose { get; init; }
    public required Func<JsonElement, bool, Task> SaveSettingsAsync { get; init; }
    public required Func<JsonElement, CoreConfigurationCommand, string, bool, Task<ConfigurationCommandResult>> ExecuteSettingsCommandAsync { get; init; }
    public required Func<JsonElement, HostSettingsUiCommand, Task> ExecuteSettingsUiCommandAsync { get; init; }
    public Action<string> RequestDashboardSettings { get; init; } = _ => { };
    public Action<string, bool> DashboardSettingsSaved { get; init; } = (_, _) => { };
    public required Action<JsonElement> SaveDashboardSettings { get; init; }
    public required Action StopCore { get; init; }
    public required Func<Task> CheckAppUpdateAsync { get; init; }
    public required Action OpenAppRelease { get; init; }
    public required Action OpenCoreRepository { get; init; }
    public required Action<string> ShowNotice { get; init; }
    public required Action SendState { get; init; }
    public required Action SendWindowChromeState { get; init; }
}
