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
    private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(10);
    private readonly CoreLifecycleController _coreLifecycle;
    private readonly ShutdownTaskTracker _backgroundTasks = new();
    private readonly SemaphoreSlim _autostartGate = new(1, 1);
    private readonly SemaphoreSlim _appUpdateGate = new(1, 1);
    private int _coreUpdatePending;
    private readonly SemaphoreSlim _coreUpdateGate = new(1, 1);
    private string _cachedCoreVersionKey = "";
    private string _cachedCoreVersion = "";
    private string _loadingCoreVersionKey = "";
    private Task? _coreVersionLoadTask;
    private string _cachedSingBoxTunKey = "";
    private bool _cachedSingBoxTunConfigured;
    private long _iconRefreshGeneration;
    private long _coreRuntimeGeneration;
    private readonly object _shutdownSync = new();
    private Task? _shutdownTask;
    private bool _resourcesDisposed;
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
        var initialIconGeneration = Interlocked.Increment(ref _iconRefreshGeneration);
        _ = RunBackgroundTask(_ => LoadExistingIconCacheAsync(Settings.ConfigPath, initialIconGeneration));
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
    public event EventHandler<HostOutboundMessage>? AppUpdateResultRequested;
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
            SecretDecryptionFailed = Settings.ActiveSecretDecryptionFailed,
            MihomoSecretDecryptionFailed = Settings.SecretDecryptionFailed,
            SingBoxSecretDecryptionFailed = Settings.SingBoxSecretDecryptionFailed,
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

    public Task SwitchCoreAsync(string targetCoreType) => RunBackgroundTask(_ => SwitchCoreOwnedAsync(targetCoreType));

    private async Task SwitchCoreOwnedAsync(string targetCoreType)
    {
        ResetCoreUpdateState();
        await _coreLifecycle.SwitchAsync(targetCoreType);
        if (!_backgroundTasks.IsClosing) await CheckForCoreUpdateAsync(_backgroundTasks.Token);
    }

    public Task UpgradeCoreAsync() => RunBackgroundTask(_ => UpgradeCoreOwnedAsync());

    private async Task UpgradeCoreOwnedAsync()
    {
        ResetCoreUpdateState();
        await _coreLifecycle.UpgradeAsync();
        if (!_backgroundTasks.IsClosing) await CheckForCoreUpdateAsync(_backgroundTasks.Token);
    }

    public Task CheckForCoreUpdateAsync(CancellationToken cancellationToken = default) =>
        RunBackgroundTask(_ => CheckForCoreUpdateOwnedAsync(cancellationToken));

    private async Task CheckForCoreUpdateOwnedAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _backgroundTasks.IsClosing || IsUpgradeInProgress || IsSwitchInProgress) return;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _backgroundTasks.Token);
        cancellationToken = linkedCancellation.Token;
        if (!await _coreUpdateGate.WaitAsync(0, cancellationToken))
        {
            Interlocked.Exchange(ref _coreUpdatePending, 1);
            return;
        }

        var identity = GetActiveCoreIdentity();
        var corePath = Settings.ActiveCorePath;
        var isSingBox = Settings.IsSingBox;
        try
        {
            ResetCoreUpdateState(publish: false);
            IsCoreUpdateChecking = true;
            PublishStateChanged();
            var result = await CoreUpdateChecker.CheckAsync(
                corePath,
                isSingBox,
                cancellationToken);
            if (_disposed || !string.Equals(identity, GetActiveCoreIdentity(), StringComparison.OrdinalIgnoreCase)) return;
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
            if (_disposed || !string.Equals(identity, GetActiveCoreIdentity(), StringComparison.OrdinalIgnoreCase)) return;
            ResetCoreUpdateState(publish: false);
            HostOperationLogger.Error("update", $"{Settings.CoreTitle} update check failed.", ex);
        }
        finally
        {
            IsCoreUpdateChecking = false;
            PublishStateChanged();
            _coreUpdateGate.Release();
            var identityChanged = !string.Equals(identity, GetActiveCoreIdentity(), StringComparison.OrdinalIgnoreCase);
            var retry = Interlocked.Exchange(ref _coreUpdatePending, 0) != 0
                || (identityChanged && _core.IsRunning);
            if (retry && !_disposed && !cancellationToken.IsCancellationRequested) _ = CheckForCoreUpdateAsync(cancellationToken);
        }
    }

    public Task CheckForAppUpdateAsync(bool manual, CancellationToken cancellationToken = default) =>
        RunBackgroundTask(_ => CheckForAppUpdateOwnedAsync(manual, cancellationToken));

    private async Task CheckForAppUpdateOwnedAsync(bool manual, CancellationToken cancellationToken)
    {
        if (_backgroundTasks.IsClosing) return;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _backgroundTasks.Token);
        cancellationToken = linkedCancellation.Token;
        if (!await _appUpdateGate.WaitAsync(0, cancellationToken))
        {
            PublishAppUpdateResult(AppUpdateResultKind.Busy, manual);
            return;
        }

        try
        {
            IsAppUpdateChecking = true;
            PublishStateChanged();
            var result = await AppUpdateChecker.CheckAsync(
                DashboardVersion.Current,
                cancellationToken: cancellationToken);
            if (_backgroundTasks.IsClosing) return;
            LatestAppVersion = result.LatestVersion;
            AppUpdateAvailable = result.UpdateAvailable;
            HostOperationLogger.Info(
                "update",
                $"Dashboard update check completed: current={result.CurrentVersion}, latest={result.LatestVersion}, available={result.UpdateAvailable}.");

            PublishAppUpdateResult(
                result.UpdateAvailable ? AppUpdateResultKind.Available : AppUpdateResultKind.UpToDate,
                manual,
                result.CurrentVersion,
                result.LatestVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("update", "Dashboard update check failed.", ex);
            PublishAppUpdateResult(AppUpdateResultKind.Failed, manual);
        }
        finally
        {
            IsAppUpdateChecking = false;
            PublishStateChanged();
            _appUpdateGate.Release();
        }
    }

    private void PublishAppUpdateResult(
        string result,
        bool manual,
        string? currentVersion = null,
        string? latestVersion = null)
    {
        if (_disposed) return;
        AppUpdateResultRequested?.Invoke(
            this,
            HostOutboundMessage.AppUpdateResult(result, manual, currentVersion, latestVersion));
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

    public async Task SaveSettingsAsync(JsonElement root, bool showMessage)
    {
        _ = await _coreLifecycle.ExecuteConfigurationCommandAsync(
            () => SaveSettingsWithinLeaseAsync(root, showMessage),
            CoreConfigurationCommand.SaveOnly);
    }

    public Task<ConfigurationCommandResult> ExecuteSettingsCommandAsync(
        JsonElement root,
        CoreConfigurationCommand command,
        string targetCoreType = "",
        bool completeSetup = false) => _coreLifecycle.ExecuteConfigurationCommandAsync(
            async () =>
            {
                await SaveSettingsWithinLeaseAsync(root, showMessage: false);
                if (completeSetup)
                    ExecuteHostSettingsTransaction(() => Settings.SetupCompleted = true);
            },
            command,
            targetCoreType);

    public async Task ExecuteSettingsUiCommandAsync(JsonElement root, Action uiAction)
    {
        _ = await _coreLifecycle.ExecuteConfigurationCommandAsync(
            async () =>
            {
                await SaveSettingsWithinLeaseAsync(root, showMessage: false);
                uiAction();
            },
            CoreConfigurationCommand.SaveOnly);
    }

    internal static void ApplyCredentialEdit(AppSettings settings, JsonElement root, bool isSingBox)
    {
        var failed = isSingBox ? settings.SingBoxSecretDecryptionFailed : settings.SecretDecryptionFailed;
        var key = isSingBox ? "singBoxSecret" : "mihomoSecret";
        var replacementKey = isSingBox ? "replaceSingBoxSecret" : "replaceMihomoSecret";
        if (failed && !HostBridgeJson.GetBool(root, replacementKey, false)) return;
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return;
        settings.ReplaceSecret(isSingBox, value.GetString() ?? "");
    }

    internal static void ApplyCoreProfileEdits(AppSettings settings, JsonElement root)
    {
        settings.CoreType = AppSettings.NormalizeCoreType(HostBridgeJson.GetString(root, "coreType", settings.CoreType));
        settings.CorePath = HostBridgeJson.GetString(root, "mihomoCorePath", settings.CorePath).Trim();
        settings.ConfigPath = HostBridgeJson.GetString(root, "mihomoConfigPath", settings.ConfigPath).Trim();
        settings.DashboardApiUrl = HostBridgeJson.GetString(root, "mihomoApiUrl", settings.DashboardApiUrl).Trim();
        ApplyCredentialEdit(settings, root, isSingBox: false);
        settings.SingBoxCorePath = HostBridgeJson.GetString(root, "singBoxCorePath", settings.SingBoxCorePath).Trim();
        settings.SingBoxConfigPath = HostBridgeJson.GetString(root, "singBoxConfigPath", settings.SingBoxConfigPath).Trim();
        settings.SingBoxApiUrl = HostBridgeJson.GetString(root, "singBoxApiUrl", settings.SingBoxApiUrl).Trim();
        ApplyCredentialEdit(settings, root, isSingBox: true);
    }

    private async Task SaveSettingsWithinLeaseAsync(JsonElement root, bool showMessage)
    {
        var previousCoreIdentity = GetActiveCoreIdentity();
        var previousAutostart = Settings.Autostart;
        var requestedAutostart = root.TryGetProperty("autostart", out var autostart)
            && autostart.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? autostart.GetBoolean()
            : previousAutostart;
        HostSettingsTransaction.Execute(Settings, () =>
        {
            ApplyCoreProfileEdits(Settings, root);
            Settings.StartCoreOnLaunch = HostBridgeJson.GetBool(root, "startCoreOnLaunch", Settings.StartCoreOnLaunch);
            Settings.MinimizeToTray = HostBridgeJson.GetBool(root, "minimizeToTray", Settings.MinimizeToTray);
            Settings.LightweightMode = HostBridgeJson.GetBool(root, "lightweightMode", Settings.LightweightMode);
            Settings.SetupCompleted = HostBridgeJson.GetBool(root, "setupCompleted", Settings.SetupCompleted);
            // Persist host fields with the last verified system autostart state. The
            // requested autostart value commits only after the OS operation succeeds.
            Settings.Autostart = previousAutostart;
        }, Settings.Save);
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

    public Task ReconcileAutostartAsync() => RunBackgroundTask(_ => ReconcileAutostartOwnedAsync());

    private async Task ReconcileAutostartOwnedAsync()
    {
        using var configurationChange = _coreLifecycle.TryEnterConfigurationChange();
        if (configurationChange is null) return;
        try
        {
            if (_backgroundTasks.IsClosing) return;
            var hasLegacyEntry = AutostartManager.HasCurrentLegacyRunEntry();
            var status = AutostartManager.QueryStatus();
            if (hasLegacyEntry && !Settings.Autostart)
            {
                ExecuteHostSettingsTransaction(() => Settings.Autostart = true);
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

        Settings.ExecuteSynchronized(() =>
        {
            var previous = Settings.DashboardSettings;
            Settings.DashboardSettings = dashboardSettings;
            try
            {
                Settings.Save();
            }
            catch
            {
                Settings.DashboardSettings = previous;
                throw;
            }
        });
    }

    public void SetActiveCorePath(string path)
    {
        using var configurationChange = _coreLifecycle.TryEnterConfigurationChange()
            ?? throw new InvalidOperationException("内核操作正在进行，请稍后重试。");
        ExecuteHostSettingsTransaction(() => Settings.ActiveCorePath = path);
        PublishStateChanged();
    }

    public void SetActiveConfigPath(string path)
    {
        using var configurationChange = _coreLifecycle.TryEnterConfigurationChange()
            ?? throw new InvalidOperationException("内核操作正在进行，请稍后重试。");
        ExecuteHostSettingsTransaction(() => Settings.ActiveConfigPath = path);
        RefreshIconCache();
        PublishStateChanged();
    }

    public void RefreshIconCache()
    {
        if (Settings.IsSingBox || _backgroundTasks.IsClosing) return;
        var generation = Interlocked.Increment(ref _iconRefreshGeneration);
        _ = RunBackgroundTask(_ => RefreshIconCacheAsync(Settings.ConfigPath, generation));
    }

    private bool IsCurrentIconRefresh(long generation) =>
        !_backgroundTasks.IsClosing && generation == Interlocked.Read(ref _iconRefreshGeneration);

    private async Task LoadExistingIconCacheAsync(string configPath, long generation)
    {
        try { await _iconCache.LoadExistingAsync(configPath, _backgroundTasks.Token, () => IsCurrentIconRefresh(generation)); }
        catch (OperationCanceledException) when (_backgroundTasks.IsClosing) { }
        catch (Exception ex) { HostOperationLogger.Error("icon-cache", "Existing icon cache scan failed.", ex); }
    }

    private async Task RefreshIconCacheAsync(string configPath, long generation)
    {
        try { await _iconCache.RefreshAsync(configPath, _backgroundTasks.Token, () => IsCurrentIconRefresh(generation)); }
        catch (OperationCanceledException) when (_backgroundTasks.IsClosing) { }
        catch (Exception ex) { HostOperationLogger.Error("icon-cache", "Icon cache refresh failed.", ex); }
    }

    private Task RunBackgroundTask(Func<CancellationToken, Task> operation) => _backgroundTasks.Run(operation);

    public void ShowNotice(string message)
    {
        if (_disposed) return;
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
        Interlocked.Increment(ref _coreRuntimeGeneration);
        _loadingCoreVersionKey = "";
        _cachedCoreVersionKey = "";
        ResetCoreUpdateState(publish: false);
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
        if (_disposed) return;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ExecuteHostSettingsTransaction(Action mutation) =>
        HostSettingsTransaction.Execute(Settings, mutation, Settings.Save);

    private string GetActiveCoreIdentity() => BuildCoreIdentity(
        Settings.CoreType,
        Settings.ActiveCorePath,
        _core.ProcessId,
        Interlocked.Read(ref _coreRuntimeGeneration));

    internal static string BuildCoreIdentity(string coreType, string corePath, int? processId, long generation) =>
        $"{coreType}|{corePath}|{processId?.ToString() ?? "stopped"}|{generation}";

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
            var key = $"{fullPath}|{lastWrite}|{Settings.CoreType}|{_core.ProcessId?.ToString() ?? "stopped"}|{Interlocked.Read(ref _coreRuntimeGeneration)}";
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
        _coreVersionLoadTask = RunBackgroundTask(token => ReadCoreVersionOwnedAsync(key, corePath, isSingBox, token));
    }

    private async Task ReadCoreVersionOwnedAsync(
        string versionGeneration,
        string corePath,
        bool isSingBox,
        CancellationToken cancellationToken)
    {
        var result = await Task.Run(
            () => ReadCoreVersion(corePath, isSingBox, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var versionChanged = false;
        if (!_backgroundTasks.IsClosing
            && string.Equals(versionGeneration, _loadingCoreVersionKey, StringComparison.OrdinalIgnoreCase))
        {
            versionChanged = !string.IsNullOrWhiteSpace(_cachedCoreVersion)
                && !string.IsNullOrWhiteSpace(result)
                && !string.Equals(_cachedCoreVersion, result, StringComparison.OrdinalIgnoreCase);
            _cachedCoreVersionKey = versionGeneration;
            _cachedCoreVersion = result;
        }

        if (string.Equals(versionGeneration, _loadingCoreVersionKey, StringComparison.OrdinalIgnoreCase))
            _loadingCoreVersionKey = "";

        if (_backgroundTasks.IsClosing) return;
        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
        if (versionChanged)
        {
            ResetCoreUpdateState();
            _ = CheckForCoreUpdateAsync();
        }
    }

    private static string ReadCoreVersion(string corePath, bool isSingBox, CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            process.Start();
            using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));
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
        await _autostartGate.WaitAsync(_backgroundTasks.Token);
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

            // The operation changes OS state and cannot be safely abandoned after it
            // starts. Commit its verified result even if shutdown began while waiting;
            // the configuration lease prevents a newer request from being overwritten.
            if (result.Success)
            {
                ExecuteHostSettingsTransaction(() => Settings.Autostart = enabled);
                PublishStateChanged();
                if (isMigration)
                {
                    ShowNotice(enabled
                        ? "开机自启已迁移到计划任务。"
                        : "已清理旧的开机自启配置。");
                }
                return true;
            }

            ExecuteHostSettingsTransaction(() => Settings.Autostart = rollbackValue);
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

    public Task ShutdownAsync()
    {
        lock (_shutdownSync)
        {
            return _shutdownTask ??= ShutdownOwnedAsync();
        }
    }

    private async Task ShutdownOwnedAsync()
    {
        _disposed = true;
        _coreLifecycle.BeginShutdown();
        var lifecycleWait = _coreLifecycle.WaitForShutdownAsync(ShutdownWaitTimeout);
        var backgroundWait = _backgroundTasks.StopAndWaitAsync(ShutdownWaitTimeout);
        await Task.WhenAll(lifecycleWait, backgroundWait);
        var lifecycleStopped = lifecycleWait.Result;
        var backgroundStopped = backgroundWait.Result;
        if (!lifecycleStopped || !backgroundStopped)
            HostOperationLogger.Error("shutdown", "Timed out waiting for owned background work; disposal will continue without interrupting an in-progress file commit.", new TimeoutException());
        // A timed-out operation may still register cancellation callbacks after an
        // await. Keep the tracker/token source alive in that fallback case.
        DisposeResources(disposeTaskTracker: backgroundStopped);
    }

    private void DisposeResources(bool disposeTaskTracker)
    {
        lock (_shutdownSync)
        {
            if (_resourcesDisposed) return;
            _resourcesDisposed = true;
        }

        _core.StatusChanged -= OnCoreStatusChanged;
        _core.LogReceived -= OnCoreLogReceived;
        _iconCache.CacheChanged -= OnIconCacheChanged;
        var resources = new List<(string Name, Action Dispose)>
        {
            ("Core lifecycle", _coreLifecycle.Dispose),
            ("Core", _core.Dispose),
            ("Dashboard server", _dashboardServer.Dispose)
        };
        if (disposeTaskTracker)
            resources.Add(("Host background task tracker", _backgroundTasks.Dispose));
        ShutdownResourceDisposer.DisposeAll([.. resources]);
    }

    public void Dispose()
    {
        _disposed = true;
        _coreLifecycle.BeginShutdown();
        // Normal application exit awaits ShutdownAsync while the WinForms message
        // loop is alive. Dispose is only an idempotent fallback and must never
        // synchronously block the UI thread waiting for captured continuations.
        DisposeResources(disposeTaskTracker: _shutdownTask?.IsCompleted == true);
    }
}

internal sealed record HostRelaunchRequest(bool StartCore, bool StartMinimized, bool ElevatedRestart);

internal sealed record HostMessageRequest(string Title, string Message, MessageBoxIcon Icon);
