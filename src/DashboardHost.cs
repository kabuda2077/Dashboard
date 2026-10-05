using System.Security.Principal;
using System.Text.Json;

namespace Dashboard;

internal sealed class DashboardHost : IDisposable
{
    internal const string MihomoRepositoryUrl = "https://github.com/MetaCubeX/mihomo";
    internal const string SingBoxRepositoryUrl = "https://github.com/reF1nd/sing-box";
    private readonly SettingsStore _settings;
    private readonly CoreProcessManager _core = new();
    private readonly CoreLifecycleController _lifecycle;
    private readonly Func<string, CoreKind, CancellationToken, Task<string>> _readCoreVersion;
    private readonly ProxyGroupIconCache _icons;
    private readonly DashboardServer _server;
    private readonly ShutdownTaskTracker _tasks = new();
    private readonly SemaphoreSlim _autostartGate = new(1, 1);
    private readonly SemaphoreSlim _appUpdateGate = new(1, 1);
    private readonly SemaphoreSlim _coreUpdateGate = new(1, 1);
    private readonly object _metadataSync = new();
    private readonly Dictionary<CoreKind, Metadata> _metadata = new();
    private Task? _metadataTask;
    private bool _metadataAgain;
    private bool _metadataForce;
    private int _coreUpdatePending;
    private long _iconGeneration;
    private CancellationTokenSource? _iconCancellation;
    private long _lastRuntimeEpoch;
    private CoreProfiles _lastProfiles;
    private CoreKind _lastActiveCoreKind;
    private Task? _shutdownTask;
    private bool _disposed;
    private string _iconConfigKey = "";

    private sealed record Metadata(string Key, string RawVersion, string DisplayVersion, bool? Tun, bool NeedsRetry);

    public DashboardHost(string? dataDirectory = null, string? dashboardDirectory = null, bool ephemeralPort = false,
        Func<bool>? isAdministrator = null, int testPort = 0,
        Func<string, CoreKind, CancellationToken, Task<string>>? readCoreVersion = null)
    {
        _readCoreVersion = readCoreVersion ?? CoreVersionReader.ReadAsync;
        _settings = new SettingsStore(dataDirectory);
        _lastProfiles = Settings.Profiles;
        _lastActiveCoreKind = Settings.ActiveCoreKind;
        _icons = new ProxyGroupIconCache(Path.Combine(dataDirectory ?? AppSettings.AppDirectory, "resources", "icon-cache"));
        _server = new DashboardServer(dashboardDirectory ?? Path.Combine(AppSettings.ResourceDirectory, "dashboard"), _icons.CacheDirectory);
        DashboardUri = ephemeralPort ? _server.StartForTests(testPort) : _server.Start();
        _lifecycle = new(_settings, _core, isAdministrator);
        _settings.Changed += OnSettingsChanged;
        _lifecycle.Changed += OnRuntimeChanged;
        _core.LogReceived += OnLog;
        _icons.CacheChanged += OnIcons;
        _ = RefreshMetadataAsync();
        RefreshIconCache();
    }

    public AppSettings Settings => _settings.Current;
    internal string DataDirectory => Path.GetDirectoryName(_settings.FilePath)!;
    public Uri DashboardUri { get; }
    public bool IsRunning => _core.IsRunning;
    public int? ProcessId => _core.ProcessId;
    public long RuntimeEpoch => _lifecycle.Epoch;
    public bool IsUpgradeInProgress => _lifecycle.IsUpgradeInProgress;
    public bool IsSwitchInProgress => _lifecycle.IsSwitchInProgress;
    public bool IsAutostartUpdating { get; private set; }
    public bool IsAppUpdateChecking { get; private set; }
    public bool AppUpdateAvailable { get; private set; }
    public string LatestAppVersion { get; private set; } = "";
    public bool IsCoreUpdateChecking { get; private set; }
    public bool CoreUpdateAvailable { get; private set; }
    public string LatestCoreVersion { get; private set; } = "";

