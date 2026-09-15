using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard;

internal sealed class DashboardHost : IDisposable
{
    internal const string MihomoRepositoryUrl = "https://github.com/MetaCubeX/mihomo";
    internal const string SingBoxRepositoryUrl = "https://github.com/reF1nd/sing-box";

    private readonly CoreProcessManager _core = new();
    private readonly ProxyGroupIconCache _iconCache = new();
    private readonly DashboardServer _dashboardServer;
    private readonly CoreLifecycleController _coreLifecycle;
    private readonly SemaphoreSlim _autostartGate = new(1, 1);
    private readonly SemaphoreSlim _appUpdateGate = new(1, 1);
    private readonly SemaphoreSlim _coreUpdateGate = new(1, 1);
    private string _cachedCoreVersionKey = "";
    private string _cachedCoreVersion = "";
    private string _loadingCoreVersionKey = "";
    private Task? _coreVersionLoadTask;
    private string _cachedSingBoxTunKey = "";
    private bool _cachedSingBoxTunConfigured;
    private bool _disposed;

    public DashboardHost()
    {
        Settings = AppSettings.Load();
        _dashboardServer = new DashboardServer(
            Path.Combine(AppSettings.AppDirectory, "resources", "dashboard"),
            _iconCache.CacheDirectory);
        DashboardUri = _dashboardServer.Start();
        _coreLifecycle = new CoreLifecycleController(
            Settings,
            _core,
            new CoreLifecycleServices
            {
                IsRunningAsAdministrator = IsRunningAsAdministrator,
                ShouldKeepMinimizedForRelaunch = () => ShouldKeepMinimizedForRelaunch?.Invoke() ?? true,
                RelaunchAsAdministrator = (startCore, startMinimized, elevatedRestart) =>
                    RelaunchRequested?.Invoke(this, new HostRelaunchRequest(startCore, startMinimized, elevatedRestart)),
                ShowNotice = ShowNotice,
                PublishState = PublishStateChanged,
                RefreshIconCache = RefreshIconCache,
                ShowTrayNotification = message => TrayNotificationRequested?.Invoke(this, message),
                ShowMessage = (title, message, icon) =>
                    MessageRequested?.Invoke(this, new HostMessageRequest(title, message, icon)),
                RunOnUiThread = action => action()
            });

        _core.StatusChanged += OnCoreStatusChanged;
        _core.LogReceived += OnCoreLogReceived;
        _iconCache.CacheChanged += OnIconCacheChanged;
        _iconCache.LoadExisting(Settings.ConfigPath);
    }

    public AppSettings Settings { get; }
    public Uri DashboardUri { get; }
    public bool IsRunning => _core.IsRunning;
    public int? ProcessId => _core.ProcessId;
    public bool IsUpgradeInProgress => _coreLifecycle.IsUpgradeInProgress;
    public bool IsSwitchInProgress => _coreLifecycle.IsSwitchInProgress;
    public bool IsAutostartUpdating { get; private set; }
    public bool IsAppUpdateChecking { get; private set; }
    public bool AppUpdateAvailable { get; private set; }
    public string LatestAppVersion { get; private set; } = "";
    public bool IsCoreUpdateChecking { get; private set; }
    public bool CoreUpdateAvailable { get; private set; }
    public string LatestCoreVersion { get; private set; } = "";
    public Func<bool>? ShouldKeepMinimizedForRelaunch { get; set; }

    public event EventHandler? StateChanged;
    public event EventHandler? RuntimeStateChanged;
    public event EventHandler<string>? LogReceived;
    public event EventHandler? IconCacheChanged;
    public event EventHandler<string>? NoticeRequested;
    public event EventHandler<string>? TrayNotificationRequested;
    public event EventHandler<HostMessageRequest>? MessageRequested;
    public event EventHandler<HostRelaunchRequest>? RelaunchRequested;

