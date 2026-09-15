using System.Net.Http.Headers;

namespace Dashboard;

internal sealed class CoreLifecycleController
{
    // Probe client is shared; the secret goes on each request so one instance
    // serves both cores. Callers must not dispose it.
    private static readonly HttpClient ApiProbeClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly AppSettings _settings;
    private readonly CoreProcessManager _core;
    private readonly CoreLifecycleServices _services;
    private bool _elevatedRetryPending;

    public CoreLifecycleController(
        AppSettings settings,
        CoreProcessManager core,
        CoreLifecycleServices services)
    {
        _settings = settings;
        _core = core;
        _services = services;
    }

    public bool IsUpgradeInProgress { get; private set; }
    public bool IsSwitchInProgress { get; private set; }

    public void Start(bool showTrayNotification = false)
    {
        try
        {
            if (!_core.IsRunning && !_services.IsRunningAsAdministrator())
            {
                ResetTunRetry();
                _services.RelaunchAsAdministrator(
                    true,
                    _services.ShouldKeepMinimizedForRelaunch(),
                    true);
                return;
            }

            _elevatedRetryPending = !_services.IsRunningAsAdministrator();
            _core.Start(_settings);
            if (showTrayNotification)
            {
                _services.ShowTrayNotification("内核已启动");
            }

            _services.RefreshIconCache();
            _ = WaitForApiAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ResetTunRetry();
            HostOperationLogger.Error("core", "Failed to start core.", ex);
            _services.ShowMessage("启动失败", ex.Message, MessageBoxIcon.Error);
        }
        finally
        {
            _services.PublishState();
        }
    }

    public void Stop()
    {
        Stop(showTrayNotification: false);
    }

    public void Stop(bool showTrayNotification)
    {
        var wasRunning = _core.IsRunning;
        try
        {
            _core.Stop();
            if (showTrayNotification && wasRunning)
            {
                _services.ShowTrayNotification("内核已关闭");
            }
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("core", "Failed to stop core.", ex);
            _services.ShowMessage("停止失败", ex.Message, MessageBoxIcon.Error);
        }
        finally
        {
            _services.PublishState();
        }
    }

    public bool Restart(bool showTrayNotification = false)
    {
        var beforeProcessId = _core.ProcessId;
        try
        {
            if (!_services.IsRunningAsAdministrator())
            {
                HostOperationLogger.Info(
                    "core",
                    $"Restart requires elevation; requesting elevated Dashboard restart. currentPid={beforeProcessId?.ToString() ?? "none"}.");
                _services.RelaunchAsAdministrator(
                    true,
                    _services.ShouldKeepMinimizedForRelaunch(),
                    true);
                return false;
            }

            if (_core.IsRunning)
            {
                _core.Stop();
            }

            Start();
            var started = _core.IsRunning;
            HostOperationLogger.Info(
                "core",
                $"Core restart {(started ? "started" : "failed to start")}. previousPid={beforeProcessId?.ToString() ?? "none"}, currentPid={_core.ProcessId?.ToString() ?? "none"}.");
            if (!started)
            {
                return false;
            }

            _services.ShowNotice("内核已重启。");
            if (showTrayNotification)
            {
                _services.ShowTrayNotification("内核已重启");
            }

            return true;
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error(
                "core",
                $"Failed to restart core. previousPid={beforeProcessId?.ToString() ?? "none"}.",
                ex);
            _services.ShowMessage("重启失败", ex.Message, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            _services.PublishState();
        }
    }

    public async Task SwitchAsync(string targetCoreType)
    {
        if (IsSwitchInProgress || IsUpgradeInProgress)
        {
            return;
        }

        var fallbackCoreType = _settings.IsSingBox
            ? AppSettings.CoreTypeMihomo
            : AppSettings.CoreTypeSingBox;
        targetCoreType = AppSettings.NormalizeCoreType(
            string.IsNullOrWhiteSpace(targetCoreType) ? fallbackCoreType : targetCoreType);
        var targetTitle = AppSettings.CoreTitleFor(targetCoreType);

        IsSwitchInProgress = true;
        _services.PublishState();
        _services.ShowNotice($"正在切换到 {targetTitle}。");

        try
        {
            if (_core.IsRunning)
            {
                _core.Stop(TimeSpan.FromSeconds(8));
                await Task.Delay(600);
            }

            _settings.CoreType = targetCoreType;
            _settings.Save();
            _services.RefreshIconCache();
            Start();

            if (_core.IsRunning)
            {
                _services.ShowNotice($"已切换到 {targetTitle}。");
            }
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("core", "Failed to switch core.", ex);
            _services.ShowNotice($"切换内核失败：{ex.Message}");
        }
        finally
        {
            IsSwitchInProgress = false;
            _services.PublishState();
        }
    }

    public async Task UpgradeAsync()
    {
        if (IsUpgradeInProgress)
        {
            return;
        }

        if (!_settings.IsSingBox && !_core.IsRunning)
        {
            _services.ShowNotice("请先启动 mihomo 内核，再执行升级。");
            return;
        }

        var wasRunning = _core.IsRunning;
        var stoppedForUpgrade = false;
        IsUpgradeInProgress = true;
        _services.PublishState();
        _services.ShowNotice(_settings.IsSingBox
            ? "正在升级 sing-box 内核，请稍候。"
            : "正在升级内核，请稍候。");

        try
        {
            if (!_settings.IsSingBox)
            {
                var mihomoResult = await MihomoApiUpdater.UpgradeAsync(_settings.DashboardApiUrl, _settings.Secret);
                if (mihomoResult.IsAlreadyLatest)
                {
                    var versionText = string.IsNullOrWhiteSpace(mihomoResult.Version)
                        ? ""
                        : $"（{mihomoResult.Version}）";
                    _services.ShowNotice($"当前已是最新版本{versionText}。");
                    return;
                }

                _services.ShowNotice("mihomo 内核升级成功。");
                return;
            }

            var result = await SingBoxUpdater.UpgradeLatestAsync(
                _settings.SingBoxCorePath,
                beforeReplace: StopRunningCoreForUpgrade);

            void StopRunningCoreForUpgrade()
            {
                if (wasRunning && _core.IsRunning)
                {
                    _core.Stop(TimeSpan.FromSeconds(8));
                    stoppedForUpgrade = true;
                }
            }

            if (result.IsAlreadyLatest)
            {
                HostOperationLogger.Info("upgrade", $"Core is already latest: {result.Version}.");
                _services.ShowNotice($"当前内核已经是最新版本（{result.Version}）。");
                return;
            }

            _services.ShowNotice($"内核已升级到 {result.Version}。");
            HostOperationLogger.Info("upgrade", $"Core upgraded to {result.Version} from asset {result.AssetName}. Backup: {result.BackupPath}");
            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                HostOperationLogger.Info("upgrade", result.Warning);
                _services.ShowNotice(result.Warning);
            }

            if (stoppedForUpgrade)
            {
                Start();
            }
        }
        catch (MihomoApiUpgradeException ex)
        {
            HostOperationLogger.Error("upgrade", "Failed to upgrade mihomo through its API.", ex);
            _services.ShowNotice(ex.UserMessage);
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("upgrade", "Failed to upgrade core.", ex);
            var message = ex switch
            {
                HttpRequestException => "升级失败：无法连接 mihomo API，请确认内核正在运行。",
                TaskCanceledException => "升级失败：请求超时，请稍后重试。",
                _ => "升级失败：发生意外错误，详情请查看 upgrade.log。"
            };
            _services.ShowNotice(message);
            if (stoppedForUpgrade && !_core.IsRunning)
            {
                Start();
            }
        }
        finally
        {
            IsUpgradeInProgress = false;
            _services.PublishState();
        }
    }