    public event EventHandler? StateChanged;
    public event EventHandler? RuntimeStateChanged;
    public event EventHandler<string>? LogReceived;
    public event EventHandler? IconCacheChanged;
    public event EventHandler<string>? NoticeRequested;
    public event EventHandler<object>? AppUpdateResultRequested;
    public event EventHandler<string>? TrayNotificationRequested;
    public event EventHandler<HostMessageRequest>? MessageRequested;
    public event EventHandler<HostRelaunchRequest>? RelaunchRequested;

    public DashboardRuntimeState BuildRuntimeState(bool isWindowMaximized)
    {
        var kind = _lifecycle.Kind;
        var connection = _lifecycle.Connection;
        Metadata? metadata;
        lock (_metadataSync) _metadata.TryGetValue(kind, out metadata);
        var apiVersion = _lifecycle.ApiVersion;
        return new()
        {
            IsRunning = _core.IsRunning,
            ProcessId = _core.ProcessId,
            CoreType = kind,
            RuntimeEpoch = _lifecycle.Epoch,
            CoreTitle = AppSettings.CoreTitleFor(kind),
            CoreVersion = apiVersion.Length > 0 ? apiVersion : metadata?.DisplayVersion ?? "",
            ApiStatus = _lifecycle.ApiStatus,
            ApiUrl = connection.ApiUrl,
            Secret = connection.Secret,
            SecretDecryptionFailed = connection.SecretDecryptionFailed,
            Operation = _lifecycle.Operation,
            RequiresRestart = _lifecycle.RequiresRestart,
            CanUpgradeCore = !_lifecycle.IsBusy && !_lifecycle.RequiresRestart && !connection.SecretDecryptionFailed
                && (kind == CoreKind.SingBox || (_core.IsRunning && _lifecycle.ApiStatus == "ready")),
            IsWindowMaximized = isWindowMaximized,
            ReadOnlyTunEnabled = kind == CoreKind.SingBox ? metadata?.Tun : null
        };
    }

    public DashboardState BuildState(bool isWindowMaximized)
    {
        var settings = Settings;
        return new()
        {
            Runtime = BuildRuntimeState(isWindowMaximized),
            Profiles = Enum.GetValues<CoreKind>().ToDictionary(AppSettings.WireKind, kind =>
            {
                var profile = settings.Profile(kind);
                return new CoreProfileState(profile.Revision, profile.ExePath, profile.ConfigPath, profile.ApiUrl, profile.Secret, profile.SecretDecryptionFailed);
            }),
            DesktopOptions = settings.DesktopOptions,
            SetupCompleted = settings.SetupCompleted,
            IsAutostartUpdating = IsAutostartUpdating,
            AppVersion = DashboardVersion.Current,
            LatestAppVersion = LatestAppVersion,
            IsAppUpdateChecking = IsAppUpdateChecking,
            AppUpdateAvailable = AppUpdateAvailable,
            LatestCoreVersion = LatestCoreVersion,
            IsCoreUpdateChecking = IsCoreUpdateChecking,
            CoreUpdateAvailable = CoreUpdateAvailable,
            LogText = _core.GetLogTail(8000),
            DroppedLogEntries = HostOperationLogger.DroppedEntries,
            LogWriteFailures = HostOperationLogger.WriteFailures,
            IconCacheMap = GetIconCacheMap()
        };
    }

    public IReadOnlyDictionary<string, string> Preferences => Settings.DashboardPreferences;
    public IReadOnlyDictionary<string, string> GetIconCacheMap() => _icons.GetDashboardMap(DashboardUri);

