using System.Diagnostics;

namespace Dashboard;

internal sealed class DashboardApplicationContext : ApplicationContext
{
    private readonly DashboardHost _host;
    private readonly Control _dispatcher = new();
    private readonly Icon _appIcon;
    private readonly Icon _trayIconImage;
    private readonly NotifyIcon _trayIcon;
    private readonly bool _startMinimized;
    private readonly WebViewContentUpdate _webViewContentUpdate;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private MainForm? _mainForm;
    private TrayMenuForm? _trayMenu;
    private DateTime _lastTrayIconToggleAt = DateTime.MinValue;
    private bool _exiting;
    private bool _disposed;

    public DashboardApplicationContext(
        bool startMinimized,
        bool startCoreAfterLaunch,
        WebViewContentUpdate webViewContentUpdate)
    {
        _startMinimized = startMinimized;
        _webViewContentUpdate = webViewContentUpdate;
        _ = _dispatcher.Handle;
        _host = new DashboardHost();
        _host.StateChanged += OnHostStateChanged;
        _host.RuntimeStateChanged += OnHostStateChanged;
        _host.NoticeRequested += OnNoticeRequested;
        _host.TrayNotificationRequested += OnTrayNotificationRequested;
        _host.MessageRequested += OnMessageRequested;
        _host.RelaunchRequested += OnRelaunchRequested;

        _appIcon = LoadAppIcon();
        _trayIconImage = LoadTrayIcon(_appIcon);
        _trayIcon = CreateTrayIcon();
        UpdateTrayStatus();
        var shouldStartCore = _host.Settings.DesktopOptions.StartCoreOnLaunch || startCoreAfterLaunch;
        var isAdministrator = DashboardHost.IsRunningAsAdministrator();
        // Starting the core without elevation always ends in an elevated relaunch,
        // so both the window and the autostart reconcile wait for that restart.
        var willRelaunchElevated = WillRelaunchElevated(shouldStartCore, isAdministrator);
        var resumeSetup = startCoreAfterLaunch && !_host.Settings.SetupCompleted && !willRelaunchElevated;
        if (resumeSetup)
            _ = ResumeSetupAsync();
        else if (!willRelaunchElevated)
            _ = RunStartupOperationsAsync(shouldStartCore);

        if (!startMinimized && !willRelaunchElevated && !resumeSetup)
        {
            ShowMainWindow();
        }

        if (willRelaunchElevated)
        {
            _ = _host.StartCoreAsync();
        }

        _ = RunAutomaticUpdateCheckAsync(_lifetimeCancellation.Token);
    }

    private async Task ResumeSetupAsync()
    {
        try
        {
            var result = await ResumeSetupCoreAsync(
                () => _host.StartCoreAsync(), () => _host.IsRunning,
                () => _host.BuildRuntimeState(false).ApiStatus,
                () => _host.ExecuteAsync(new() { ProtocolVersion = 2, Type = "completeSetup" }),
                _lifetimeCancellation.Token);
            if (!_exiting)
            {
                ShowMainWindow();
                if (result.Status != "completed")
                    MessageBox.Show(_mainForm, result.Message ?? result.Code, "首次启动未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            await _host.ReconcileAutostartAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            HostOperationLogger.Error("host", "Failed to resume first-time setup.", error);
            if (!_exiting)
            {
                ShowMainWindow();
                MessageBox.Show(_mainForm, error.Message, "首次启动未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    internal static async Task<CommandResult> ResumeSetupCoreAsync(
        Func<Task<CommandResult>> start, Func<bool> isRunning, Func<string> apiStatus,
        Func<Task<CommandResult>> complete, CancellationToken token, TimeSpan? timeout = null)
    {
        token.ThrowIfCancellationRequested();
        var result = await start();
        if (result.Status != "completed") return result;
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (isRunning() && apiStatus() != "ready" && DateTime.UtcNow < deadline)
            await Task.Delay(100, token);
        token.ThrowIfCancellationRequested();
        if (!isRunning() || apiStatus() != "ready")
            return CommandResult.Failed("apiNotReady", "内核 API 尚未就绪，请检查 API 地址和 Secret。");
        return await complete();
    }

    internal bool HasMainWindow => _mainForm is { IsDisposed: false };

    private Task RunStartupOperationsAsync(bool shouldStartCore) =>
        RunStartupOperationsAsync(shouldStartCore, async () => { await _host.StartCoreAsync(); }, _host.ReconcileAutostartAsync);

    internal static async Task RunStartupOperationsAsync(
        bool shouldStartCore,
        Func<Task> startCore,
        Func<Task> reconcileAutostartAsync)
    {
        // Starting and autostart reconciliation share the core configuration gate.
        // Keep their order explicit so reconciliation cannot reject launch startup.
        if (shouldStartCore) await startCore();
        await reconcileAutostartAsync();
    }

    internal static bool WillRelaunchElevated(bool shouldStartCore, bool isAdministrator)
    {
        return shouldStartCore && !isAdministrator;
    }

    private async Task RunAutomaticUpdateCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.WhenAll(
                    _host.CheckForAppUpdateAsync(manual: false, cancellationToken),
                    _host.CheckForCoreUpdateAsync(cancellationToken));
                await Task.Delay(TimeSpan.FromHours(24), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal void ActivateMainWindow()
    {
        RunOnUiThread(ShowMainWindow);
    }

    internal void ShowMainWindow()
    {
        if (_exiting)
        {
            return;
        }

        if (_mainForm is null || _mainForm.IsDisposed)
        {
            var form = new MainForm(_host, _webViewContentUpdate);
            form.FormClosed += OnMainFormClosed;
            form.ExitRequested += OnWindowExitRequested;
            _mainForm = form;
            form.Show();
            HostOperationLogger.Diagnostic("performance", "host:mainFormCreatedOnDemand");
            return;
        }

        _mainForm.ShowFromTray();
    }

    private NotifyIcon CreateTrayIcon()
    {
        var icon = new NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "Dashboard",
            Visible = true
        };
        icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenTrayWindow();
            }
            else if (e.Button == MouseButtons.Right)
            {
                ShowTrayMenu(Cursor.Position);
            }
        };
        return icon;
    }

    private void OpenTrayWindow()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastTrayIconToggleAt).TotalMilliseconds < 250)
        {
            return;
        }

        _lastTrayIconToggleAt = now;
        ShowMainWindow();
    }

