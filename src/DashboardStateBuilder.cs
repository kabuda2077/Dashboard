namespace Dashboard;

internal sealed class DashboardStateBuilder
{
    private readonly AppSettings _settings;
    private readonly CoreProcessManager _core;
    private readonly ProxyGroupIconCache _iconCache;
    private readonly Func<string> _getCoreVersion;
    private readonly Func<bool> _isWindowMaximized;
    private readonly Func<bool?> _getReadOnlyTunEnabled;
    private readonly Func<string> _getLogText;
    private readonly Func<Uri> _getDashboardUri;
    private readonly Func<bool> _isCoreUpgrading;
    private readonly Func<bool> _isCoreSwitching;

    public DashboardStateBuilder(
        AppSettings settings,
        CoreProcessManager core,
        ProxyGroupIconCache iconCache,
        Func<string> getCoreVersion,
        Func<bool> isWindowMaximized,
        Func<bool?> getReadOnlyTunEnabled,
        Func<string> getLogText,
        Func<Uri> getDashboardUri,
        Func<bool> isCoreUpgrading,
        Func<bool> isCoreSwitching)
    {
        _settings = settings;
        _core = core;
        _iconCache = iconCache;
        _getCoreVersion = getCoreVersion;
        _isWindowMaximized = isWindowMaximized;
        _getReadOnlyTunEnabled = getReadOnlyTunEnabled;
        _getLogText = getLogText;
        _getDashboardUri = getDashboardUri;
        _isCoreUpgrading = isCoreUpgrading;
        _isCoreSwitching = isCoreSwitching;
    }

    public DashboardState Build()
    {
        return new DashboardState
        {
            IsRunning = _core.IsRunning,
            ProcessId = _core.ProcessId,
            CoreType = _settings.CoreType,
            CoreTitle = _settings.CoreTitle,
            CoreVersion = _getCoreVersion(),
            CorePath = _settings.ActiveCorePath,
            ConfigPath = _settings.ActiveConfigPath,
            ApiUrl = _settings.ActiveDashboardApiUrl,
            Secret = _settings.ActiveSecret,
            MihomoCorePath = _settings.CorePath,
            MihomoConfigPath = _settings.ConfigPath,
            MihomoApiUrl = _settings.DashboardApiUrl,
            MihomoSecret = _settings.Secret,
            SingBoxCorePath = _settings.SingBoxCorePath,
            SingBoxConfigPath = _settings.SingBoxConfigPath,
            SingBoxApiUrl = _settings.SingBoxApiUrl,
            SingBoxSecret = _settings.SingBoxSecret,
            SetupCompleted = _settings.SetupCompleted,
            ReadOnlyTunEnabled = _getReadOnlyTunEnabled(),
            StartCoreOnLaunch = _settings.StartCoreOnLaunch,
            MinimizeToTray = _settings.MinimizeToTray,
            LightweightMode = _settings.LightweightMode,
            Autostart = _settings.Autostart,
            CanUpgradeCore = true,
            IsCoreUpgrading = _isCoreUpgrading(),
            IsCoreSwitching = _isCoreSwitching(),
            IsWindowMaximized = _isWindowMaximized(),
            LogText = _getLogText(),
            IconCacheMap = _iconCache.GetDashboardMap(_getDashboardUri()),
            DashboardSettings = _settings.DashboardSettings
        };
    }

    public DashboardRuntimeState BuildRuntime()
    {
        return new DashboardRuntimeState
        {
            IsRunning = _core.IsRunning,
            ProcessId = _core.ProcessId,
            CoreTitle = _settings.CoreTitle,
            CoreVersion = _getCoreVersion(),
            CanUpgradeCore = true,
            IsCoreUpgrading = _isCoreUpgrading(),
            IsCoreSwitching = _isCoreSwitching(),
            IsWindowMaximized = _isWindowMaximized()
        };
    }

    public IReadOnlyDictionary<string, string> BuildIconCacheMap()
    {
        return _iconCache.GetDashboardMap(_getDashboardUri());
    }
}
