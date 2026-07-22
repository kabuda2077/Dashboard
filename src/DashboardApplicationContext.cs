using System.Diagnostics;

namespace Dashboard;

internal sealed class DashboardApplicationContext : ApplicationContext
{
    private readonly DashboardHost _host;
    private readonly Control _dispatcher = new();
    private readonly Icon _appIcon;
    private readonly Icon _trayIconImage;
    private readonly NotifyIcon _trayIcon;
    private MainForm? _mainForm;
    private TrayMenuForm? _trayMenu;
    private DateTime _lastTrayIconToggleAt = DateTime.MinValue;
    private bool _exiting;
    private bool _disposed;

    public DashboardApplicationContext(bool startMinimized, bool startCoreAfterLaunch)
    {
        _ = _dispatcher.Handle;
        _host = new DashboardHost();
        _host.ShouldKeepMinimizedForRelaunch = ShouldKeepMinimizedForRelaunch;
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
        var shouldStartCore = _host.Settings.StartCoreOnLaunch || startCoreAfterLaunch;
        if (!ShouldDeferAutostartReconcile(shouldStartCore, DashboardHost.IsRunningAsAdministrator()))
        {
            _ = Task.Run(_host.ReconcileAutostartAsync);
        }

        if (!startMinimized)
        {
            ShowMainWindow();
        }

        if (shouldStartCore)
        {
            _ = Task.Run(() => _host.StartCore());
        }
    }

    internal bool HasMainWindow => _mainForm is { IsDisposed: false };

    internal static bool ShouldDeferAutostartReconcile(bool shouldStartCore, bool isAdministrator)
    {
        return shouldStartCore && !isAdministrator;
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
            var form = new MainForm(_host);
            form.FormClosed += OnMainFormClosed;
            _mainForm = form;
            form.Show();
            HostOperationLogger.Info("performance", "host:mainFormCreatedOnDemand");
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
        _trayMenu?.Close();
        _trayMenu = new TrayMenuForm(new[]
        {
            new TrayMenuItem("显示窗口", ShowMainWindow),
            new TrayMenuItem(
                "重启内核",
                () => _ = Task.Run(() => _host.RestartCore(showTrayNotification: true)),
                Enabled: _host.IsRunning && !_host.IsUpgradeInProgress),
            new TrayMenuItem(
                "停止内核",
                () => _ = Task.Run(() => _host.StopCore(showTrayNotification: true)),
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

    private void OnMainFormClosed(object? sender, FormClosedEventArgs e)
    {
        HostOperationLogger.Info("window-lifecycle", $"context observed formClosed reason={e.CloseReason} exiting={_exiting}");
        if (sender is MainForm form)
        {
            form.FormClosed -= OnMainFormClosed;
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
        RunOnUiThread(() => RelaunchAsAdministrator(request));
    }

    private void UpdateTrayStatus()
    {
        _trayIcon.Text = _host.IsRunning ? "Dashboard - 运行中" : "Dashboard - 未运行";
    }

    private bool ShouldKeepMinimizedForRelaunch()
    {
        return _mainForm is null
            || _mainForm.IsDisposed
            || !_mainForm.Visible
            || !_mainForm.ShowInTaskbar
            || _mainForm.WindowState == FormWindowState.Minimized;
    }

    private void RelaunchAsAdministrator(HostRelaunchRequest request)
    {
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

            Process.Start(new ProcessStartInfo(Application.ExecutablePath, string.Join(" ", arguments))
            {
                WorkingDirectory = AppSettings.AppDirectory,
                UseShellExecute = true,
                Verb = "runas"
            });
            ExitApplication();
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("host", "Failed to restart as administrator.", ex);
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

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        HostOperationLogger.Info("window-lifecycle", "context exit requested.");
        _trayMenu?.Close();
        if (_mainForm is { IsDisposed: false } form)
        {
            form.CloseForApplicationExit();
        }
        _mainForm = null;
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        HostOperationLogger.Info("window-lifecycle", "context ExitThreadCore.");
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
        _host.StateChanged -= OnHostStateChanged;
        _host.RuntimeStateChanged -= OnHostStateChanged;
        _host.NoticeRequested -= OnNoticeRequested;
        _host.TrayNotificationRequested -= OnTrayNotificationRequested;
        _host.MessageRequested -= OnMessageRequested;
        _host.RelaunchRequested -= OnRelaunchRequested;
        _host.ShouldKeepMinimizedForRelaunch = null;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayMenu?.Dispose();
        _trayIconImage.Dispose();
        _appIcon.Dispose();
        _host.Dispose();
        _dispatcher.Dispose();
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