    private void ShowTrayMenu(Point location)
    {
        if (_disposed || _exiting) return;
        _trayMenu?.Close();
        _trayMenu = new TrayMenuForm(new[]
        {
            new TrayMenuItem("显示窗口", ShowMainWindow),
            new TrayMenuItem(
                "重启内核",
                () => _ = _host.RestartCoreAsync(showTrayNotification: true),
                Enabled: _host.IsRunning && !_host.IsUpgradeInProgress),
            new TrayMenuItem(
                "停止内核",
                () => _ = _host.StopCoreAsync(showTrayNotification: true),
                Enabled: _host.IsRunning && !_host.IsUpgradeInProgress),
            TrayMenuItem.Separator(),
            new TrayMenuItem("退出", ExitApplication)
        });
        _trayMenu.FormClosed += (sender, _) =>
        {
            if (ReferenceEquals(sender, _trayMenu))
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
        };
        _trayMenu.ShowNear(location);
    }

    private void OnWindowExitRequested(object? sender, EventArgs e) => ExitApplication();

    private void OnMainFormClosed(object? sender, FormClosedEventArgs e)
    {
        HostOperationLogger.Diagnostic("window-lifecycle", $"context observed formClosed reason={e.CloseReason} exiting={_exiting}");
        if (sender is MainForm form)
        {
            form.FormClosed -= OnMainFormClosed;
            form.ExitRequested -= OnWindowExitRequested;
        }

        _mainForm = null;
        if (!_exiting)
        {
            ExitApplication();
        }
    }