    public DashboardState BuildState(bool isWindowMaximized)
    {
        return new DashboardState
        {
            IsRunning = _core.IsRunning,
            ProcessId = _core.ProcessId,
            CoreType = Settings.CoreType,
            CoreTitle = Settings.CoreTitle,
            CoreVersion = GetCachedActiveCoreVersion(),
            CorePath = Settings.ActiveCorePath,
            ConfigPath = Settings.ActiveConfigPath,
            ApiUrl = Settings.ActiveDashboardApiUrl,
            Secret = Settings.ActiveSecret,
            MihomoCorePath = Settings.CorePath,
            MihomoConfigPath = Settings.ConfigPath,
            MihomoApiUrl = Settings.DashboardApiUrl,
            MihomoSecret = Settings.Secret,
            SingBoxCorePath = Settings.SingBoxCorePath,
            SingBoxConfigPath = Settings.SingBoxConfigPath,
            SingBoxApiUrl = Settings.SingBoxApiUrl,
            SingBoxSecret = Settings.SingBoxSecret,
            SetupCompleted = Settings.SetupCompleted,
            ReadOnlyTunEnabled = GetActiveTunConfigured(),
            StartCoreOnLaunch = Settings.StartCoreOnLaunch,
            MinimizeToTray = Settings.MinimizeToTray,
            LightweightMode = Settings.LightweightMode,
            Autostart = Settings.Autostart,
            IsAutostartUpdating = IsAutostartUpdating,
            AppVersion = DashboardVersion.Current,
            LatestAppVersion = LatestAppVersion,
            IsAppUpdateChecking = IsAppUpdateChecking,
            AppUpdateAvailable = AppUpdateAvailable,
            LatestCoreVersion = LatestCoreVersion,
            IsCoreUpdateChecking = IsCoreUpdateChecking,
            CoreUpdateAvailable = CoreUpdateAvailable,
            CanUpgradeCore = true,
            IsCoreUpgrading = _coreLifecycle.IsUpgradeInProgress,
            IsCoreSwitching = _coreLifecycle.IsSwitchInProgress,
            IsWindowMaximized = isWindowMaximized,
            LogText = _core.GetLogTail(8000),
            IconCacheMap = GetIconCacheMap(),
            DashboardSettings = Settings.DashboardSettings
        };
    }

    public DashboardRuntimeState BuildRuntimeState(bool isWindowMaximized)
    {
        return new DashboardRuntimeState
        {
            IsRunning = _core.IsRunning,
            ProcessId = _core.ProcessId,
            CoreTitle = Settings.CoreTitle,
            CoreVersion = GetCachedActiveCoreVersion(),
            CanUpgradeCore = true,
            IsCoreUpgrading = _coreLifecycle.IsUpgradeInProgress,
            IsCoreSwitching = _coreLifecycle.IsSwitchInProgress,
            IsWindowMaximized = isWindowMaximized
        };
    }

    public IReadOnlyDictionary<string, string> GetIconCacheMap()
    {
        return _iconCache.GetDashboardMap(DashboardUri);
    }

    public void StartCore(bool showTrayNotification = false)
    {
        _coreLifecycle.Start(showTrayNotification);
    }

    public void StopCore(bool showTrayNotification = false)
    {
        _coreLifecycle.Stop(showTrayNotification);
    }

    public bool RestartCore(bool showTrayNotification = false)
    {
        return _coreLifecycle.Restart(showTrayNotification);
    }

    public async Task SwitchCoreAsync(string targetCoreType)
    {
        ResetCoreUpdateState();
        await _coreLifecycle.SwitchAsync(targetCoreType);
        await CheckForCoreUpdateAsync();
    }

    public async Task UpgradeCoreAsync()
    {
        ResetCoreUpdateState();
        await _coreLifecycle.UpgradeAsync();
        await CheckForCoreUpdateAsync();
    }

