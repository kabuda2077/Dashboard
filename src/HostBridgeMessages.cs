using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dashboard;

internal static class HostBridgeCommand
{
    public const string WindowDrag = "windowDrag";
    public const string WindowResize = "windowResize";
    public const string WindowToggleMaximize = "windowToggleMaximize";
    public const string WindowMinimize = "windowMinimize";
    public const string WindowClose = "windowClose";
    public const string RequestWindowState = "requestWindowState";
    public const string RequestState = "requestState";
    public const string Performance = "performance";
    public const string Save = "save";
    public const string CompleteSetup = "completeSetup";
    public const string Start = "start";
    public const string Restart = "restart";
    public const string SwitchCore = "switchCore";
    public const string Stop = "stop";
    public const string UpgradeCore = "upgradeCore";
    public const string BrowseCore = "browseCore";
    public const string BrowseConfig = "browseConfig";
    public const string OpenCoreLocation = "openCoreLocation";
    public const string OpenConfigLocation = "openConfigLocation";
    public const string SaveDashboardSettings = "saveDashboardSettings";
}

internal static class HostBridgeMessageType
{
    public const string State = "state";
    public const string RuntimeState = "runtimeState";
    public const string LogAppend = "logAppend";
    public const string IconCacheUpdated = "iconCacheUpdated";
    public const string Notice = "notice";
    public const string WindowState = "windowState";
}

internal static class HostBridgeJson
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(object message)
    {
        return JsonSerializer.Serialize(message, JsonOptions);
    }

    public static string GetString(JsonElement root, string propertyName, string fallback)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
    }

    public static bool GetBool(JsonElement root, string propertyName, bool fallback)
    {
        return root.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : fallback;
    }

    public static double GetDouble(JsonElement root, string propertyName, double fallback)
    {
        return root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out var value)
            ? value
            : fallback;
    }
}

internal sealed record HostOutboundMessage
{
    public required string Type { get; init; }
    public DashboardState? State { get; init; }
    public DashboardRuntimeState? RuntimeState { get; init; }
    public string? Message { get; init; }
    public string? LogText { get; init; }
    public IReadOnlyDictionary<string, string>? IconCacheMap { get; init; }
    public bool? IsMaximized { get; init; }

    public static HostOutboundMessage StateMessage(DashboardState state) => new()
    {
        Type = HostBridgeMessageType.State,
        State = state
    };

    public static HostOutboundMessage Notice(string message) => new()
    {
        Type = HostBridgeMessageType.Notice,
        Message = message
    };

    public static HostOutboundMessage WindowState(bool isMaximized) => new()
    {
        Type = HostBridgeMessageType.WindowState,
        IsMaximized = isMaximized
    };

    public static HostOutboundMessage Runtime(DashboardRuntimeState state) => new()
    {
        Type = HostBridgeMessageType.RuntimeState,
        RuntimeState = state
    };

    public static HostOutboundMessage LogAppend(string logText) => new()
    {
        Type = HostBridgeMessageType.LogAppend,
        LogText = logText
    };

    public static HostOutboundMessage IconCacheUpdated(IReadOnlyDictionary<string, string> iconCacheMap) => new()
    {
        Type = HostBridgeMessageType.IconCacheUpdated,
        IconCacheMap = iconCacheMap
    };
}

internal sealed record DashboardRuntimeState
{
    public required bool IsRunning { get; init; }
    public required int? ProcessId { get; init; }
    public required string CoreTitle { get; init; }
    public required string CoreVersion { get; init; }
    public required bool CanUpgradeCore { get; init; }
    public required bool IsCoreUpgrading { get; init; }
    public required bool IsCoreSwitching { get; init; }
    public required bool IsWindowMaximized { get; init; }
}

internal sealed record DashboardState
{
    public required bool IsRunning { get; init; }
    public required int? ProcessId { get; init; }
    public required string CoreType { get; init; }
    public required string CoreTitle { get; init; }
    public required string CoreVersion { get; init; }
    public required string CorePath { get; init; }
    public required string ConfigPath { get; init; }
    public required string ApiUrl { get; init; }
    public required string Secret { get; init; }
    public required string MihomoCorePath { get; init; }
    public required string MihomoConfigPath { get; init; }
    public required string MihomoApiUrl { get; init; }
    public required string MihomoSecret { get; init; }
    public required string SingBoxCorePath { get; init; }
    public required string SingBoxConfigPath { get; init; }
    public required string SingBoxApiUrl { get; init; }
    public required string SingBoxSecret { get; init; }
    public required bool SetupCompleted { get; init; }
    public required bool? ReadOnlyTunEnabled { get; init; }
    public required bool StartCoreOnLaunch { get; init; }
    public required bool MinimizeToTray { get; init; }
    public required bool LightweightMode { get; init; }
    public required bool Autostart { get; init; }
    public bool IsAutostartUpdating { get; init; }
    public required bool CanUpgradeCore { get; init; }
    public required bool IsCoreUpgrading { get; init; }
    public required bool IsCoreSwitching { get; init; }
    public required bool IsWindowMaximized { get; init; }
    public required string LogText { get; init; }
    public required IReadOnlyDictionary<string, string> IconCacheMap { get; init; }
    public IReadOnlyDictionary<string, string>? DashboardSettings { get; init; }
}