    public async Task<CommandResult> ExecuteAsync(HostRequest request)
    {
        if (_disposed || _tasks.IsClosing) return CommandResult.Rejected("closing");
        if (request.Type is "saveProfile" or "start" or "restart" or "switchCore" or "stop" or "upgradeCore" or "completeSetup")
        {
            var result = await _lifecycle.ExecuteAsync(request).ConfigureAwait(false);
            if (result.Status == "completed" && request.Type is ("start" or "restart" or "switchCore" or "upgradeCore" or "saveProfile"))
            {
                // Metadata is a separate owner, not a prerequisite for acknowledging a committed command.
                _ = RefreshMetadataAsync();
                if (!_disposed) _ = CheckForCoreUpdateAsync();
            }
            return result;
        }
        var response = CommandResult.Rejected("closing");
        await _tasks.Run(async token =>
        {
            try
            {
                switch (request.Type)
                {
                    case "saveDashboardPreferences":
                        await _settings.SavePreferencesAsync(request.Preferences!, token).ConfigureAwait(false);
                        response = CommandResult.Completed("preferencesSaved", saved: true); break;
                    case "setDesktopOption":
                        response = request.Option == "autostart"
                            ? await SetAutostartAsync(request.Value!.Value, token).ConfigureAwait(false)
                            : await SaveOptionAsync(request.Option!, request.Value!.Value, token).ConfigureAwait(false); break;
                    case "refreshCoreMetadata":
                        await RefreshMetadataAsync(force: true).ConfigureAwait(false);
                        RefreshIconCache(force: true);
                        response = _lifecycle.RefreshConnection(); break;
                    default: response = CommandResult.Rejected("unknownCommand"); break;
                }
            }
            catch (OperationCanceledException) { response = new("cancelled", "cancelled"); }
            catch (Exception error)
            {
                HostOperationLogger.Error("host", "Host command failed.", error);
                response = CommandResult.Failed("saveFailed", error.Message);
            }
        }).ConfigureAwait(false);
        return response;
    }

    private async Task<CommandResult> SaveOptionAsync(string name, bool value, CancellationToken token)
    {
        await _settings.SetOptionAsync(name, value, token).ConfigureAwait(false);
        return CommandResult.Completed("optionSaved", saved: true);
    }

    // Native entry points share the same command path as the WebView.
    public Task<CommandResult> StartCoreAsync(bool showTrayNotification = false) => ExecuteNativeCoreAsync("start", showTrayNotification);
    public Task<CommandResult> StopCoreAsync(bool showTrayNotification = false) => ExecuteNativeCoreAsync("stop", showTrayNotification);
    public async Task<bool> RestartCoreAsync(bool showTrayNotification = false) =>
        (await ExecuteNativeCoreAsync("restart", showTrayNotification).ConfigureAwait(false)).Status == "completed";
    private async Task<CommandResult> ExecuteNativeCoreAsync(string type, bool showTrayNotification)
    {
        var result = await ExecuteAsync(new() { ProtocolVersion = 2, Type = type, CoreType = Settings.ActiveCoreKind }).ConfigureAwait(false);
        if (result.Status == "elevationRequired") RequestElevation();
        if (showTrayNotification) TrayNotificationRequested?.Invoke(this,
            result.Status == "completed" ? "内核操作已完成。" : result.Message ?? result.Code);
        return result;
    }

    public void RequestElevation()
    {
        if (!_disposed && !_tasks.IsClosing) RelaunchRequested?.Invoke(this, new(true, true, true));
    }

    public Task ReconcileAutostartAsync() => _tasks.Run(async token =>
    {
        // Only reconcile this version's explicitly enabled task. No registry/old-install migration.
        if (!Settings.DesktopOptions.Autostart) return;
        var status = await Task.Run(AutostartManager.QueryStatus, token).ConfigureAwait(false);
        if (!status.IsValid)
        {
            var result = await SetAutostartAsync(true, token).ConfigureAwait(false);
            if (result.Status != "completed") NoticeRequested?.Invoke(this, result.Message ?? result.Code);
        }
    });

    private async Task<CommandResult> SetAutostartAsync(bool value, CancellationToken token)
    {
        if (!await _autostartGate.WaitAsync(0, token).ConfigureAwait(false)) return CommandResult.Rejected("busy");
        IsAutostartUpdating = true;
        PublishState();
        try
        {
            var result = await AutostartManager.SetEnabledAsync(value).ConfigureAwait(false);
            if (!result.Success) return CommandResult.Failed("autostartFailed", result.Message);
            try
            {
                // The OS operation completed: persist its verified result even during shutdown.
                await _settings.SetOptionAsync("autostart", value).ConfigureAwait(false);
                return CommandResult.Completed("optionSaved", saved: true);
            }
            catch (Exception error)
            {
                return CommandResult.Failed("autostartChangedButNotSaved", "系统自启状态已改变，但设置未保存：" + error.Message);
            }
        }
        finally { IsAutostartUpdating = false; _autostartGate.Release(); PublishState(); }
    }

