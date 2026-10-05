namespace Dashboard;

// All core mutations enter here. Window dialogs and desktop options never hold this gate.
internal sealed class CoreLifecycleController : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly CoreProcessManager _core;
    private readonly CoreOperationGate _gate = new();
    private readonly ShutdownTaskTracker _tasks = new();
    private readonly object _sync = new();
    private readonly Func<bool> _isAdministrator;
    private static readonly HttpClient ApiClient = new();
    private readonly HttpClient _upgradeClient;
    private CancellationTokenSource? _probe;
    private CoreLaunchSpec? _running;
    private CoreProfile? _connection;
    private int? _observedPid;
    private long _epoch;
    private string _apiStatus = "idle";
    private string _apiVersion = "";
    private string _operation = "idle";
    private bool _closing;
    private Task<bool>? _shutdownTask;

    public event EventHandler? Changed;
    public long Epoch { get { lock (_sync) return _epoch; } }
    public string ApiVersion { get { lock (_sync) return _apiVersion; } }
    public string ApiStatus { get { lock (_sync) return _apiStatus; } }
    public string Operation { get { lock (_sync) return _operation; } }
    public CoreLaunchSpec? Running { get { lock (_sync) return _running; } }
    public bool IsUpgradeInProgress => Operation == "upgradeCore";
    public bool IsSwitchInProgress => Operation == "switchCore";
    public bool IsBusy => Operation != "idle";
    public CoreProfile Connection { get { lock (_sync) return _core.IsRunning ? _connection ?? _settings.Current.ActiveProfile : _settings.Current.ActiveProfile; } }
    public CoreKind Kind => _core.IsRunning && Running is { } running ? running.Kind : _settings.Current.ActiveCoreKind;
    public bool RequiresRestart => _core.IsRunning && Running is { } running
        && !running.SameProcessTarget(new CoreLaunchSpec(running.Kind, _settings.Current.Profile(running.Kind)));

    public CoreLifecycleController(SettingsStore settings, CoreProcessManager core, Func<bool>? isAdministrator = null,
        HttpClient? upgradeClient = null)
    {
        _settings = settings;
        _core = core;
        _upgradeClient = upgradeClient ?? CoreUpgradeSupport.SharedClient;
        _isAdministrator = isAdministrator ?? DashboardHost.IsRunningAsAdministrator;
        _core.StatusChanged += OnProcessChanged;
    }

    public Task<CommandResult> ExecuteAsync(HostRequest request)
    {
        if (_closing) return Task.FromResult(CommandResult.Rejected("closing"));
        var lease = _gate.TryEnter();
        if (lease is null) return Task.FromResult(CommandResult.Rejected("busy"));
        lock (_sync) _operation = request.Type;
        Publish();
        return RunAcceptedAsync(request, lease);
    }

    private async Task<CommandResult> RunAcceptedAsync(HostRequest request, IDisposable lease)
    {
        var result = CommandResult.Rejected("closing");
        try
        {
            await _tasks.Run(token => Task.Run(async () => result = await ExecuteOwnedAsync(request, token), token)).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) { return new CommandResult("cancelled", "cancelled"); }
        catch (Exception error)
        {
            HostOperationLogger.Error("core", "Core command failed.", error);
            return CommandResult.Failed("operationFailed", error.Message);
        }
        finally
        {
            lock (_sync) _operation = "idle";
            lease.Dispose();
            Publish();
        }
    }

    private async Task<CommandResult> ExecuteOwnedAsync(HostRequest request, CancellationToken token)
    {
        var kind = request.CoreType ?? _settings.Current.ActiveCoreKind;
        var saved = false;
        try
        {
            if (request.Type == "start" && _core.IsRunning) return CommandResult.Rejected("alreadyRunning");
            if (request.Type is "restart" or "upgradeCore" && _core.IsRunning && Kind != kind)
                return CommandResult.Rejected("wrongCore");
            if (request.Type == "upgradeCore")
            {
                if (request.Draft is not null) return CommandResult.Rejected("invalidInput");
                if (request.ConfirmUnverified && (request.ExpectedRevision is null || request.ExpectedRuntimeEpoch is null))
                    return CommandResult.Rejected("confirmationExpired");
                if (request.ExpectedRevision is { } revision && revision != _settings.Current.Profile(kind).Revision)
                    return CommandResult.Rejected("staleRevision");
                if (request.ExpectedRuntimeEpoch is { } epoch && epoch != Epoch)
                    return CommandResult.Rejected("staleRuntime");
            }
            if (request.Type == "completeSetup")
            {
                if (!_core.IsRunning || ApiStatus != "ready") return CommandResult.Rejected("apiNotReady");
                await _settings.CompleteSetupAsync(token).ConfigureAwait(false);
                return CommandResult.Completed("setupCompleted", saved: true);
            }
            if (request.Draft is not null)
            {
                var before = _settings.Current.Profile(kind);
                await _settings.UpdateProfileAsync(kind, request.ExpectedRevision ?? -1, request.Draft, token).ConfigureAwait(false);
                saved = true;
                var current = _settings.Current.Profile(kind);
                if (_core.IsRunning && Kind == kind && (before.ApiUrl != current.ApiUrl || before.Secret != current.Secret
                    || before.SecretDecryptionFailed != current.SecretDecryptionFailed))
                    Connect(current);
            }
            switch (request.Type)
            {
                case "saveProfile":
                    if (!saved) return CommandResult.Rejected("missingDraft");
                    return CommandResult.Completed("profileSaved", saved: true);
                case "stop":
                    CancelProbe();
                    await _core.StopAsync().ConfigureAwait(false);
                    return CommandResult.Completed("stopped");
                case "start":
                case "restart":
                    ValidateLaunch(new CoreLaunchSpec(kind, _settings.Current.Profile(kind)));
                    if (request.Type == "restart")
                    {
                        CancelProbe();
                        await _core.StopAsync().ConfigureAwait(false);
                    }
                    if (_settings.Current.ActiveCoreKind != kind)
                        await _settings.SetActiveCoreAsync(kind, token).ConfigureAwait(false);
                    return await StartAsync(new CoreLaunchSpec(kind, _settings.Current.Profile(kind)), saved, token).ConfigureAwait(false);
                case "switchCore":
                    return await SwitchAsync(kind, saved, token).ConfigureAwait(false);
                case "upgradeCore":
                    return await UpgradeAsync(kind, saved, request.ConfirmUnverified, token).ConfigureAwait(false);
                default:
                    return CommandResult.Rejected("unknownCommand");
            }
        }
        catch (SettingsConflictException) { return CommandResult.Rejected("staleRevision"); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return new("cancelled", "cancelled", Saved: saved); }
        catch (UnverifiedReleaseException error) { return new("rejected", "confirmationRequired", error.Message, saved); }
        catch (Exception error)
        {
            HostOperationLogger.Error("core", $"Core operation {request.Type} failed.", error);
            return CommandResult.Failed(error is ArgumentException ? "invalidInput" : "operationFailed", error.Message, saved);
        }
    }

    private async Task<CommandResult> StartAsync(CoreLaunchSpec spec, bool saved, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_closing) return CommandResult.Rejected("closing");
        if (!_isAdministrator()) return new("elevationRequired", "elevationRequired", Saved: saved);
        if (spec.Profile.SecretDecryptionFailed) return CommandResult.Rejected("credentialRecoveryRequired");
        lock (_sync) { _running = spec; _connection = spec.Profile; }
        await _core.StartAsync(spec, token).ConfigureAwait(false);
        if (!_core.IsRunning) return CommandResult.Failed("processStartFailed", saved: saved);
        Connect(spec.Profile);
        return CommandResult.Completed("processStarted", saved);
    }

    private static void ValidateLaunch(CoreLaunchSpec spec)
    {
        if (!File.Exists(spec.Profile.ExePath)) throw new FileNotFoundException("找不到内核文件。", spec.Profile.ExePath);
        if (!File.Exists(spec.Profile.ConfigPath)) throw new FileNotFoundException("找不到核心配置文件。", spec.Profile.ConfigPath);
        if (spec.Profile.SecretDecryptionFailed) throw new InvalidOperationException("请先明确替换无法解密的 Secret。");
    }

    private async Task<CommandResult> SwitchAsync(CoreKind kind, bool saved, CancellationToken token)
    {
        var target = new CoreLaunchSpec(kind, _settings.Current.Profile(kind));
        ValidateLaunch(target);
        var previousKind = Kind;
        var previous = _core.IsRunning ? Running : null;
        CancelProbe();
        // A failed stop leaves the existing active selection/owned Process unchanged.
        await _core.StopAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        try
        {
            await _settings.SetActiveCoreAsync(kind, token).ConfigureAwait(false);
            var result = await StartAsync(new(kind, _settings.Current.Profile(kind)), saved, token).ConfigureAwait(false);
            if (result.Status is "failed" or "rejected") await RecoverAsync(previousKind, previous, token).ConfigureAwait(false);
            return result;
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            await RecoverAsync(previousKind, previous, token).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RecoverAsync(CoreKind kind, CoreLaunchSpec? previous, CancellationToken token)
    {
        if (_closing || token.IsCancellationRequested) return;
        try
        {
            if (_core.IsRunning) await _core.StopAsync().ConfigureAwait(false);
            await _settings.SetActiveCoreAsync(kind, token).ConfigureAwait(false);
            if (previous is not null)
            {
                var connection = _settings.Current.Profile(kind);
                var recovery = previous with
                {
                    Profile = previous.Profile with
                    {
                        ApiUrl = connection.ApiUrl,
                        Secret = connection.Secret,
                        SecretDecryptionFailed = connection.SecretDecryptionFailed
                    }
                };
                await StartAsync(recovery, saved: false, token).ConfigureAwait(false);
            }
        }
        catch (Exception error) { HostOperationLogger.Error("core", "One-shot recovery failed; no further restart attempted.", error); }
    }

    private async Task<CommandResult> UpgradeAsync(CoreKind kind, bool saved, bool confirmUnverified, CancellationToken token)
    {
        if (_core.IsRunning && RequiresRestart) return CommandResult.Rejected("restartRequired");
        var profile = _settings.Current.Profile(kind);
        if (profile.SecretDecryptionFailed) return CommandResult.Rejected("credentialRecoveryRequired");
        if (kind == CoreKind.Mihomo)
        {
            if (!_core.IsRunning || ApiStatus != "ready") return CommandResult.Rejected("apiNotReady");
            var result = await MihomoApiUpdater.UpgradeAsync(profile.ApiUrl, profile.Secret, token).ConfigureAwait(false);
            if (result.IsAlreadyLatest) return CommandResult.Completed("alreadyLatest", saved);
            Connect(profile);
            return CommandResult.Completed("upgraded", saved);
        }
        var previous = _core.IsRunning ? Running : null;
        CoreUpgradeResult? upgrade = null;
        try
        {
            upgrade = await SingBoxUpdater.UpgradeAsync(profile.ExePath,
                () => { CancelProbe(); _core.Stop(TimeSpan.FromSeconds(8)); }, token, confirmUnverified,
                _upgradeClient, CoreVersionReader.ReadAsync).ConfigureAwait(false);
            if (upgrade.IsAlreadyLatest) return CommandResult.Completed("alreadyLatest", saved);
            if (previous is not null)
            {
                var started = await StartAsync(new(kind, profile), saved, token).ConfigureAwait(false);
                if (started.Status != "completed") throw new InvalidOperationException("升级后内核未能启动。");
                await Task.Delay(250, token).ConfigureAwait(false);
                if (!_core.IsRunning) throw new InvalidOperationException("升级后的内核立即退出。");
            }
            return CommandResult.Completed("upgraded", saved);
        }
        catch (Exception) when (!_closing && !token.IsCancellationRequested)
        {
            if (upgrade is { IsAlreadyLatest: false } && File.Exists(upgrade.BackupPath))
            {
                try
                {
                    await _core.StopAsync().ConfigureAwait(false);
                    CoreUpgradeSupport.ReplaceCoreWithRollback(upgrade.BackupPath, profile.ExePath, upgrade.BackupPath);
                }
                catch (Exception recoveryError) { HostOperationLogger.Error("upgrade", "Upgrade rollback failed; backup retained.", recoveryError); throw; }
            }
            if (previous is not null && !_core.IsRunning) await RecoverAsync(kind, previous, token).ConfigureAwait(false);
            throw;
        }
    }

    private void OnProcessChanged(object? sender, EventArgs args)
    {
        var pid = _core.ProcessId;
        lock (_sync)
        {
            if (_observedPid != pid)
            {
                _observedPid = pid;
                _epoch++;
                _apiStatus = pid is null ? "idle" : "checking";
                _apiVersion = "";
            }
        }
        if (pid is null) CancelProbe();
        Publish();
    }

    public CommandResult RefreshConnection()
    {
        using var lease = _gate.TryEnter();
        if (lease is null) return CommandResult.Rejected(_closing ? "closing" : "busy");
        if (!_core.IsRunning) return CommandResult.Rejected("apiNotReady");
        Connect(_settings.Current.Profile(Kind));
        return CommandResult.Completed();
    }

    private void Connect(CoreProfile profile)
    {
        CancelProbe();
        if (_closing || !_core.IsRunning) return;
        CancellationTokenSource probe;
        long epoch;
        lock (_sync)
        {
            _connection = profile;
            epoch = ++_epoch;
            _apiVersion = "";
            _apiStatus = profile.SecretDecryptionFailed ? "unauthorized" : "checking";
            probe = CancellationTokenSource.CreateLinkedTokenSource(_tasks.Token);
            _probe = probe;
        }
        Publish();
        _ = _tasks.Run(async _ =>
        {
            try
            {
                var result = profile.SecretDecryptionFailed ? new CoreApiStatus("unauthorized", "")
                    : await CoreApiProbe.ProbeAsync(ApiClient, profile.ApiUrl, profile.Secret,
                        TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500), probe.Token).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_closing || epoch != _epoch || probe.IsCancellationRequested || !_core.IsRunning) return;
                    _apiStatus = result.Status;
                    _apiVersion = result.Version;
                }
                Publish();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                lock (_sync)
                {
                    if (_closing || epoch != _epoch || probe.IsCancellationRequested || !_core.IsRunning) return;
                    _apiStatus = "unreachable";
                    _apiVersion = "";
                }
                HostOperationLogger.Error("api", "API readiness probe failed.", error);
                Publish();
            }
            finally
            {
                lock (_sync) { if (ReferenceEquals(_probe, probe)) _probe = null; }
                probe.Dispose();
            }
        });
    }

    private void CancelProbe()
    {
        lock (_sync)
        {
            try { _probe?.Cancel(); } catch (ObjectDisposedException) { }
            _probe = null;
            if (_apiStatus == "checking") _apiStatus = "unreachable";
        }
    }

    private void Publish()
    {
        if (!_closing) Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> ShutdownAsync(TimeSpan timeout)
    {
        lock (_sync) return _shutdownTask ??= ShutdownOwnedAsync(timeout);
    }

    private async Task<bool> ShutdownOwnedAsync(TimeSpan timeout)
    {
        _closing = true;
        _gate.Close();
        CancelProbe();
        var drain = _tasks.StopAndWaitAsync(timeout);
        var stop = _core.StopAsync();
        try
        {
            await Task.WhenAll(drain, stop).WaitAsync(timeout).ConfigureAwait(false);
            return await drain;
        }
        catch (Exception error)
        {
            HostOperationLogger.Error("shutdown", "Core shutdown did not finish within its budget.", error);
            _ = stop.ContinueWith(task => { _ = task.Exception; }, TaskScheduler.Default);
            return false;
        }
    }

    public void Dispose()
    {
        _closing = true;
        _gate.Close();
        CancelProbe();
        _core.StatusChanged -= OnProcessChanged;
        if (_shutdownTask is { IsCompletedSuccessfully: true } && _shutdownTask.Result) _tasks.Dispose();
    }
}
