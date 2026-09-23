namespace Dashboard;

internal sealed class CoreLifecycleController : IDisposable
{
    private static readonly TimeSpan ApiProbeRequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ApiProbeTotalTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient ApiProbeClient = new();

    private readonly AppSettings _settings;
    private readonly CoreProcessManager _core;
    private readonly CoreLifecycleServices _services;
    private readonly CoreOperationGate _operations = new();
    private readonly ShutdownTaskTracker _tasks = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _shutdownSync = new();
    private readonly object _probeSync = new();
    private Task? _drainTask;
    private CancellationTokenSource? _probeCancellation;
    private long _probeGeneration;
    private bool _shutdownBegun;
    private bool _shutdownResourcesDisposed;
    private bool _elevatedRetryPending;

    public IDisposable? TryEnterConfigurationChange()
    {
        var lease = _operations.TryEnter();
        if (lease is not null) CancelProbe();
        return lease;
    }

    private void CancelProbe()
    {
        lock (_probeSync)
        {
            Interlocked.Increment(ref _probeGeneration);
            _probeCancellation?.Cancel();
        }
    }

    public void BeginShutdown()
    {
        lock (_shutdownSync)
        {
            if (_shutdownBegun) return;
            _shutdownBegun = true;
            _operations.Close();
            CancelProbe();
            _shutdown.Cancel();
        }
    }

    public async Task<bool> WaitForShutdownAsync(TimeSpan timeout)
    {
        BeginShutdown();
        Task drainTask;
        lock (_shutdownSync)
        {
            if (_shutdownResourcesDisposed) return true;
            drainTask = _drainTask ??= DrainAndDisposeAsync();
        }

        try
        {
            await drainTask.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async Task DrainAndDisposeAsync()
    {
        // Ensure disposal cannot run inline while the caller holds _shutdownSync.
        await Task.Yield();
        var operationWait = _operations.WaitForIdleAsync(Timeout.InfiniteTimeSpan);
        var taskWait = _tasks.StopAndWaitAsync(Timeout.InfiniteTimeSpan);
        await Task.WhenAll(operationWait, taskWait);

        lock (_shutdownSync)
        {
            if (_shutdownResourcesDisposed) return;
            _tasks.Dispose();
            _shutdown.Dispose();
            _shutdownResourcesDisposed = true;
        }
    }

    public void Dispose()
    {
        BeginShutdown();
        _ = WaitForShutdownAsync(TimeSpan.Zero);
    }

    private IDisposable? EnterOperation()
    {
        var lease = _operations.TryEnter();
        if (lease is null && !_operations.IsClosing) _services.ShowNotice("内核操作正在进行，请稍后重试。");
        return lease;
    }

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
        using var operation = EnterOperation();
        if (operation is null) return;
        StartInternal(showTrayNotification);
    }

    private void StartInternal(bool showTrayNotification = false)
    {
        if (_operations.IsClosing) return;
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
            _ = _tasks.Run(_ => WaitForApiAndNotifyAsync());
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
        using var operation = EnterOperation();
        if (operation is null) return;
        CancelProbe();
        ResetTunRetry();
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
        using var operation = EnterOperation();
        if (operation is null) return false;
        return RestartInternal(showTrayNotification);
    }

    private bool RestartInternal(bool showTrayNotification = false)
    {
        CancelProbe();
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

            StartInternal();
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

    public Task SwitchAsync(string targetCoreType) => _tasks.Run(async _ =>
    {
        using var operation = EnterOperation();
        if (operation is null) return;
        await SwitchInternalAsync(targetCoreType);
    });

    private async Task SwitchInternalAsync(string targetCoreType)
    {
        CancelProbe();

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
                await _services.DelayAsync(TimeSpan.FromMilliseconds(600), _shutdown.Token);
            }

            _shutdown.Token.ThrowIfCancellationRequested();
            HostSettingsTransaction.Execute(
                _settings,
                () => _settings.CoreType = targetCoreType,
                _settings.Save);
            _services.RefreshIconCache();
            StartInternal();

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
            if (!_operations.IsClosing) _services.PublishState();
        }
    }

    public Task UpgradeAsync() => _tasks.Run(async _ =>
    {
        using var operation = EnterOperation();
        if (operation is null) return;
        await UpgradeInternalAsync();
    });

    public async Task<ConfigurationCommandResult> ExecuteConfigurationCommandAsync(
        Func<Task> saveAsync,
        CoreConfigurationCommand command,
        string targetCoreType = "")
    {
        var result = ConfigurationCommandResult.Rejected;
        await _tasks.Run(async _ =>
        {
            using var operation = EnterOperation();
            if (operation is null) return;
            CancelProbe();
            await saveAsync();
            switch (command)
            {
                case CoreConfigurationCommand.SaveOnly:
                    break;
                case CoreConfigurationCommand.Start:
                    StartInternal();
                    break;
                case CoreConfigurationCommand.Restart:
                    RestartInternal();
                    break;
                case CoreConfigurationCommand.Switch:
                    await SwitchInternalAsync(targetCoreType);
                    break;
                case CoreConfigurationCommand.Upgrade:
                    await UpgradeInternalAsync();
                    break;
            }
            result = ConfigurationCommandResult.Executed;
        });
        return result;
    }