    public async Task CheckForCoreUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (IsUpgradeInProgress || IsSwitchInProgress
            || !await _coreUpdateGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            ResetCoreUpdateState(publish: false);
            IsCoreUpdateChecking = true;
            PublishStateChanged();
            var result = await CoreUpdateChecker.CheckAsync(
                Settings.ActiveCorePath,
                Settings.IsSingBox,
                cancellationToken);
            LatestCoreVersion = result.LatestVersion;
            CoreUpdateAvailable = result.UpdateAvailable;
            HostOperationLogger.Info(
                "update",
                $"Core update check completed: core={Settings.CoreType}, current={result.CurrentVersion}, latest={result.LatestVersion}, available={result.UpdateAvailable}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ResetCoreUpdateState(publish: false);
            HostOperationLogger.Error("update", $"{Settings.CoreTitle} update check failed.", ex);
        }
        finally
        {
            IsCoreUpdateChecking = false;
            PublishStateChanged();
            _coreUpdateGate.Release();
        }
    }

    public async Task CheckForAppUpdateAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (!await _appUpdateGate.WaitAsync(0, cancellationToken))
        {
            if (manual)
            {
                ShowNotice("正在检查 Dashboard 更新，请稍候。");
            }
            return;
        }

        try
        {
            IsAppUpdateChecking = true;
            PublishStateChanged();
            var result = await AppUpdateChecker.CheckAsync(
                DashboardVersion.Current,
                cancellationToken: cancellationToken);
            LatestAppVersion = result.LatestVersion;
            AppUpdateAvailable = result.UpdateAvailable;
            HostOperationLogger.Info(
                "update",
                $"Dashboard update check completed: current={result.CurrentVersion}, latest={result.LatestVersion}, available={result.UpdateAvailable}.");

            if (manual && result.UpdateAvailable)
            {
                ShowNotice($"发现 Dashboard 新版本 v{result.LatestVersion}，可前往 Release 下载。");
            }
            else if (manual)
            {
                ShowNotice($"当前已是最新版本（v{result.CurrentVersion}）。");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("update", "Dashboard update check failed.", ex);
            if (manual)
            {
                ShowNotice("检查 Dashboard 更新失败，请稍后重试。");
            }
        }
        finally
        {
            IsAppUpdateChecking = false;
            PublishStateChanged();
            _appUpdateGate.Release();
        }
    }

