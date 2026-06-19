using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dashboard;

public sealed class AppSettings
{
    public const string CoreTypeMihomo = "mihomo";
    public const string CoreTypeSingBox = "sing-box";
    private const string AppDirectoryName = "Dashboard";
    private const string LegacyAppDirectoryName = "MihomoDashboard";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string CoreType { get; set; } = CoreTypeMihomo;
    public string CorePath { get; set; } = @"E:\APP\Dashboard\mihomo\mihomo.exe";
    public string ConfigPath { get; set; } = DefaultConfigPath;
    public string DashboardApiUrl { get; set; } = "http://127.0.0.1:9090";
    public string Secret { get; set; } = "";
    public string? ProtectedSecret { get; set; }
    public string SingBoxCorePath { get; set; } = @"E:\APP\Dashboard\sing-box\sing-box.exe";
    public string SingBoxConfigPath { get; set; } = @"E:\APP\Dashboard\sing-box\config.json";
    public string SingBoxApiUrl { get; set; } = "http://127.0.0.1:9090";
    public string SingBoxSecret { get; set; } = "";
    public string? ProtectedSingBoxSecret { get; set; }
    public bool StartCoreOnLaunch { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool LightweightMode { get; set; } = true;
    public bool Autostart { get; set; }

    [JsonIgnore]
    public bool IsSingBox => string.Equals(NormalizeCoreType(CoreType), CoreTypeSingBox, StringComparison.Ordinal);

    [JsonIgnore]
    public string CoreDisplayName => IsSingBox ? "sing-box" : "mihomo";

    [JsonIgnore]
    public string CoreTitle => IsSingBox ? "sing-box" : "Mihomo Core";

    [JsonIgnore]
    public string ActiveCorePath
    {
        get => IsSingBox ? SingBoxCorePath : CorePath;
        set
        {
            if (IsSingBox)
            {
                SingBoxCorePath = value;
            }
            else
            {
                CorePath = value;
            }
        }
    }

    [JsonIgnore]
    public string ActiveConfigPath
    {
        get => IsSingBox ? SingBoxConfigPath : ConfigPath;
        set
        {
            if (IsSingBox)
            {
                SingBoxConfigPath = value;
            }
            else
            {
                ConfigPath = value;
            }
        }
    }

    [JsonIgnore]
    public string ActiveDashboardApiUrl
    {
        get => IsSingBox ? SingBoxApiUrl : DashboardApiUrl;
        set
        {
            if (IsSingBox)
            {
                SingBoxApiUrl = value;
            }
            else
            {
                DashboardApiUrl = value;
            }
        }
    }

    [JsonIgnore]
    public string ActiveSecret
    {
        get => IsSingBox ? SingBoxSecret : Secret;
        set
        {
            if (IsSingBox)
            {
                SingBoxSecret = value;
            }
            else
            {
                Secret = value;
            }
        }
    }

    public static string AppDirectory => ResolveAppDirectory();

    public static string SettingsDirectory => AppDirectory;

    private static string LegacyDashboardSettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDirectoryName);

    private static string LegacyMihomoSettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyAppDirectoryName);

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    private static string LegacyDashboardSettingsPath => Path.Combine(LegacyDashboardSettingsDirectory, "settings.json");

    private static string LegacyMihomoSettingsPath => Path.Combine(LegacyMihomoSettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        Directory.CreateDirectory(SettingsDirectory);
        MigrateLegacySettingsFile();

        if (!File.Exists(SettingsPath))
        {
            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            var shouldSave = settings.RestoreSecrets(json);
            settings.CoreType = NormalizeCoreType(settings.CoreType);
            settings.MigrateDefaultConfigPath();
            if (shouldSave)
            {
                settings.Save();
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        ProtectedSecret = SecretProtector.Protect(Secret);
        ProtectedSingBoxSecret = SecretProtector.Protect(SingBoxSecret);
        CoreType = NormalizeCoreType(CoreType);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static string DefaultConfigPath => @"E:\APP\Dashboard\mihomo\config.yaml";

    private static string LegacyDefaultConfigPath => Path.Combine(AppDirectory, "config", "config.yaml");

    private static string ResolveAppDirectory()
    {
        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var trimmedBaseDirectory = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetFileName(trimmedBaseDirectory).Equals("EBWebView", StringComparison.OrdinalIgnoreCase))
        {
            return Directory.GetParent(trimmedBaseDirectory)?.FullName ?? baseDirectory;
        }

        return baseDirectory;
    }

    private static void MigrateLegacySettingsFile()
    {
        if (File.Exists(SettingsPath))
        {
            return;
        }

        var legacyPath = new[] { LegacyDashboardSettingsPath, LegacyMihomoSettingsPath }
            .FirstOrDefault(File.Exists);
        if (legacyPath is null)
        {
            return;
        }

        try
        {
            File.Copy(legacyPath, SettingsPath, overwrite: false);
        }
        catch
        {
        }
    }

    public static void MigrateLegacyDataDirectory(string directoryName)
    {
        var targetDirectory = Path.Combine(SettingsDirectory, directoryName);
        var legacyDirectory = new[] { LegacyDashboardSettingsDirectory, LegacyMihomoSettingsDirectory }
            .Select(directory => Path.Combine(directory, directoryName))
            .FirstOrDefault(Directory.Exists);
        if (legacyDirectory is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
            foreach (var sourcePath in Directory.EnumerateFiles(legacyDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(legacyDirectory, sourcePath);
                var targetPath = Path.Combine(targetDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetDirectory);
                if (!File.Exists(targetPath))
                {
                    File.Copy(sourcePath, targetPath);
                }
            }
        }
        catch
        {
        }
    }

    private void MigrateDefaultConfigPath()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath)
            || !IsSamePath(ConfigPath, LegacyDefaultConfigPath)
            || File.Exists(ConfigPath))
        {
            return;
        }

        ConfigPath = DefaultConfigPath;
        Save();
    }

    public static string NormalizeCoreType(string? coreType)
    {
        return string.Equals(coreType, CoreTypeSingBox, StringComparison.OrdinalIgnoreCase)
            ? CoreTypeSingBox
            : CoreTypeMihomo;
    }

    private bool RestoreSecrets(string json)
    {
        var shouldSave = RestoreSecret(json, nameof(Secret), ProtectedSecret, value => Secret = value);
        shouldSave |= RestoreSecret(
            json,
            nameof(SingBoxSecret),
            ProtectedSingBoxSecret,
            value => SingBoxSecret = value);
        return shouldSave;
    }

    private bool RestoreSecret(
        string json,
        string portablePropertyName,
        string? protectedValue,
        Action<string> setSecret)
    {
        TryReadStringProperty(json, portablePropertyName, out var portableSecret);

        if (!string.IsNullOrWhiteSpace(protectedValue))
        {
            try
            {
                setSecret(SecretProtector.Unprotect(protectedValue));
            }
            catch
            {
                setSecret(portableSecret);
            }

            return string.IsNullOrWhiteSpace(portableSecret);
        }

        if (string.IsNullOrWhiteSpace(portableSecret))
        {
            return false;
        }

        setSecret(portableSecret);
        return true;
    }

    private static bool TryReadStringProperty(string json, string propertyName, out string value)
    {
        value = "";
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return true;
    }

    private static bool IsSamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }
}
