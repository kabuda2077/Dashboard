using System.Text.Json;
using System.Text.Json.Serialization;

namespace MihomoDashboard;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string CorePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "cores", "mihomo.exe");
    public string ConfigPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "config.yaml");
    public string DashboardApiUrl { get; set; } = "http://127.0.0.1:9090";
    public string Secret { get; set; } = "";
    public bool StartCoreOnLaunch { get; set; }
    public bool MinimizeToTray { get; set; } = true;

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MihomoDashboard");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        Directory.CreateDirectory(SettingsDirectory);

        if (!File.Exists(SettingsPath))
        {
            var defaults = new AppSettings();
            defaults.Save();
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
