using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dashboard;

public sealed class AppSettings
{
    private const string AppDirectoryName = "Dashboard";
    private const string LegacyAppDirectoryName = "MihomoDashboard";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string CorePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "cores", "mihomo.exe");
    public string ConfigPath { get; set; } = DefaultConfigPath;
    public string DashboardApiUrl { get; set; } = "http://127.0.0.1:9090";
    [JsonIgnore]
    public string Secret { get; set; } = "";
    public string? ProtectedSecret { get; set; }
    public bool StartCoreOnLaunch { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool Autostart { get; set; }
    public string? ElevatedCoreTaskSignature { get; set; }

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDirectoryName);

    private static string LegacySettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyAppDirectoryName);

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    private static string LegacySettingsPath => Path.Combine(LegacySettingsDirectory, "settings.json");

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
            var shouldSave = settings.RestoreSecret(json);
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
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static string DefaultConfigPath => Path.Combine(AppContext.BaseDirectory, "config.yaml");

    private static string LegacyDefaultConfigPath => Path.Combine(AppContext.BaseDirectory, "config", "config.yaml");

    private static void MigrateLegacySettingsFile()
    {
        if (File.Exists(SettingsPath) || !File.Exists(LegacySettingsPath))
        {
            return;
        }

        try
        {
            File.Copy(LegacySettingsPath, SettingsPath, overwrite: false);
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

    private bool RestoreSecret(string json)
    {
        if (!string.IsNullOrWhiteSpace(ProtectedSecret))
        {
            try
            {
                Secret = SecretProtector.Unprotect(ProtectedSecret);
            }
            catch
            {
                Secret = "";
            }

            return false;
        }

        if (!TryReadStringProperty(json, nameof(Secret), out var legacySecret))
        {
            return false;
        }

        Secret = legacySecret;
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
