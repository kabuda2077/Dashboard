namespace Dashboard;

// Snapshot only host-owned fields. DashboardSettings remains independently writable.
internal static class HostSettingsTransaction
{
    public static void Execute(AppSettings settings, Action mutation, Action persist)
    {
        settings.ExecuteSynchronized(() =>
        {
            var previous = HostSettingsSnapshot.Capture(settings);
            try
            {
                mutation();
                persist();
            }
            catch
            {
                previous.Restore(settings);
                throw;
            }
        });
    }
}

internal sealed record HostSettingsSnapshot(
    string CoreType,
    string CorePath,
    string ConfigPath,
    string DashboardApiUrl,
    string Secret,
    string? ProtectedSecret,
    bool SecretDecryptionFailed,
    string SingBoxCorePath,
    string SingBoxConfigPath,
    string SingBoxApiUrl,
    string SingBoxSecret,
    string? ProtectedSingBoxSecret,
    bool SingBoxSecretDecryptionFailed,
    bool SetupCompleted,
    bool StartCoreOnLaunch,
    bool MinimizeToTray,
    bool LightweightMode,
    bool Autostart)
{
    public static HostSettingsSnapshot Capture(AppSettings settings) => new(
        settings.CoreType, settings.CorePath, settings.ConfigPath, settings.DashboardApiUrl,
        settings.Secret, settings.ProtectedSecret, settings.SecretDecryptionFailed,
        settings.SingBoxCorePath, settings.SingBoxConfigPath,
        settings.SingBoxApiUrl, settings.SingBoxSecret, settings.ProtectedSingBoxSecret,
        settings.SingBoxSecretDecryptionFailed,
        settings.SetupCompleted, settings.StartCoreOnLaunch, settings.MinimizeToTray,
        settings.LightweightMode, settings.Autostart);

    public void Restore(AppSettings settings)
    {
        settings.CoreType = CoreType;
        settings.CorePath = CorePath;
        settings.ConfigPath = ConfigPath;
        settings.DashboardApiUrl = DashboardApiUrl;
        settings.RestoreSecretPersistenceState(
            isSingBox: false, Secret, ProtectedSecret, SecretDecryptionFailed);
        settings.SingBoxCorePath = SingBoxCorePath;
        settings.SingBoxConfigPath = SingBoxConfigPath;
        settings.SingBoxApiUrl = SingBoxApiUrl;
        settings.RestoreSecretPersistenceState(
            isSingBox: true, SingBoxSecret, ProtectedSingBoxSecret, SingBoxSecretDecryptionFailed);
        settings.SetupCompleted = SetupCompleted;
        settings.StartCoreOnLaunch = StartCoreOnLaunch;
        settings.MinimizeToTray = MinimizeToTray;
        settings.LightweightMode = LightweightMode;
        settings.Autostart = Autostart;
    }
}