    public void OpenAppReleasePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppUpdateChecker.ReleasesPageUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("update", "Failed to open Dashboard Releases page.", ex);
            ShowNotice("无法打开 GitHub Release 页面，请检查系统默认浏览器。");
        }
    }

    public void OpenCoreRepositoryPage()
    {
        var repositoryUrl = GetCoreRepositoryUrl(Settings.IsSingBox);
        try
        {
            Process.Start(new ProcessStartInfo(repositoryUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("core", $"Failed to open core repository: {repositoryUrl}", ex);
            ShowNotice("无法打开内核 GitHub 仓库，请检查系统默认浏览器。");
        }
    }

    internal static string GetCoreRepositoryUrl(bool isSingBox)
    {
        return isSingBox ? SingBoxRepositoryUrl : MihomoRepositoryUrl;
    }

    public void CompleteSetup()
    {
        Settings.SetupCompleted = true;
        Settings.Save();
    }

    public async Task SaveSettingsAsync(JsonElement root, bool showMessage)
    {
        var previousCoreIdentity = GetActiveCoreIdentity();
        var previousAutostart = Settings.Autostart;
        var requestedAutostart = root.TryGetProperty("autostart", out var autostart)
            && autostart.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? autostart.GetBoolean()
            : previousAutostart;
        Settings.CoreType = AppSettings.NormalizeCoreType(HostBridgeJson.GetString(root, "coreType", Settings.CoreType));
        Settings.CorePath = HostBridgeJson.GetString(root, "mihomoCorePath", Settings.CorePath).Trim();
        Settings.ConfigPath = HostBridgeJson.GetString(root, "mihomoConfigPath", Settings.ConfigPath).Trim();
        Settings.DashboardApiUrl = HostBridgeJson.GetString(root, "mihomoApiUrl", Settings.DashboardApiUrl).Trim();
        Settings.Secret = HostBridgeJson.GetString(root, "mihomoSecret", Settings.Secret);
        Settings.SingBoxCorePath = HostBridgeJson.GetString(root, "singBoxCorePath", Settings.SingBoxCorePath).Trim();
        Settings.SingBoxConfigPath = HostBridgeJson.GetString(root, "singBoxConfigPath", Settings.SingBoxConfigPath).Trim();
        Settings.SingBoxApiUrl = HostBridgeJson.GetString(root, "singBoxApiUrl", Settings.SingBoxApiUrl).Trim();
        Settings.SingBoxSecret = HostBridgeJson.GetString(root, "singBoxSecret", Settings.SingBoxSecret);
        Settings.ActiveCorePath = HostBridgeJson.GetString(root, "corePath", Settings.ActiveCorePath).Trim();
        Settings.ActiveConfigPath = HostBridgeJson.GetString(root, "configPath", Settings.ActiveConfigPath).Trim();
        Settings.ActiveDashboardApiUrl = HostBridgeJson.GetString(root, "apiUrl", Settings.ActiveDashboardApiUrl).Trim();
        Settings.ActiveSecret = HostBridgeJson.GetString(root, "secret", Settings.ActiveSecret);
        Settings.StartCoreOnLaunch = HostBridgeJson.GetBool(root, "startCoreOnLaunch", Settings.StartCoreOnLaunch);
        Settings.MinimizeToTray = HostBridgeJson.GetBool(root, "minimizeToTray", Settings.MinimizeToTray);
        Settings.LightweightMode = HostBridgeJson.GetBool(root, "lightweightMode", Settings.LightweightMode);
        Settings.SetupCompleted = HostBridgeJson.GetBool(root, "setupCompleted", Settings.SetupCompleted);
        Settings.Autostart = requestedAutostart;
        Settings.Save();
        RefreshIconCache();
        PublishStateChanged();

        if (!string.Equals(previousCoreIdentity, GetActiveCoreIdentity(), StringComparison.OrdinalIgnoreCase))
        {
            ResetCoreUpdateState();
            _ = CheckForCoreUpdateAsync();
        }

        var autostartSucceeded = true;
        if (requestedAutostart != previousAutostart)
        {
            autostartSucceeded = await ApplyAutostartSettingAsync(
                requestedAutostart,
                previousAutostart,
                isMigration: false);
        }

        if (showMessage)
        {
            ShowNotice(autostartSucceeded ? "设置已保存。" : "其他设置已保存。");
        }
    }

    public async Task ReconcileAutostartAsync()
    {
        try
        {
            var hasLegacyEntry = AutostartManager.HasCurrentLegacyRunEntry();
            var status = AutostartManager.QueryStatus();
            if (hasLegacyEntry && !Settings.Autostart)
            {
                Settings.Autostart = true;
                Settings.Save();
            }

            if (Settings.Autostart)
            {
                if (!status.IsValid || hasLegacyEntry)
                {
                    await ApplyAutostartSettingAsync(true, true, isMigration: true);
                }
                return;
            }

            if (status.Exists || hasLegacyEntry)
            {
                await ApplyAutostartSettingAsync(false, status.Exists || hasLegacyEntry, isMigration: true);
            }
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("autostart", "Failed to reconcile autostart state.", ex);
            ShowNotice($"检查开机自启失败：{ex.Message}");
        }
    }

    public void SaveDashboardSettings(JsonElement root)
    {
        if (!root.TryGetProperty("settings", out var settingsProperty)
            || settingsProperty.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var dashboardSettings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in settingsProperty.EnumerateObject())
        {
            if (!setting.Name.StartsWith("config/", StringComparison.Ordinal)
                || setting.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            dashboardSettings[setting.Name] = setting.Value.GetString() ?? "";
        }

        Settings.DashboardSettings = dashboardSettings;
        Settings.Save();
    }

    public void SetActiveCorePath(string path)
    {
        Settings.ActiveCorePath = path;
        Settings.Save();
        PublishStateChanged();
    }

    public void SetActiveConfigPath(string path)
    {
        Settings.ActiveConfigPath = path;
        Settings.Save();
        RefreshIconCache();
        PublishStateChanged();
    }

    public void RefreshIconCache()
    {
        if (Settings.IsSingBox)
        {
            return;
        }

        var configPath = Settings.ConfigPath;
        _ = Task.Run(async () =>
        {
            try
            {
                await _iconCache.RefreshAsync(configPath);
            }
            catch (Exception ex)
            {
                HostOperationLogger.Error("icon-cache", "Icon cache refresh failed.", ex);
            }
        });
    }

    public void ShowNotice(string message)
    {
        NoticeRequested?.Invoke(this, message);
    }

    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void OnCoreStatusChanged(object? sender, EventArgs e)
    {
        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCoreLogReceived(object? sender, string entry)
    {
        _coreLifecycle.ObserveLogEntry(entry);
        LogReceived?.Invoke(this, entry);
    }

    private void OnIconCacheChanged(object? sender, EventArgs e)
    {
        IconCacheChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetActiveCoreIdentity()
    {
        return $"{Settings.CoreType}|{Settings.ActiveCorePath}";
    }

    private void ResetCoreUpdateState(bool publish = true)
    {
        LatestCoreVersion = "";
        CoreUpdateAvailable = false;
        if (publish)
        {
            PublishStateChanged();
        }
    }

    private bool? GetActiveTunConfigured()
    {
        return Settings.IsSingBox ? IsSingBoxTunConfigured() : null;
    }

    private bool IsSingBoxTunConfigured()
    {
        if (string.IsNullOrWhiteSpace(Settings.SingBoxConfigPath) || !File.Exists(Settings.SingBoxConfigPath))
        {
            _cachedSingBoxTunKey = "";
            _cachedSingBoxTunConfigured = false;
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(Settings.SingBoxConfigPath);
            var lastWrite = File.GetLastWriteTimeUtc(fullPath).Ticks;
            var key = $"{fullPath}|{lastWrite}";
            if (string.Equals(key, _cachedSingBoxTunKey, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedSingBoxTunConfigured;
            }

            _cachedSingBoxTunKey = key;
            _cachedSingBoxTunConfigured = ReadSingBoxTunConfigured(fullPath);
            return _cachedSingBoxTunConfigured;
        }
        catch
        {
            return _cachedSingBoxTunConfigured;
        }
    }

    private static bool ReadSingBoxTunConfigured(string configPath)
    {
        try
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (!document.RootElement.TryGetProperty("inbounds", out var inbounds)
                || inbounds.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var inbound in inbounds.EnumerateArray())
            {
                if (inbound.ValueKind == JsonValueKind.Object
                    && inbound.TryGetProperty("type", out var typeProperty)
                    && typeProperty.ValueKind == JsonValueKind.String
                    && string.Equals(typeProperty.GetString(), "tun", StringComparison.OrdinalIgnoreCase))
                {
                    if (inbound.TryGetProperty("enabled", out var enabledProperty)
                        && enabledProperty.ValueKind == JsonValueKind.False)
                    {
                        return false;
                    }

                    if (inbound.TryGetProperty("disabled", out var disabledProperty)
                        && disabledProperty.ValueKind == JsonValueKind.True)
                    {
                        return false;
                    }

                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("config", "Failed to read sing-box TUN configuration.", ex);
        }

        return false;
    }

    private string GetCachedActiveCoreVersion()
    {
        var corePath = Settings.ActiveCorePath;
        if (string.IsNullOrWhiteSpace(corePath) || !File.Exists(corePath))
        {
            _cachedCoreVersionKey = "";
            _cachedCoreVersion = "";
            return "";
        }

        try
        {
            var fullPath = Path.GetFullPath(corePath);
            var lastWrite = File.GetLastWriteTimeUtc(fullPath).Ticks;
            var key = $"{fullPath}|{lastWrite}|{Settings.CoreType}";
            if (string.Equals(key, _cachedCoreVersionKey, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedCoreVersion;
            }

            StartCoreVersionReadIfNeeded(key, fullPath, Settings.IsSingBox);
            return "";
        }
        catch
        {
            return "";
        }
    }

    private void StartCoreVersionReadIfNeeded(string key, string corePath, bool isSingBox)
    {
        if (string.Equals(key, _loadingCoreVersionKey, StringComparison.OrdinalIgnoreCase)
            && _coreVersionLoadTask is { IsCompleted: false })
        {
            return;
        }

        _loadingCoreVersionKey = key;
        _coreVersionLoadTask = Task.Run(() => ReadCoreVersion(corePath, isSingBox)).ContinueWith(task =>
        {
            var versionChanged = false;
            if (task.Status == TaskStatus.RanToCompletion
                && string.Equals(key, _loadingCoreVersionKey, StringComparison.OrdinalIgnoreCase))
            {
                versionChanged = !string.IsNullOrWhiteSpace(_cachedCoreVersion)
                    && !string.IsNullOrWhiteSpace(task.Result)
                    && !string.Equals(_cachedCoreVersion, task.Result, StringComparison.OrdinalIgnoreCase);
                _cachedCoreVersionKey = key;
                _cachedCoreVersion = task.Result;
            }

            if (string.Equals(key, _loadingCoreVersionKey, StringComparison.OrdinalIgnoreCase))
            {
                _loadingCoreVersionKey = "";
            }

            RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
            if (versionChanged)
            {
                ResetCoreUpdateState();
                _ = CheckForCoreUpdateAsync();
            }
        }, TaskScheduler.Default);
    }

    private static string ReadCoreVersion(string corePath, bool isSingBox)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(corePath, isSingBox ? "version" : "-v")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                TryKill(process);
                return "";
            }

            Task.WaitAll([outputTask, errorTask], 1000);
            var output = string.Join(Environment.NewLine, new[] { outputTask.Result, errorTask.Result }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return FormatCoreVersion(output, isSingBox);
        }
        catch
        {
            return "";
        }
    }

    private static string FormatCoreVersion(string output, bool isSingBox)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "";
        }

        var match = Regex.Match(output, @"v?\d+\.\d+\.\d+(?:[-+.][A-Za-z0-9.-]+)?");
        if (!match.Success)
        {
            return "";
        }

        var version = match.Value.Trim();
        return isSingBox
            ? $"sing-box {version.TrimStart('v', 'V')}"
            : version;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private async Task<bool> ApplyAutostartSettingAsync(bool enabled, bool rollbackValue, bool isMigration)
    {
        await _autostartGate.WaitAsync();
        try
        {
            IsAutostartUpdating = true;
            PublishStateChanged();
            AutostartOperationResult result;
            try
            {
                result = await AutostartManager.SetEnabledAsync(enabled);
            }
            catch (Exception ex)
            {
                HostOperationLogger.Error("autostart", "Autostart update failed unexpectedly.", ex);
                result = AutostartOperationResult.Failure(ex.Message);
            }
            finally
            {
                IsAutostartUpdating = false;
            }

            if (result.Success)
            {
                Settings.Autostart = enabled;
                Settings.Save();
                PublishStateChanged();
                if (isMigration)
                {
                    ShowNotice(enabled
                        ? "开机自启已迁移到计划任务。"
                        : "已清理旧的开机自启配置。");
                }
                return true;
            }

            Settings.Autostart = rollbackValue;
            Settings.Save();
            PublishStateChanged();
            var message = enabled
                ? $"开机自启设置失败：{result.Message}"
                : $"关闭开机自启失败：{result.Message}";
            HostOperationLogger.Error("autostart", message, new InvalidOperationException(result.Message));
            ShowNotice(message);
            return false;
        }
        finally
        {
            _autostartGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _core.StatusChanged -= OnCoreStatusChanged;
        _core.LogReceived -= OnCoreLogReceived;
        _iconCache.CacheChanged -= OnIconCacheChanged;
        _core.Dispose();
        _dashboardServer.Dispose();
    }
}

internal sealed record HostRelaunchRequest(bool StartCore, bool StartMinimized, bool ElevatedRestart);

internal sealed record HostMessageRequest(string Title, string Message, MessageBoxIcon Icon);