    public void ObserveLogEntry(string? logEntry)
    {
        if (!_elevatedRetryPending || !IsTunPermissionFailure(logEntry))
        {
            return;
        }

        ResetTunRetry();
        Stop();
        _services.RelaunchAsAdministrator(
            true,
            _services.ShouldKeepMinimizedForRelaunch(),
            true);
    }

    private async Task WaitForApiAndNotifyAsync()
    {
        var apiUrl = _settings.ActiveDashboardApiUrl;
        var secret = _settings.ActiveSecret;
        var endpoint = $"{apiUrl.TrimEnd('/')}/version";
        Exception? lastException = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                }

                using var response = await ApiProbeClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    _services.RunOnUiThread(() =>
                    {
                        ResetTunRetry();
                        _services.RefreshIconCache();
                        _services.PublishState();
                    });
                    return;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(500);
        }

        _services.RunOnUiThread(() =>
        {
            ResetTunRetry();
            if (lastException is not null)
            {
                HostOperationLogger.Error("core", $"Core API did not become reachable: {endpoint}", lastException);
            }
            else
            {
                HostOperationLogger.Info("core", $"Core API did not become reachable: {endpoint}");
            }

            _services.ShowNotice($"内核已启动，但无法连接 API：{apiUrl}");
        });
    }

    private void ResetTunRetry()
    {
        _elevatedRetryPending = false;
    }

    private static bool IsTunPermissionFailure(string? logEntry)
    {
        return !string.IsNullOrWhiteSpace(logEntry)
            && (logEntry.Contains("Start TUN listening error", StringComparison.OrdinalIgnoreCase)
                || logEntry.Contains("configure tun interface: Access is denied", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class CoreLifecycleServices
{
    public required Func<bool> IsRunningAsAdministrator { get; init; }
    public required Func<bool> ShouldKeepMinimizedForRelaunch { get; init; }
    public required Action<bool, bool, bool> RelaunchAsAdministrator { get; init; }
    public required Action<string> ShowNotice { get; init; }
    public required Action PublishState { get; init; }
    public required Action RefreshIconCache { get; init; }
    public required Action<string> ShowTrayNotification { get; init; }
    public required Action<string, string, MessageBoxIcon> ShowMessage { get; init; }
    public required Action<Action> RunOnUiThread { get; init; }
}
