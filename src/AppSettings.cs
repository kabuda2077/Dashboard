using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dashboard;

[JsonConverter(typeof(JsonStringEnumConverter<CoreKind>))]
public enum CoreKind
{
    [JsonStringEnumMemberName("mihomo")] Mihomo,
    [JsonStringEnumMemberName("sing-box")] SingBox
}

public sealed record CoreProfile
{
    [JsonRequired] public long Revision { get; init; }
    [JsonRequired] public string ExePath { get; init; } = "";
    [JsonRequired] public string ConfigPath { get; init; } = "";
    [JsonRequired] public string ApiUrl { get; init; } = "http://127.0.0.1:9090";
    [JsonRequired] public string ProtectedSecret { get; init; } = "";
    [JsonIgnore] public string Secret { get; init; } = "";
    [JsonIgnore] public bool SecretDecryptionFailed { get; init; }
}

public sealed record CoreProfiles
{
    [JsonRequired] public CoreProfile Mihomo { get; init; } = new();
    [JsonRequired] public CoreProfile SingBox { get; init; } = new();
}

public sealed record DesktopOptions
{
    [JsonRequired] public bool StartCoreOnLaunch { get; init; }
    [JsonRequired] public bool MinimizeToTray { get; init; } = true;
    [JsonRequired] public bool LightweightMode { get; init; } = true;
    [JsonRequired] public bool Autostart { get; init; }
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 2;
    [JsonRequired] public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    [JsonRequired] public CoreKind ActiveCoreKind { get; init; }
    [JsonRequired] public CoreProfiles Profiles { get; init; } = new();
    [JsonRequired] public DesktopOptions DesktopOptions { get; init; } = new();
    [JsonRequired] public bool SetupCompleted { get; init; }
    [JsonRequired] public long PreferencesRevision { get; init; }
    [JsonRequired] public IReadOnlyDictionary<string, string> DashboardPreferences { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public CoreProfile Profile(CoreKind kind) => kind switch
    {
        CoreKind.Mihomo => Profiles.Mihomo,
        CoreKind.SingBox => Profiles.SingBox,
        _ => throw new ArgumentException("Unknown core kind.")
    };

    public AppSettings WithProfile(CoreKind kind, CoreProfile profile) => this with
    {
        Profiles = kind switch
        {
            CoreKind.Mihomo => Profiles with { Mihomo = profile },
            CoreKind.SingBox => Profiles with { SingBox = profile },
            _ => throw new ArgumentException("Unknown core kind.")
        }
    };

    [JsonIgnore] public CoreProfile ActiveProfile => Profile(ActiveCoreKind);
    [JsonIgnore] public bool IsSingBox => ActiveCoreKind == CoreKind.SingBox;
    public static string WireKind(CoreKind kind) => kind == CoreKind.SingBox ? "sing-box" : "mihomo";
    public static string CoreTitleFor(CoreKind kind) => kind == CoreKind.SingBox ? "sing-box" : "Mihomo Core";
    public static string ProfileId(CoreKind kind) => "desktop:" + WireKind(kind);
    public static string AppDirectory => Path.GetFullPath(AppContext.BaseDirectory);
    public static string ResourceDirectory => Path.Combine(AppDirectory, "resources");
    public static string LogDirectory => Path.Combine(ResourceDirectory, "logs");
    public static string WebViewUserDataDirectory => Path.Combine(ResourceDirectory, "webview-data-v2");
    public static string SettingsPath => Path.Combine(AppDirectory, "settings.json");

    public static AppSettings CreateDefault(string appDirectory) => new()
    {
        Profiles = new()
        {
            Mihomo = new() { ExePath = Path.Combine(appDirectory, "mihomo", "mihomo.exe"), ConfigPath = Path.Combine(appDirectory, "mihomo", "config.yaml") },
            SingBox = new() { ExePath = Path.Combine(appDirectory, "sing-box", "sing-box.exe"), ConfigPath = Path.Combine(appDirectory, "sing-box", "config.json") }
        }
    };

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<CoreKind>(allowIntegerValues: false) }
    };
}

public sealed class AppSettingsLoadException(string path, Exception inner)
    : Exception($"无法读取新版设置文件 {path}。原文件未修改；请使用全新目录或恢复有效的 schemaVersion=2 设置。", inner);

public sealed class SettingsConflictException() : Exception("配置已被其他操作修改，请重新读取后再提交。");

public sealed record SecretEdit
{
    public string Action { get; init; } = "keep";
    public string? Value { get; init; }
}

public sealed record CoreProfileEdit
{
    public required string ExePath { get; init; }
    public required string ConfigPath { get; init; }
    public required string ApiUrl { get; init; }
    public SecretEdit Secret { get; init; } = new();
}

internal sealed record CoreLaunchSpec(CoreKind Kind, CoreProfile Profile)
{
    public string Title => AppSettings.CoreTitleFor(Kind);
    public bool SameProcessTarget(CoreLaunchSpec other) => Kind == other.Kind
        && string.Equals(Profile.ExePath, other.Profile.ExePath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Profile.ConfigPath, other.Profile.ConfigPath, StringComparison.OrdinalIgnoreCase);
}