    private CoreProfile EffectiveProfile(CoreKind kind)
    {
        var current = Settings.Profile(kind);
        var running = _lifecycle.Running;
        return _core.IsRunning && running?.Kind == kind
            ? current with { ExePath = running.Profile.ExePath, ConfigPath = running.Profile.ConfigPath }
            : current;
    }

    private static string MetadataKey(CoreProfile profile)
    {
        static string FileKey(string path)
        {
            var file = new FileInfo(path);
            return file.Exists ? $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}" : path + "|missing";
        }
        return FileKey(profile.ExePath) + "|" + FileKey(profile.ConfigPath);
    }

    public Task RefreshMetadataAsync(bool force = false)
    {
        lock (_metadataSync)
        {
            if (_tasks.IsClosing) return Task.CompletedTask;
            _metadataAgain = true;
            _metadataForce |= force;
            if (_metadataTask is { IsCompleted: false }) return _metadataTask;
            _metadataTask = _tasks.Run(token => Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    bool forced;
                    lock (_metadataSync)
                    {
                        if (!_metadataAgain) { _metadataTask = null; return; }
                        _metadataAgain = false; forced = _metadataForce; _metadataForce = false;
                    }
                    foreach (var kind in Enum.GetValues<CoreKind>())
                    {
                        var profile = EffectiveProfile(kind);
                        var key = MetadataKey(profile);
                        lock (_metadataSync) { if (!forced && _metadata.TryGetValue(kind, out var cached) && cached.Key == key && !cached.NeedsRetry) continue; }
                        string raw = "";
                        var failed = false;
                        var exists = File.Exists(profile.ExePath);
                        try { if (exists) raw = await _readCoreVersion(profile.ExePath, kind, token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { return; }
                        catch (Exception error) { failed = true; HostOperationLogger.Error("version", "Core metadata query failed.", error); }
                        var version = CoreUpdateChecker.DisplayVersion(raw, kind);
                        var tun = kind == CoreKind.SingBox ? ReadTun(profile.ConfigPath) : null;
                        if (MetadataKey(EffectiveProfile(kind)) != key) { lock (_metadataSync) _metadataAgain = true; continue; }
                        lock (_metadataSync) _metadata[kind] = new(key, raw, version, tun, failed || (exists && version.Length == 0));
                    }
                    PublishState();
                }
            }, token));
            return _metadataTask;
        }
    }

    private static bool? ReadTun(string path)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path), new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (json.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!json.RootElement.TryGetProperty("inbounds", out var inbounds)) return false;
            if (inbounds.ValueKind != JsonValueKind.Array) return null;
            var hasTun = false;
            foreach (var item in inbounds.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(type.GetString())) return null;
                var disabled = false;
                var enabled = true;
                if (item.TryGetProperty("disabled", out var disabledValue))
                {
                    if (disabledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
                    disabled = disabledValue.GetBoolean();
                }
                if (item.TryGetProperty("enabled", out var enabledValue))
                {
                    if (enabledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
                    enabled = enabledValue.GetBoolean();
                }
                hasTun |= type.GetString() == "tun" && !disabled && enabled;
            }
            return hasTun;
        }
        catch { return null; }
    }

    public Task CheckForCoreUpdateAsync(CancellationToken cancellationToken = default) => _tasks.Run(async lifetime =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellationToken);
        var token = linked.Token;
        if (!await _coreUpdateGate.WaitAsync(0, token).ConfigureAwait(false)) { Interlocked.Exchange(ref _coreUpdatePending, 1); return; }
        IsCoreUpdateChecking = true;
        ResetCoreUpdateState();
        var kind = _lifecycle.Kind;
        var epoch = _lifecycle.Epoch;
        try
        {
            await RefreshMetadataAsync().ConfigureAwait(false);
            Metadata? metadata;
            lock (_metadataSync) _metadata.TryGetValue(kind, out metadata);
            if (metadata is null || metadata.RawVersion.Length == 0) return;
            var result = await CoreUpdateChecker.CheckReleaseAsync(CoreUpgradeSupport.SharedClient, metadata.RawVersion, kind == CoreKind.SingBox, token).ConfigureAwait(false);
            if (kind == _lifecycle.Kind && epoch == _lifecycle.Epoch && !_disposed)
            { LatestCoreVersion = result.LatestVersion; CoreUpdateAvailable = result.UpdateAvailable; }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { HostOperationLogger.Error("update", "Core update check failed.", error); }
        finally
        {
            IsCoreUpdateChecking = false;
            _coreUpdateGate.Release();
            PublishState();
            if (Interlocked.Exchange(ref _coreUpdatePending, 0) != 0 && !_tasks.IsClosing) _ = CheckForCoreUpdateAsync();
        }
    });

    public Task CheckForAppUpdateAsync(bool manual, CancellationToken cancellationToken = default) => _tasks.Run(async lifetime =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellationToken);
        if (!await _appUpdateGate.WaitAsync(0, linked.Token).ConfigureAwait(false))
        { PublishAppUpdateResult(AppUpdateResultKind.Busy, manual); return; }
        IsAppUpdateChecking = true; PublishState();
        try
        {
            var result = await AppUpdateChecker.CheckAsync(DashboardVersion.Current, cancellationToken: linked.Token).ConfigureAwait(false);
            LatestAppVersion = result.LatestVersion;
            AppUpdateAvailable = result.UpdateAvailable;
            PublishAppUpdateResult(AppUpdateAvailable ? AppUpdateResultKind.Available : AppUpdateResultKind.UpToDate, manual);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { HostOperationLogger.Error("update", "Dashboard update check failed.", error); PublishAppUpdateResult(AppUpdateResultKind.Failed, manual); }
        finally { IsAppUpdateChecking = false; _appUpdateGate.Release(); PublishState(); }
    });

    private void PublishAppUpdateResult(string result, bool manual) => AppUpdateResultRequested?.Invoke(this,
        HostOutboundMessage.AppUpdateResult(result, manual, DashboardVersion.Current, LatestAppVersion));

    public void OpenAppReleasePage() => OpenWeb(AppUpdateChecker.ReleasesPageUrl);
    public void OpenCoreRepositoryPage() => OpenWeb(GetCoreRepositoryUrl(_lifecycle.Kind == CoreKind.SingBox));
    internal static string GetCoreRepositoryUrl(bool isSingBox) => isSingBox ? SingBoxRepositoryUrl : MihomoRepositoryUrl;
    private void OpenWeb(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) { MessageRequested?.Invoke(this, new("无法打开链接", error.Message, MessageBoxIcon.Warning)); }
    }

    public void RefreshIconCache(bool force = false)
    {
        if (_tasks.IsClosing) return;
        if (Settings.ActiveCoreKind != CoreKind.Mihomo)
        {
            lock (_metadataSync)
            {
                Interlocked.Increment(ref _iconGeneration);
                _iconConfigKey = "";
                _iconCancellation?.Cancel();
                _iconCancellation = null;
            }
            _icons.ClearPublishedMap();
            return;
        }
        if (force) lock (_metadataSync) _iconConfigKey = "";
        var path = Settings.Profile(CoreKind.Mihomo).ConfigPath;
        _ = _tasks.Run(async lifetime =>
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            await Task.Run(async () =>
            {
                string key;
                try { var file = new FileInfo(path); key = path + "|" + (file.Exists ? file.LastWriteTimeUtc.Ticks : 0); }
                catch { return; }
                long generation;
                lock (_metadataSync)
                {
                    if (Settings.ActiveCoreKind != CoreKind.Mihomo || Settings.Profiles.Mihomo.ConfigPath != path || _iconConfigKey == key) return;
                    _iconConfigKey = key;
                    generation = Interlocked.Increment(ref _iconGeneration);
                    _iconCancellation?.Cancel();
                    _iconCancellation = request;
                }
                bool Current() => !_tasks.IsClosing && !request.IsCancellationRequested
                    && Settings.ActiveCoreKind == CoreKind.Mihomo && generation == Interlocked.Read(ref _iconGeneration);
                try
                {
                    await _icons.LoadExistingAsync(path, request.Token, Current).ConfigureAwait(false);
                    await _icons.RefreshAsync(path, request.Token, Current).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    lock (_metadataSync) { if (ReferenceEquals(_iconCancellation, request)) _iconConfigKey = ""; }
                }
                catch (Exception error)
                {
                    lock (_metadataSync) { if (ReferenceEquals(_iconCancellation, request)) _iconConfigKey = ""; }
                    HostOperationLogger.Error("icon-cache", "Icon refresh failed.", error);
                }
                finally { lock (_metadataSync) { if (ReferenceEquals(_iconCancellation, request)) _iconCancellation = null; } }
            }, lifetime).ConfigureAwait(false);
        });
    }

    private void OnSettingsChanged(object? sender, EventArgs args)
    {
        var selectionChanged = _lastActiveCoreKind != Settings.ActiveCoreKind;
        if (!ReferenceEquals(_lastProfiles, Settings.Profiles) || selectionChanged)
        {
            _lastProfiles = Settings.Profiles; _lastActiveCoreKind = Settings.ActiveCoreKind;
            _ = RefreshMetadataAsync(); RefreshIconCache(force: selectionChanged); ResetCoreUpdateState();
        }
        PublishState();
    }
    private void OnRuntimeChanged(object? sender, EventArgs args)
    {
        if (_lastRuntimeEpoch != _lifecycle.Epoch)
        { _lastRuntimeEpoch = _lifecycle.Epoch; ResetCoreUpdateState(); _ = RefreshMetadataAsync(); }
        if (_lifecycle.IsUpgradeInProgress) ResetCoreUpdateState();
        if (!_disposed) RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void ResetCoreUpdateState() { LatestCoreVersion = ""; CoreUpdateAvailable = false; PublishState(); }
    private void OnLog(object? sender, string text) => LogReceived?.Invoke(this, text);
    private void OnIcons(object? sender, EventArgs args) => IconCacheChanged?.Invoke(this, args);
    private void PublishState() { if (!_disposed) StateChanged?.Invoke(this, EventArgs.Empty); }
    public void ShowNotice(string text) => NoticeRequested?.Invoke(this, text);
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public Task ShutdownAsync() => _shutdownTask ??= ShutdownOwnedAsync();
    private async Task ShutdownOwnedAsync()
    {
        _disposed = true;
        var core = _lifecycle.ShutdownAsync(TimeSpan.FromSeconds(10));
        var background = _tasks.StopAndWaitAsync(TimeSpan.FromSeconds(10));
        var server = _server.StopAsync();
        await Task.WhenAll(core, background, server).ConfigureAwait(false);
        if (!core.Result || !background.Result) HostOperationLogger.Error("shutdown", "Shutdown budget expired; unfinished resources are not disposed under active work.", new TimeoutException());
        Detach();
        if (core.Result) { _lifecycle.Dispose(); _core.Dispose(); }
        if (background.Result) _tasks.Dispose();
    }
    private void Detach()
    {
        _settings.Changed -= OnSettingsChanged;
        _lifecycle.Changed -= OnRuntimeChanged;
        _core.LogReceived -= OnLog;
        _icons.CacheChanged -= OnIcons;
    }
    public void Dispose()
    {
        Detach();
        if (_shutdownTask is null) _ = ShutdownAsync();
    }
}

internal sealed record HostRelaunchRequest(bool StartCore, bool StartMinimized, bool ElevatedRestart);
internal sealed record HostMessageRequest(string Title, string Message, MessageBoxIcon Icon);