    private async Task UpgradeInternalAsync()
    {
        if (_settings.ActiveSecretDecryptionFailed)
        {
            _services.ShowNotice("Secret 无法解密，请在 Core 页面重新填写并确认替换后再升级。");
            return;
        }
        var isSingBox = _settings.IsSingBox;
        var corePath = _settings.ActiveCorePath;
        var apiUrl = _settings.ActiveDashboardApiUrl;
        var secret = _settings.ActiveSecret;
        if (!isSingBox && !_core.IsRunning)
        {
            _services.ShowNotice("请先启动 mihomo 内核，再执行升级。");
            return;
        }

        var wasRunning = _core.IsRunning;
        var stoppedForUpgrade = false;
        IsUpgradeInProgress = true;
        _services.PublishState();
        _services.ShowNotice(isSingBox
            ? "正在升级 sing-box 内核，请稍候。"
            : "正在升级内核，请稍候。");

        try
        {
            if (!isSingBox)
            {
                var mihomoResult = await _services.UpgradeMihomoAsync(apiUrl, secret, _shutdown.Token);
                if (_operations.IsClosing) return;
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

            var result = await _services.UpgradeSingBoxAsync(
                corePath,
                StopRunningCoreForUpgrade,
                _shutdown.Token);

            void StopRunningCoreForUpgrade()
            {
                _shutdown.Token.ThrowIfCancellationRequested();
                if (wasRunning && _core.IsRunning)
                {
                    _core.Stop(TimeSpan.FromSeconds(8));
                    stoppedForUpgrade = true;
                }
            }

            if (_operations.IsClosing) return;
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
                StartInternal();
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
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
            if (stoppedForUpgrade && !_core.IsRunning && !_operations.IsClosing)
            {
                StartInternal();
            }
        }
        finally
        {
            IsUpgradeInProgress = false;
            if (!_operations.IsClosing) _services.PublishState();
        }
    }

    public void ObserveLogEntry(string? logEntry)
    {
        if (_operations.IsClosing || !_elevatedRetryPending || !IsTunPermissionFailure(logEntry))
        {
            return;
        }

        using var operation = EnterOperation();
        if (operation is null) return;
        ResetTunRetry();
        CancelProbe();
        try
        {
            _core.Stop();
            if (!_operations.IsClosing)
                _services.RelaunchAsAdministrator(true, _services.ShouldKeepMinimizedForRelaunch(), true);
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("core", "Failed to stop core before elevated retry.", ex);
        }
    }

    private async Task WaitForApiAndNotifyAsync()
    {
        if (_settings.ActiveSecretDecryptionFailed)
        {
            _services.ShowNotice("Secret 无法解密，API 连接已暂停。请在 Core 页面重新填写并确认替换。");
            _services.PublishState();
            return;
        }
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        long generation;
        lock (_probeSync)
        {
            CancelProbe();
            generation = Interlocked.Read(ref _probeGeneration);
            _probeCancellation = probe;
        }
        var processId = _core.ProcessId;
        bool IsCurrent() => !_operations.IsClosing && generation == Interlocked.Read(ref _probeGeneration)
            && processId == _core.ProcessId;
        var apiUrl = _settings.ActiveDashboardApiUrl;
        var secret = _settings.ActiveSecret;
        bool reachable;
        try
        {
            reachable = await CoreApiProbe.WaitAsync(ApiProbeClient, apiUrl, secret,
                ApiProbeRequestTimeout, ApiProbeTotalTimeout, TimeSpan.FromMilliseconds(500), probe.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            if (IsCurrent()) HostOperationLogger.Error("core", "Core API probe failed.", ex);
            return;
        }
        finally
        {
            lock (_probeSync)
            {
                if (ReferenceEquals(_probeCancellation, probe)) _probeCancellation = null;
            }
        }
        _services.RunOnUiThread(() =>
        {
            if (!IsCurrent()) return;
            ResetTunRetry();
            if (reachable)
            {
                _services.RefreshIconCache();
                _services.PublishState();
            }
            else _services.ShowNotice($"内核已启动，但无法连接 API：{apiUrl}");
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

internal enum CoreConfigurationCommand
{
    SaveOnly,
    Start,
    Restart,
    Switch,
    Upgrade
}

internal enum ConfigurationCommandResult
{
    Executed,
    Rejected
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
    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;
    public Func<string, string, CancellationToken, Task<MihomoApiUpgradeResult>> UpgradeMihomoAsync { get; init; } =
        MihomoApiUpdater.UpgradeAsync;
    public Func<string, Action?, CancellationToken, Task<CoreUpgradeResult>> UpgradeSingBoxAsync { get; init; } =
        (path, beforeReplace, token) => SingBoxUpdater.UpgradeLatestAsync(path, beforeReplace, token);
}