    private void OnHostStateChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(UpdateTrayStatus);
    }

    private void OnTrayNotificationRequested(object? sender, string message)
    {
        RunOnUiThread(() => _trayIcon.ShowBalloonTip(1800, "Dashboard", message, ToolTipIcon.Info));
    }

    private void OnNoticeRequested(object? sender, string message)
    {
        RunOnUiThread(() =>
        {
            if (_mainForm is null || _mainForm.IsDisposed || !_mainForm.Visible)
            {
                _trayIcon.ShowBalloonTip(1800, "Dashboard", message, ToolTipIcon.Info);
            }
        });
    }

    private void OnMessageRequested(object? sender, HostMessageRequest request)
    {
        RunOnUiThread(() =>
        {
            if (_mainForm is null || _mainForm.IsDisposed || !_mainForm.Visible)
            {
                _trayIcon.ShowBalloonTip(2500, request.Title, request.Message, ToToolTipIcon(request.Icon));
                return;
            }

            MessageBox.Show(
                _mainForm,
                request.Message,
                request.Title,
                MessageBoxButtons.OK,
                request.Icon);
        });
    }

    private void OnRelaunchRequested(object? sender, HostRelaunchRequest request)
    {
        RunOnUiThread(() => RelaunchAsAdministrator(request with { StartMinimized = ShouldKeepMinimizedForRelaunch() }));
    }

    private void UpdateTrayStatus()
    {
        _trayIcon.Text = _host.IsRunning ? "Dashboard - 运行中" : "Dashboard - 未运行";
    }

    private bool ShouldKeepMinimizedForRelaunch()
    {
        if (_mainForm is null || _mainForm.IsDisposed)
        {
            return _startMinimized;
        }

        return !_mainForm.Visible
            || !_mainForm.ShowInTaskbar
            || _mainForm.WindowState == FormWindowState.Minimized;
    }

    private async void RelaunchAsAdministrator(HostRelaunchRequest request)
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            var arguments = new List<string>();
            if (request.StartCore)
            {
                arguments.Add("--start-core");
            }
            if (request.StartMinimized)
            {
                arguments.Add("--minimized");
            }
            if (request.ElevatedRestart)
            {
                arguments.Add("--elevated-restart");
            }
            if (HostOperationLogger.IsDiagnosticEnabled)
            {
                arguments.Add("--diagnostic-log");
            }

            var handedOff = await RelaunchAfterConfirmationAsync(
                ConfirmCurrentWindowExitAsync,
                () =>
                {
                    using var replacement = Process.Start(new ProcessStartInfo(Application.ExecutablePath, string.Join(" ", arguments))
                    {
                        WorkingDirectory = AppSettings.AppDirectory,
                        UseShellExecute = true,
                        Verb = "runas"
                    }) ?? throw new InvalidOperationException("未能创建管理员进程。");
                },
                FinishExitAsync);
            if (!handedOff) _exiting = false;
        }
        catch (Exception ex)
        {
            _exiting = false;
            HostOperationLogger.Error("host", "Failed to restart as administrator.", ex);
            if (!request.StartMinimized)
            {
                ShowMainWindow();
            }
            MessageBox.Show(
                _mainForm,
                $"无法以管理员权限重启：{ex.Message}",
                "管理员重启失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_disposed || _dispatcher.IsDisposed)
        {
            return;
        }

        try
        {
            if (_dispatcher.InvokeRequired)
            {
                _dispatcher.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async void ExitApplication()
    {
        if (_exiting) return;

        _exiting = true;
        if (!await ConfirmCurrentWindowExitAsync())
        {
            _exiting = false;
            return;
        }
        await FinishExitAsync();
    }

    private Task<bool> ConfirmCurrentWindowExitAsync() =>
        _mainForm is { IsDisposed: false } form
            ? ConfirmExitAsync(form.FlushPreferencesAsync,
                () => MessageBox.Show(form, "存在未保存修改或保存尚未确认。仍然退出并放弃未保存修改？", "修改尚未保存", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            : Task.FromResult(true);

    internal static async Task<bool> RelaunchAfterConfirmationAsync(
        Func<Task<bool>> confirmExit, Action startReplacement, Func<Task> finishExit)
    {
        if (!await confirmExit()) return false;
        startReplacement(); // cancellation/failure must leave the old host alive
        await finishExit();
        return true;
    }

    private async Task FinishExitAsync()
    {
        HostOperationLogger.Diagnostic("window-lifecycle", "context exit requested; awaiting host shutdown.");
        _trayMenu?.Close();
        _trayIcon.Visible = false;
        await CompleteExitAsync(
            _host.ShutdownAsync,
            () =>
            {
                if (_mainForm is { IsDisposed: false } form)
                    form.CloseForApplicationExit();
                _mainForm = null;
            },
            ExitThread);
    }

    internal static async Task<bool> ConfirmExitAsync(Func<Task<bool>> flushAsync, Func<bool> confirmDiscard) =>
        await flushAsync() || confirmDiscard();

    internal static async Task CompleteExitAsync(
        Func<Task> shutdownAsync,
        Action closeWindows,
        Action exitThread)
    {
        try
        {
            // Keep the WinForms message loop alive while host operations complete;
            // their existing UI-context continuations must remain runnable.
            await shutdownAsync();
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("shutdown", "Asynchronous host shutdown failed.", ex);
        }
        finally
        {
            closeWindows();
            exitThread();
        }
    }

    protected override void ExitThreadCore()
    {
        HostOperationLogger.Diagnostic("window-lifecycle", "context ExitThreadCore.");
        DisposeOwnedResources();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeOwnedResources();
        }

        base.Dispose(disposing);
    }

    private void DisposeOwnedResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _host.StateChanged -= OnHostStateChanged;
        _host.RuntimeStateChanged -= OnHostStateChanged;
        _host.NoticeRequested -= OnNoticeRequested;
        _host.TrayNotificationRequested -= OnTrayNotificationRequested;
        _host.MessageRequested -= OnMessageRequested;
        _host.RelaunchRequested -= OnRelaunchRequested;
        _trayIcon.Visible = false;
        ShutdownResourceDisposer.DisposeAll(
            ("Tray icon", _trayIcon.Dispose),
            ("Tray menu", () => _trayMenu?.Dispose()),
            ("Tray icon image", _trayIconImage.Dispose),
            ("Application icon", _appIcon.Dispose),
            ("Dashboard host", _host.Dispose),
            ("Application lifetime cancellation", _lifetimeCancellation.Dispose),
            ("UI dispatcher", _dispatcher.Dispose));
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppSettings.AppDirectory, "resources", "app.ico");
        return File.Exists(iconPath)
            ? new Icon(iconPath)
            : Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
    }

    private static ToolTipIcon ToToolTipIcon(MessageBoxIcon icon)
    {
        return icon switch
        {
            MessageBoxIcon.Error => ToolTipIcon.Error,
            MessageBoxIcon.Warning => ToolTipIcon.Warning,
            _ => ToolTipIcon.Info
        };
    }

    private static Icon LoadTrayIcon(Icon fallback)
    {
        var iconPath = Path.Combine(AppSettings.AppDirectory, "resources", "tray.ico");
        return File.Exists(iconPath)
            ? new Icon(iconPath)
            : (Icon)fallback.Clone();
    }
}
