using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dashboard;

internal static class HostBridgeJson
{
    public const int ProtocolVersion = 2;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<CoreKind>(allowIntegerValues: false) }
    };
    public static string Serialize(object message) => JsonSerializer.Serialize(message, JsonOptions);
}

internal sealed record HostRequest
{
    public int ProtocolVersion { get; init; }
    public required string Type { get; init; }
    public string? RequestId { get; init; }
    public CoreKind? CoreType { get; init; }
    public long? ExpectedRevision { get; init; }
    public long? ExpectedRuntimeEpoch { get; init; }
    public CoreProfileEdit? Draft { get; init; }
    public string? Option { get; init; }
    public bool? Value { get; init; }
    public IReadOnlyDictionary<string, string>? Preferences { get; init; }
    public string? Edge { get; init; }
    public string? Name { get; init; }
    public double? DurationMs { get; init; }
    public bool ConfirmUnverified { get; init; }
}

internal sealed record CommandResult(string Status, string Code, string? Message = null,
    bool Saved = false, string? Path = null)
{
    public static CommandResult Completed(string code = "completed", bool saved = false) => new("completed", code, Saved: saved);
    public static CommandResult Rejected(string code) => new("rejected", code);
    public static CommandResult Failed(string code, string? message = null, bool saved = false) => new("failed", code, message, saved);
}

internal sealed record HostReply(string RequestId, CommandResult Result, DashboardState State)
{
    public string Type => "commandResult";
    public int ProtocolVersion => HostBridgeJson.ProtocolVersion;
}

internal sealed record BootstrapReply(string RequestId, DashboardState State,
    IReadOnlyDictionary<string, string> Preferences)
{
    public string Type => "bootstrap";
    public int ProtocolVersion => HostBridgeJson.ProtocolVersion;
}

internal sealed record CoreProfileState(long Revision, string ExePath, string ConfigPath,
    string ApiUrl, string Secret, bool SecretDecryptionFailed);

internal record DashboardRuntimeState
{
    public bool IsRunning { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? ProcessId { get; init; }
    public CoreKind CoreType { get; init; }
    public long RuntimeEpoch { get; init; }
    public string CoreTitle { get; init; } = "";
    public string CoreVersion { get; init; } = "";
    public string ApiStatus { get; init; } = "idle";
    public string ApiUrl { get; init; } = "";
    public string Secret { get; init; } = "";
    public bool SecretDecryptionFailed { get; init; }
    public string Operation { get; init; } = "idle";
    public bool RequiresRestart { get; init; }
    public bool CanUpgradeCore { get; init; }
    public bool IsCoreUpgrading => Operation == "upgradeCore";
    public bool IsCoreSwitching => Operation == "switchCore";
    public bool IsWindowMaximized { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool? ReadOnlyTunEnabled { get; init; }
}

internal sealed record DashboardState
{
    public DashboardRuntimeState Runtime { get; init; } = new();
    public IReadOnlyDictionary<string, CoreProfileState> Profiles { get; init; } = new Dictionary<string, CoreProfileState>();
    public DesktopOptions DesktopOptions { get; init; } = new();
    public bool SetupCompleted { get; init; }
    public bool IsAutostartUpdating { get; init; }
    public string AppVersion { get; init; } = "";
    public string LatestAppVersion { get; init; } = "";
    public bool IsAppUpdateChecking { get; init; }
    public bool AppUpdateAvailable { get; init; }
    public string LatestCoreVersion { get; init; } = "";
    public bool IsCoreUpdateChecking { get; init; }
    public bool CoreUpdateAvailable { get; init; }
    public string LogText { get; init; } = "";
    public long DroppedLogEntries { get; init; }
    public long LogWriteFailures { get; init; }
    public IReadOnlyDictionary<string, string> IconCacheMap { get; init; } = new Dictionary<string, string>();
}

internal static class HostOutboundMessage
{
    public static object StateMessage(DashboardState state) => new { protocolVersion = 2, type = "state", state };
    public static object Runtime(DashboardRuntimeState runtimeState) => new { protocolVersion = 2, type = "runtimeState", runtimeState };
    public static object Notice(string message, string severity = "info", string code = "notice") => new { protocolVersion = 2, type = "notice", message, severity, code };
    public static object WindowState(bool isMaximized) => new { protocolVersion = 2, type = "windowState", isMaximized };
    public static object LogAppend(string logText) => new { protocolVersion = 2, type = "logAppend", logText };
    public static object IconCacheUpdated(IReadOnlyDictionary<string, string> iconCacheMap) => new { protocolVersion = 2, type = "iconCacheUpdated", iconCacheMap };
    public static object AppUpdateResult(string result, bool manual, string? currentVersion = null, string? latestVersion = null) =>
        new { protocolVersion = 2, type = "appUpdateResult", result, manual, currentVersion, latestVersion };
}

internal static class AppUpdateResultKind
{
    public const string Available = "available";
    public const string UpToDate = "upToDate";
    public const string Failed = "failed";
    public const string Busy = "busy";
}
