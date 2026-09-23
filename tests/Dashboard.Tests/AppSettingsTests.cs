using System.Text.Json;
using Dashboard;

namespace Dashboard.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void DefaultsUsePortableAppDirectory()
    {
        CleanSettingsFile();

        var settings = new AppSettings();

        AssertSamePath(Path.Combine(AppSettings.AppDirectory, "mihomo", "mihomo.exe"), settings.CorePath);
        AssertSamePath(Path.Combine(AppSettings.AppDirectory, "mihomo", "config.yaml"), settings.ConfigPath);
        AssertSamePath(Path.Combine(AppSettings.AppDirectory, "sing-box", "sing-box.exe"), settings.SingBoxCorePath);
        AssertSamePath(Path.Combine(AppSettings.AppDirectory, "sing-box", "config.json"), settings.SingBoxConfigPath);
        AssertSamePath(Path.Combine(AppSettings.AppDirectory, "resources", "logs"), AppSettings.LogDirectory);
    }

    [Fact]
    public void LoadMigratesLegacyDefaultConfigPathAndNormalizesCoreType()
    {
        CleanSettingsFile();
        var legacyConfigPath = Path.Combine(AppSettings.AppDirectory, "config", "config.yaml");
        WriteSettingsJson(new
        {
            CoreType = "unknown",
            ConfigPath = legacyConfigPath,
            DashboardApiUrl = "http://127.0.0.1:9090"
        });

        var settings = AppSettings.Load();

        Assert.Equal(AppSettings.CoreTypeMihomo, settings.CoreType);
        Assert.True(settings.SetupCompleted);
        AssertSamePath(Path.Combine(AppSettings.AppDirectory, "mihomo", "config.yaml"), settings.ConfigPath);
    }

    [Fact]
    public void LoadLeavesDashboardSettingsNullWhenLegacyFileDoesNotContainThem()
    {
        CleanSettingsFile();
        WriteSettingsJson(new
        {
            SetupCompleted = true
        });

        var settings = AppSettings.Load();

        Assert.Null(settings.DashboardSettings);
    }

    [Fact]
    public void SaveAndLoadPreservesDashboardSettings()
    {
        CleanSettingsFile();
        var settings = new AppSettings
        {
            DashboardSettings = new Dictionary<string, string>
            {
                ["config/default-theme"] = "\"light\"",
                ["config/proxy-sort-type"] = "\"default\""
            }
        };

        settings.Save();
        var loaded = AppSettings.Load();

        Assert.NotNull(loaded.DashboardSettings);
        Assert.Equal("\"light\"", loaded.DashboardSettings["config/default-theme"]);
        Assert.Equal("\"default\"", loaded.DashboardSettings["config/proxy-sort-type"]);
    }

    private static void WriteSettingsJson(object value)
    {
        Directory.CreateDirectory(AppSettings.SettingsDirectory);
        File.WriteAllText(AppSettings.SettingsPath, JsonSerializer.Serialize(value));
    }

    private static void CleanSettingsFile()
    {
        if (File.Exists(AppSettings.SettingsPath))
        {
            File.Delete(AppSettings.SettingsPath);
        }
    }

    private static void AssertSamePath(string expected, string actual)
    {
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(actual), ignoreCase: true);
    }
}
