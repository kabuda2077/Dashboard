namespace Dashboard.Tests;

public sealed class HostSettingsSnapshotTests
{
    [Fact]
    public void FailedPersistenceRollsBackEveryHostFieldButKeepsConcurrentDashboardPreferences()
    {
        var settings = new AppSettings
        {
            CoreType = AppSettings.CoreTypeMihomo,
            CorePath = "old-core",
            Secret = "old-secret",
            ProtectedSecret = "old-protected",
            Autostart = false,
            DashboardSettings = new Dictionary<string, string> { ["config/theme"] = "old" }
        };

        Assert.Throws<IOException>(() => HostSettingsTransaction.Execute(settings, () =>
        {
            settings.CoreType = AppSettings.CoreTypeSingBox;
            settings.CorePath = "new-core";
            settings.Secret = "new-secret";
            settings.ProtectedSecret = "new-protected";
            settings.Autostart = true;
        }, () =>
        {
            settings.DashboardSettings = new Dictionary<string, string> { ["config/theme"] = "new" };
            throw new IOException("simulated atomic write failure");
        }));

        Assert.Equal(AppSettings.CoreTypeMihomo, settings.CoreType);
        Assert.Equal("old-core", settings.CorePath);
        Assert.Equal("old-secret", settings.Secret);
        Assert.Equal("old-protected", settings.ProtectedSecret);
        Assert.False(settings.Autostart);
        Assert.Equal("new", settings.DashboardSettings!["config/theme"]);
    }

    [Fact]
    public void RestoreRollsBackHostFieldsWithoutOverwritingDashboardPreferences()
    {
        var settings = new AppSettings
        {
            CoreType = AppSettings.CoreTypeMihomo,
            CorePath = "old-core",
            Secret = "old-secret",
            Autostart = false,
            DashboardSettings = new Dictionary<string, string> { ["config/theme"] = "old" }
        };
        var snapshot = HostSettingsSnapshot.Capture(settings);

        settings.CoreType = AppSettings.CoreTypeSingBox;
        settings.CorePath = "new-core";
        settings.Secret = "new-secret";
        settings.Autostart = true;
        settings.DashboardSettings = new Dictionary<string, string> { ["config/theme"] = "new" };
        snapshot.Restore(settings);

        Assert.Equal(AppSettings.CoreTypeMihomo, settings.CoreType);
        Assert.Equal("old-core", settings.CorePath);
        Assert.Equal("old-secret", settings.Secret);
        Assert.False(settings.Autostart);
        Assert.Equal("new", settings.DashboardSettings!["config/theme"]);
    }
}
