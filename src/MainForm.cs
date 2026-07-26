using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard;

public sealed class MainForm : Form
{
    private readonly DashboardHost _host;
    private readonly AppSettings _settings;
    private readonly HostMessageRouter _hostMessageRouter;
    private readonly DashboardStatePublisher _statePublisher;
    private readonly Uri _dashboardUri;
    private readonly Icon _appIcon;
    private Panel _contentPanel = null!;
    private WebView2? _webView;
    private Rectangle _trayRestoreBounds;
    private FormWindowState _trayRestoreWindowState = FormWindowState.Normal;
    private bool _hiddenToTray;
    private bool _trayTransitionInProgress;
    private bool _allowClose;
    private bool _initialized;
    private bool _dashboardInitialized;
    private Task? _dashboardInitializationTask;
    private bool _webViewSuspended;
    private int _dashboardSuspendVersion;
    private readonly System.Windows.Forms.Timer _dashboardDisposeTimer = new() { Interval = DelayedDashboardDisposeMs };
    private const int ResizeBorderThickness = 8;
    private const int MaximizedContentPadding = 8;
    private const int DelayedDashboardDisposeMs = 60000;

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private const int CS_DROPSHADOW = 0x00020000;
    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMNCRP_ENABLED = 2;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int SW_SHOWMAXIMIZED = 3;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const int TrayHideAnimationDelayMs = 220;
    private const int WebViewInitializationAttempts = 3;
    private const int EAbortHResult = unchecked((int)0x80004004);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            cp.Style |= WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
            return cp;
        }
    }

    internal MainForm(DashboardHost host)
    {
        _host = host;
        _settings = host.Settings;
        _dashboardUri = host.DashboardUri;
        _statePublisher = new DashboardStatePublisher(
            () => _host.BuildState(WindowState == FormWindowState.Maximized),
            () => _host.BuildRuntimeState(WindowState == FormWindowState.Maximized),
            _host.GetIconCacheMap,
            HasDashboardWebView,
            ShouldHoldDashboardUpdates,
            PostDashboardMessage);

        Text = "Dashboard";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(244, 244, 245);
        MinimumSize = new Size(1120, 720);
        Size = new Size(1360, 840);
        _trayRestoreBounds = Bounds;
        StartPosition = FormStartPosition.CenterScreen;
        _appIcon = LoadAppIcon();
        Icon = _appIcon;

        BuildLayout();
        _hostMessageRouter = new HostMessageRouter(new HostMessageHandlers
        {
            WindowDrag = BeginWindowDrag,
            WindowResize = BeginWindowResize,
            WindowToggleMaximize = ToggleMaximize,
            WindowMinimize = MinimizeToTaskbar,
            WindowClose = Close,
            SaveSettingsAsync = _host.SaveSettingsAsync,
            SaveDashboardSettings = _host.SaveDashboardSettings,
            CompleteSetup = _host.CompleteSetup,
            StartCore = () => RunCoreOperation(() => _host.StartCore()),
            StopCore = () => RunCoreOperation(() => _host.StopCore()),
            RestartCore = () => RunCoreOperation(() => _host.RestartCore()),
            SwitchCoreAsync = targetCoreType => Task.Run(() => _host.SwitchCoreAsync(targetCoreType)),
            UpgradeCoreAsync = () => Task.Run(_host.UpgradeCoreAsync),
            BrowseCorePath = BrowseCorePath,
            BrowseConfigPath = BrowseConfigPath,
            OpenCoreLocationAsync = () => OpenPathLocationAsync(_settings.ActiveCorePath, "内核文件"),
            OpenConfigLocationAsync = () => OpenPathLocationAsync(_settings.ActiveConfigPath, "配置文件"),
            ShowNoticeAsync = ShowDashboardNoticeAsync,
            SendState = SendStateToDashboard,
            SendWindowChromeState = SendWindowChromeState
        });
        BindEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        var shownStartedAt = Stopwatch.GetTimestamp();
        base.OnShown(e);

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        RefreshStatus();
        _host.RefreshIconCache();
        await EnsureDashboardInitializedAsync();

        HostOperationLogger.Info("performance", $"host:onShown durationMs={Stopwatch.GetElapsedTime(shownStartedAt).TotalMilliseconds:0}");
    }

    private void BuildLayout()
    {
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        Controls.Add(_contentPanel);
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
            return;
        }

        UpdateMaximizedBounds();
        WindowState = FormWindowState.Maximized;
    }

    private void BeginWindowDrag()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        ReleaseCapture();
        _ = SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
    }

    private void BeginWindowResize(JsonElement root)
    {
        if (!IsHandleCreated || WindowState == FormWindowState.Maximized)
        {
            return;
        }

        var hitTest = GetResizeHitTest(GetString(root, "edge", string.Empty));
        if (hitTest == HTCLIENT)
        {
            return;
        }

        BeginWindowResize(hitTest);
    }

    private void BeginWindowResize(int hitTest)
    {
        if (!IsHandleCreated || WindowState == FormWindowState.Maximized || hitTest == HTCLIENT)
        {
            return;
        }

        ReleaseCapture();
        _ = SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(hitTest), IntPtr.Zero);
    }

    private static int GetResizeHitTest(string edge)
    {
        return edge switch
        {
            "left" => HTLEFT,
            "right" => HTRIGHT,
            "top" => HTTOP,
            "bottom" => HTBOTTOM,
            "topLeft" => HTTOPLEFT,
            "topRight" => HTTOPRIGHT,
            "bottomLeft" => HTBOTTOMLEFT,
            "bottomRight" => HTBOTTOMRIGHT,
            _ => HTCLIENT
        };
    }

    private void UpdateMaximizedBounds()
    {
        var screen = Screen.FromHandle(Handle);
        MaximizedBounds = screen.WorkingArea;
    }

    private Task EnsureDashboardInitializedAsync()
    {
        if (_dashboardInitialized && HasDashboardWebView())
        {
            return Task.CompletedTask;
        }

        if (_dashboardInitializationTask is { IsCompleted: false })
        {
            return _dashboardInitializationTask;
        }

        _dashboardInitializationTask = InitializeDashboardAsync();
        return _dashboardInitializationTask;
    }

    private async Task InitializeDashboardAsync()
    {
        if (!await InitializeWebViewAsync())
        {
            _dashboardInitializationTask = null;
            return;
        }

        LoadDashboard();
        RefreshStatus();
        _dashboardInitialized = true;
    }

    private void BindEvents()
    {
        _host.StateChanged += OnHostStateChanged;
        _host.RuntimeStateChanged += OnHostRuntimeStateChanged;
        _host.LogReceived += OnHostLogReceived;
        _host.IconCacheChanged += OnHostIconCacheChanged;
        _host.NoticeRequested += OnHostNoticeRequested;
        _dashboardDisposeTimer.Tick += (_, _) =>
        {
            _dashboardDisposeTimer.Stop();
            if (_settings.LightweightMode && _hiddenToTray && !Visible && !_trayTransitionInProgress)
            {
                DisposeDashboardView();
            }
        };
    }

    private void OnHostStateChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(SendStateToDashboard);
    }

    private void OnHostRuntimeStateChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(RefreshStatus);
    }

    private void OnHostLogReceived(object? sender, string entry)
    {
        RunOnUiThread(() => _statePublisher.QueueLogAppend(entry));
    }

    private void OnHostIconCacheChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(_statePublisher.SendIconCacheUpdated);
    }

    private void OnHostNoticeRequested(object? sender, string message)
    {
        RunOnUiThread(() => _ = ShowDashboardNoticeAsync(message));
    }

    private void MinimizeToTaskbar()
    {
        if (_trayTransitionInProgress)
        {
            return;
        }

        _hiddenToTray = false;
        ShowInTaskbar = true;
        MinimizeWindowWithAnimation();
    }

    private void MinimizeWindowWithAnimation()
    {
        if (!IsHandleCreated)
        {
            WindowState = FormWindowState.Minimized;
            return;
        }

        _ = ShowWindowAsync(Handle, SW_MINIMIZE);
    }

    private void RestoreWindowWithAnimation()
    {
        if (!IsHandleCreated)
        {
            WindowState = _trayRestoreWindowState == FormWindowState.Maximized
                ? FormWindowState.Maximized
                : FormWindowState.Normal;
            return;
        }

        var command = _trayRestoreWindowState == FormWindowState.Maximized
            ? SW_SHOWMAXIMIZED
            : SW_RESTORE;
        _ = ShowWindowAsync(Handle, command);
    }

    private void EnsureWebViewCreated()
    {
        CancelDelayedDashboardDispose();
        if (_webView is not null && !_webView.IsDisposed)
        {
            return;
        }

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        _contentPanel.Controls.Add(_webView);
        _webView.BringToFront();
    }

    private bool HasDashboardWebView()
    {
        return _webView?.CoreWebView2 is not null;
    }

    private static string WebViewUserDataDirectory => AppSettings.WebViewUserDataDirectory;

    private async Task<bool> InitializeWebViewAsync()
    {
        var startedAt = Stopwatch.GetTimestamp();
        for (var attempt = 1; attempt <= WebViewInitializationAttempts; attempt++)
        {
            EnsureWebViewCreated();
            var webView = _webView;
            if (webView is null)
            {
                return false;
            }

            try
            {
                AppSettings.MigratePortableDataDirectory("EBWebView", WebViewUserDataDirectory);
                AppSettings.MigrateResourceDataDirectory("runtime", "EBWebView", WebViewUserDataDirectory);
                Directory.CreateDirectory(WebViewUserDataDirectory);
                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: WebViewUserDataDirectory);
                await webView.EnsureCoreWebView2Async(environment);
                if (!ReferenceEquals(webView, _webView) || webView.CoreWebView2 is null)
                {
                    return false;
                }

                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BuildDashboardSettingsBootstrapScript());

                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                webView.CoreWebView2.NavigationCompleted += (_, _) =>
                {
                    HostOperationLogger.Info("performance", $"webview:navigationCompleted durationMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0}");
                    SendStateToDashboard();
                };

                HostOperationLogger.Info("performance", $"webview:initialized durationMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0}");
                return true;
            }
            catch (Exception ex) when (IsWebViewInitializationAborted(ex))
            {
                HostOperationLogger.Info(
                    "webview",
                    $"WebView2 initialization was canceled; rebuilding control (attempt {attempt}/{WebViewInitializationAttempts}).");
                ResetFailedWebView(webView);
                if (attempt == WebViewInitializationAttempts || IsDisposed || Disposing)
                {
                    return false;
                }

                await Task.Delay(150 * attempt);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                ShowWebViewRuntimeMissingMessage(ex);
                HostOperationLogger.Error("webview", "WebView2 Runtime was not found.", ex);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "WebView2 初始化失败，Dashboard 暂时无法显示界面。\n\n"
                        + $"详细错误：{ex.Message}",
                    "WebView2 初始化失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                HostOperationLogger.Error("webview", "Failed to initialize WebView2.", ex);
                return false;
            }
        }

        return false;
    }

    internal static bool IsWebViewInitializationAborted(Exception exception)
    {
        return exception is COMException { HResult: EAbortHResult }
            || exception.GetBaseException() is COMException { HResult: EAbortHResult };
    }

    private void ResetFailedWebView(WebView2 webView)
    {
        if (ReferenceEquals(webView, _webView))
        {
            _contentPanel.Controls.Remove(webView);
            _webView = null;
        }

        webView.Dispose();
    }

    private void ShowWebViewRuntimeMissingMessage(Exception exception)
    {
        MessageBox.Show(
            this,
            "Dashboard 需要 Microsoft Edge WebView2 Runtime 才能显示界面。\n\n"
                + "请安装 WebView2 Runtime 后重新打开 Dashboard：\n"
                + "https://developer.microsoft.com/microsoft-edge/webview2/\n\n"
                + $"详细错误：{exception.Message}",
            "缺少 WebView2 Runtime",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void LoadDashboard()
    {
        var coreWebView = _webView?.CoreWebView2;
        if (coreWebView is null)
        {
            return;
        }

        var uri = new Uri(_dashboardUri, $"?{BuildDashboardQuery()}#/core");
        coreWebView.Navigate(uri.ToString());
    }

    private string BuildDashboardQuery()
    {
        var query = new List<string>();
        if (Uri.TryCreate(_settings.ActiveDashboardApiUrl, UriKind.Absolute, out var apiUri))
        {
            query.Add(apiUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "https=1" : "http=1");
            query.Add($"hostname={Uri.EscapeDataString(apiUri.Host)}");
            query.Add($"port={Uri.EscapeDataString(apiUri.Port.ToString())}");

            var secondaryPath = apiUri.AbsolutePath.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(secondaryPath) && secondaryPath != "/")
            {
                query.Add($"secondaryPath={Uri.EscapeDataString(secondaryPath)}");
            }
        }
        else
        {
            query.Add("http=1");
            query.Add("hostname=127.0.0.1");
            query.Add("port=9090");
        }

        query.Add($"label={Uri.EscapeDataString("本机内核")}");
        query.Add($"coreType={Uri.EscapeDataString(_settings.CoreType)}");
        query.Add("disableUpgradeCore=1");

        return string.Join("&", query);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            await _hostMessageRouter.RouteAsync(e.WebMessageAsJson);
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("host-bridge", "Failed to process dashboard message.", ex);
            await ShowDashboardNoticeAsync($"操作失败：{ex.Message}");
        }
    }

    private string BuildDashboardSettingsBootstrapScript()
    {
        var hasDashboardSettings = _settings.DashboardSettings is not null;
        var settingsJson = JsonSerializer.Serialize(_settings.DashboardSettings ?? new Dictionary<string, string>(), HostBridgeJson.JsonOptions);
        var shouldApply = hasDashboardSettings ? "true" : "false";

        return "(() => {"
            + $"const settings = {settingsJson};"
            + $"const shouldApply = {shouldApply};"
            + "window.__mihomoDashboardSettings = settings;"
            + "window.__mihomoHasDashboardSettings = shouldApply;"
            + "if (!shouldApply) return;"
            + "const keys = Object.keys(settings || {});"
            + "const keySet = new Set(keys);"
            + "for (let i = localStorage.length - 1; i >= 0; i--) {"
            + "  const key = localStorage.key(i);"
            + "  if (key && key.startsWith('config/') && !keySet.has(key)) localStorage.removeItem(key);"
            + "}"
            + "for (const key of keys) {"
            + "  const value = settings[key];"
            + "  if (key.startsWith('config/') && typeof value === 'string') localStorage.setItem(key, value);"
            + "}"
            + "})();";
    }

    private static string GetString(JsonElement root, string propertyName, string fallback)
    {
        return HostBridgeJson.GetString(root, propertyName, fallback);
    }

    private static bool GetBool(JsonElement root, string propertyName, bool fallback)
    {
        return HostBridgeJson.GetBool(root, propertyName, fallback);
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            if (!IsHandleCreated)
            {
                return;
            }

            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }

            action();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RunCoreOperation(Action action)
    {
        _ = Task.Run(action);
    }

    private void RefreshStatus()
    {
        _statePublisher.SendRuntimeState();
    }

    private void SendStateToDashboard()
    {
        _statePublisher.SendState();
    }

    private Task ShowDashboardNoticeAsync(string message)
    {
        return _statePublisher.ShowNoticeAsync(message);
    }

    private void SendWindowChromeState()
    {
        _statePublisher.SendWindowChromeState(WindowState == FormWindowState.Maximized);
    }

    private bool ShouldHoldDashboardUpdates()
    {
        return _webViewSuspended
            || _hiddenToTray
            || !Visible
            || WindowState == FormWindowState.Minimized;
    }

    private async void SuspendDashboard()
    {
        var coreWebView = _webView?.CoreWebView2;
        if (_webViewSuspended || coreWebView is null)
        {
            return;
        }

        var suspendVersion = ++_dashboardSuspendVersion;
        _statePublisher.StopRefreshTimer();
        _statePublisher.MarkDirty();

        try
        {
            var suspended = await coreWebView.TrySuspendAsync();
            if (suspendVersion != _dashboardSuspendVersion || !ShouldHoldDashboardUpdates())
            {
                if (suspended)
                {
                    coreWebView.Resume();
                }

                return;
            }

            _webViewSuspended = suspended;
        }
        catch
        {
            _webViewSuspended = false;
        }
    }

    private void ResumeDashboard()
    {
        var coreWebView = _webView?.CoreWebView2;
        if (coreWebView is null)
        {
            return;
        }

        _dashboardSuspendVersion++;

        try
        {
            if (_webViewSuspended)
            {
                coreWebView.Resume();
                _webViewSuspended = false;
            }
        }
        catch
        {
            _webViewSuspended = false;
        }

        FlushDashboardUpdates();
    }

    private void DisposeDashboardView()
    {
        CancelDelayedDashboardDispose();
        _dashboardSuspendVersion++;
        _statePublisher.StopRefreshTimer();
        _statePublisher.MarkDirty();
        _webViewSuspended = false;
        _dashboardInitialized = false;
        _dashboardInitializationTask = null;

        var webView = _webView;
        if (webView is null)
        {
            return;
        }

        _contentPanel.Controls.Remove(webView);
        _webView = null;
        webView.Dispose();
    }

    private void ScheduleDashboardViewDispose()
    {
        _dashboardDisposeTimer.Stop();
        _dashboardDisposeTimer.Interval = DelayedDashboardDisposeMs;
        _dashboardDisposeTimer.Start();
    }

    private void CancelDelayedDashboardDispose()
    {
        if (_dashboardDisposeTimer.Enabled)
        {
            _dashboardDisposeTimer.Stop();
        }
    }

    private void FlushDashboardUpdates()
    {
        _statePublisher.Flush();
    }

    private void PostDashboardMessage(object message)
    {
        var coreWebView = _webView?.CoreWebView2;
        if (coreWebView is null)
        {
            return;
        }

        coreWebView.PostWebMessageAsJson(HostBridgeJson.Serialize(message));
    }

    private void BrowseCorePath()
    {
        using var dialog = new OpenFileDialog
        {
            Title = _settings.IsSingBox ? "选择 sing-box.exe" : "选择 mihomo.exe",
            Filter = _settings.IsSingBox
                ? "sing-box executable|sing-box*.exe|Executable|*.exe|All files|*.*"
                : "Mihomo executable|mihomo*.exe;clash*.exe|Executable|*.exe|All files|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _host.SetActiveCorePath(dialog.FileName);
        }
    }

    private void BrowseConfigPath()
    {
        using var dialog = new OpenFileDialog
        {
            Title = _settings.IsSingBox ? "选择 config.json" : "选择 config.yaml",
            Filter = _settings.IsSingBox
                ? "JSON config|*.json|All files|*.*"
                : "YAML config|*.yaml;*.yml|All files|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _host.SetActiveConfigPath(dialog.FileName);
        }
    }

    private async Task OpenPathLocationAsync(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            await ShowDashboardNoticeAsync($"请先设置{label}路径。");
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
            {
                UseShellExecute = true
            });
            return;
        }

        var directory = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"")
            {
                UseShellExecute = true
            });
            await ShowDashboardNoticeAsync($"{label}不存在，已打开所在文件夹。");
            return;
        }

        await ShowDashboardNoticeAsync($"找不到{label}所在位置。");
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        UpdateMaximizedBounds();
        if (WindowState == FormWindowState.Normal)
        {
            RememberTrayRestoreState();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyWindowChrome();

        if (WindowState != FormWindowState.Minimized)
        {
            ResumeDashboard();
            RememberTrayRestoreState();
            SendWindowChromeState();
        }
        else
        {
            SuspendDashboard();
        }

    }

    private void RememberTrayRestoreState()
    {
        if (!Visible || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _trayRestoreWindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Maximized
            : FormWindowState.Normal;
        _trayRestoreBounds = WindowState == FormWindowState.Normal
            ? Bounds
            : RestoreBounds;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        HostOperationLogger.Info(
            "window-lifecycle",
            $"formClosing reason={e.CloseReason} allowClose={_allowClose} minimizeToTray={_settings.MinimizeToTray} visible={Visible} windowState={WindowState}");
        if (!_allowClose && _settings.MinimizeToTray && ShouldHideToTrayOnClose(e.CloseReason))
        {
            e.Cancel = true;
            HostOperationLogger.Info("window-lifecycle", "formClosing cancelled; hiding to tray.");
            HideToTray();
            return;
        }

        base.OnFormClosing(e);
    }

    private static bool ShouldHideToTrayOnClose(CloseReason closeReason)
    {
        return closeReason is CloseReason.UserClosing or CloseReason.None;
    }

    private async void HideToTray(bool animate = true)
    {
        if (_hiddenToTray || _trayTransitionInProgress)
        {
            return;
        }

        RememberTrayRestoreState();
        _trayTransitionInProgress = true;
        try
        {
            _hiddenToTray = true;
            if (animate && Visible && WindowState != FormWindowState.Minimized)
            {
                MinimizeWindowWithAnimation();
                await Task.Delay(TrayHideAnimationDelayMs);
            }
            else if (WindowState != FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Minimized;
            }

            if (IsDisposed)
            {
                return;
            }

            Hide();
            if (_settings.LightweightMode)
            {
                ScheduleDashboardViewDispose();
            }
        }
        finally
        {
            _trayTransitionInProgress = false;
        }
    }

    public void ShowFromTray()
    {
        CancelDelayedDashboardDispose();
        if (_trayTransitionInProgress)
        {
            return;
        }

        if (Visible && WindowState != FormWindowState.Minimized)
        {
            ResumeDashboard();
            Activate();
            BringToFront();
            _ = EnsureDashboardInitializedAsync();
            return;
        }

        ResumeDashboard();
        _trayTransitionInProgress = true;
        try
        {
            _hiddenToTray = false;
            Opacity = 1;

            if (_trayRestoreWindowState != FormWindowState.Maximized && !_trayRestoreBounds.IsEmpty)
            {
                Bounds = _trayRestoreBounds;
            }

            if (!Visible)
            {
                WindowState = FormWindowState.Minimized;
                Show();
            }

            RestoreWindowWithAnimation();
            ResumeDashboard();
            Activate();
            BringToFront();
            _ = EnsureDashboardInitializedAsync();
        }
        finally
        {
            _trayTransitionInProgress = false;
        }
    }

    internal void CloseForApplicationExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        HostOperationLogger.Info("window-lifecycle", $"formClosed reason={e.CloseReason}");
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.StateChanged -= OnHostStateChanged;
            _host.RuntimeStateChanged -= OnHostRuntimeStateChanged;
            _host.LogReceived -= OnHostLogReceived;
            _host.IconCacheChanged -= OnHostIconCacheChanged;
            _host.NoticeRequested -= OnHostNoticeRequested;
            DisposeDashboardView();
            _statePublisher.Dispose();
            _dashboardDisposeTimer.Dispose();
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppSettings.AppDirectory, "resources", "app.ico");
        return File.Exists(iconPath)
            ? new Icon(iconPath)
            : Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateMaximizedBounds();
        ApplyWindowChrome();
    }

    private void ApplyWindowChrome()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var renderingPolicy = DWMNCRP_ENABLED;
        _ = DwmSetWindowAttribute(Handle, DWMWA_NCRENDERING_POLICY, ref renderingPolicy, sizeof(int));

        var cornerPreference = WindowState == FormWindowState.Maximized
            ? DWMWCP_DONOTROUND
            : DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

        var margins = WindowState == FormWindowState.Maximized
            ? DwmMargins.Empty
            : new DwmMargins(1);
        _ = DwmExtendFrameIntoClientArea(Handle, ref margins);

        _contentPanel.Padding = WindowState == FormWindowState.Maximized
            ? new Padding(MaximizedContentPadding)
            : Padding.Empty;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        if (m.Msg == WM_NCHITTEST)
        {
            var clientPoint = PointToClient(GetScreenPointFromLParam(m.LParam));
            m.Result = HitTestClientPoint(clientPoint);
            return;
        }

        base.WndProc(ref m);
    }

    private IntPtr HitTestClientPoint(Point point)
    {
        if (WindowState != FormWindowState.Maximized)
        {
            var left = point.X >= 0 && point.X < ResizeBorderThickness;
            var right = point.X <= ClientSize.Width && point.X >= ClientSize.Width - ResizeBorderThickness;
            var top = point.Y >= 0 && point.Y < ResizeBorderThickness;
            var bottom = point.Y <= ClientSize.Height && point.Y >= ClientSize.Height - ResizeBorderThickness;

            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
        }

        return HTCLIENT;
    }

    private static Point GetScreenPointFromLParam(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        return new Point(unchecked((short)(value & 0xffff)), unchecked((short)((value >> 16) & 0xffff)));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref DwmMargins margins);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmMargins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;

        public DwmMargins(int width)
        {
            Left = width;
            Right = width;
            Top = width;
            Bottom = width;
        }

        public static DwmMargins Empty => new();
    }
}
