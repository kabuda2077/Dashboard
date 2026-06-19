using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly CoreProcessManager _core = new();
    private readonly ProxyGroupIconCache _iconCache = new();
    private readonly DashboardServer _dashboardServer;
    private readonly Uri _dashboardUri;
    private readonly Icon _appIcon;
    private readonly Icon _trayIconImage;
    private readonly NotifyIcon _trayIcon;
    private Panel _contentPanel = null!;
    private WebView2? _webView;
    private TrayMenuForm? _trayMenu;
    private Rectangle _trayRestoreBounds;
    private FormWindowState _trayRestoreWindowState = FormWindowState.Normal;
    private bool _hiddenToTray;
    private bool _trayTransitionInProgress;
    private bool _allowClose;
    private bool _initialized;
    private bool _dashboardInitialized;
    private Task? _dashboardInitializationTask;
    private bool _startMinimized;
    private bool _startCoreAfterLaunch;
    private bool _coreUpgradeInProgress;
    private bool _coreSwitchInProgress;
    private bool _elevatedRetryPending;
    private bool _tunPermissionFailureSeen;
    private bool _stateRefreshPending;
    private bool _dashboardStateDirty;
    private bool _webViewSuspended;
    private int _dashboardSuspendVersion;
    private string? _pendingDashboardNotice;
    private readonly System.Windows.Forms.Timer _stateRefreshTimer = new() { Interval = 150 };
    private DateTime _lastStateRefresh = DateTime.MinValue;
    private DateTime _lastTrayIconToggleAt = DateTime.MinValue;
    private const int ResizeBorderThickness = 8;
    private const int MaximizedContentPadding = 8;
    private const int MinRefreshIntervalMs = 150;
    private const int MaxRefreshDelayMs = 1000;

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

    public MainForm(bool startMinimized, bool startCoreAfterLaunch)
    {
        _startMinimized = startMinimized;
        _startCoreAfterLaunch = startCoreAfterLaunch;
        _settings = AppSettings.Load();
        SyncAutostartSetting();
        _iconCache.LoadExisting(_settings.ConfigPath);
        _dashboardServer = new DashboardServer(Path.Combine(AppSettings.AppDirectory, "resources", "dashboard"), _iconCache.CacheDirectory);
        _dashboardUri = _dashboardServer.Start();

        Text = "Dashboard";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(244, 244, 245);
        MinimumSize = new Size(1120, 720);
        Size = new Size(1360, 840);
        _trayRestoreBounds = Bounds;
        StartPosition = FormStartPosition.CenterScreen;
        _appIcon = LoadAppIcon();
        _trayIconImage = LoadTrayIcon(_appIcon);
        Icon = _appIcon;

        _trayIcon = CreateTrayIcon();
        BuildLayout();
        BindEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        RefreshStatus();
        RefreshIconCache();

        if (_startMinimized)
        {
            HideToTray(animate: false);
        }
        else
        {
            await EnsureDashboardInitializedAsync();
        }

        if (_settings.StartCoreOnLaunch || _startCoreAfterLaunch)
        {
            StartCore();
        }
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
        EnsureWebViewCreated();
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
        _core.StatusChanged += (_, _) => BeginInvoke(new Action(RefreshStatus));
        _core.LogReceived += (_, entry) => BeginInvoke(new Action(() => QueueStateRefresh(entry)));
        _stateRefreshTimer.Tick += (_, _) =>
        {
            RefreshStateNow();
        };
        _iconCache.CacheChanged += (_, _) => BeginInvoke(new Action(SendStateToDashboard));
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
        ShowFromTray();
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

    private void ShowTrayMenu(Point location)
    {
        _trayMenu?.Close();

        var isRunning = _core.IsRunning;
        _trayMenu = new TrayMenuForm(new[]
        {
            new TrayMenuItem("显示窗口", ShowFromTray),
            new TrayMenuItem("重启内核", () => RestartCore(showTrayNotification: true), Enabled: isRunning && !_coreUpgradeInProgress),
            new TrayMenuItem("停止内核", () => StopCore(showTrayNotification: true), Enabled: isRunning && !_coreUpgradeInProgress),
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

    private void EnsureWebViewCreated()
    {
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

    private static string WebViewUserDataDirectory => AppSettings.AppDirectory;

    private async Task<bool> InitializeWebViewAsync()
    {
        EnsureWebViewCreated();
        var webView = _webView;
        if (webView is null)
        {
            return false;
        }

        try
        {
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

            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            webView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                SendStateToDashboard();
            };

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"WebView2 初始化失败：{ex.Message}", "缺少 WebView2 Runtime", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
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
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "windowDrag":
                    BeginWindowDrag();
                    return;
                case "windowResize":
                    BeginWindowResize(root);
                    return;
                case "windowToggleMaximize":
                    ToggleMaximize();
                    SendWindowChromeState();
                    return;
                case "windowMinimize":
                    MinimizeToTaskbar();
                    return;
                case "windowClose":
                    Close();
                    return;
                case "requestWindowState":
                    SendWindowChromeState();
                    return;
                case "requestState":
                    break;
                case "save":
                    SaveSettingsFromMessage(root, showMessage: true);
                    break;
                case "start":
                    SaveSettingsFromMessage(root, showMessage: false);
                    StartCore();
                    break;
                case "restart":
                    SaveSettingsFromMessage(root, showMessage: false);
                    RestartCore();
                    break;
                case "switchCore":
                    SaveSettingsFromMessage(root, showMessage: false);
                    await SwitchCoreAsync(root);
                    return;
                case "stop":
                    StopCore();
                    break;
                case "upgradeCore":
                    SaveSettingsFromMessage(root, showMessage: false);
                    await UpgradeCoreAsync();
                    break;
                case "browseCore":
                    SaveSettingsFromMessage(root, showMessage: false);
                    BrowseCorePath();
                    break;
                case "browseConfig":
                    SaveSettingsFromMessage(root, showMessage: false);
                    BrowseConfigPath();
                    break;
                case "openCoreLocation":
                    SaveSettingsFromMessage(root, showMessage: false);
                    await OpenPathLocationAsync(_settings.ActiveCorePath, "内核文件");
                    break;
                case "openConfigLocation":
                    SaveSettingsFromMessage(root, showMessage: false);
                    await OpenPathLocationAsync(_settings.ActiveConfigPath, "配置文件");
                    break;
            }

            SendStateToDashboard();
        }
        catch (Exception ex)
        {
            await ShowDashboardNoticeAsync($"操作失败：{ex.Message}");
        }
    }

    private void SaveSettingsFromMessage(JsonElement root, bool showMessage)
    {
        _settings.CoreType = AppSettings.NormalizeCoreType(GetString(root, "coreType", _settings.CoreType));
        _settings.CorePath = GetString(root, "mihomoCorePath", _settings.CorePath).Trim();
        _settings.ConfigPath = GetString(root, "mihomoConfigPath", _settings.ConfigPath).Trim();
        _settings.DashboardApiUrl = GetString(root, "mihomoApiUrl", _settings.DashboardApiUrl).Trim();
        _settings.Secret = GetString(root, "mihomoSecret", _settings.Secret);
        _settings.SingBoxCorePath = GetString(root, "singBoxCorePath", _settings.SingBoxCorePath).Trim();
        _settings.SingBoxConfigPath = GetString(root, "singBoxConfigPath", _settings.SingBoxConfigPath).Trim();
        _settings.SingBoxApiUrl = GetString(root, "singBoxApiUrl", _settings.SingBoxApiUrl).Trim();
        _settings.SingBoxSecret = GetString(root, "singBoxSecret", _settings.SingBoxSecret);
        _settings.ActiveCorePath = GetString(root, "corePath", _settings.ActiveCorePath).Trim();
        _settings.ActiveConfigPath = GetString(root, "configPath", _settings.ActiveConfigPath).Trim();
        _settings.ActiveDashboardApiUrl = GetString(root, "apiUrl", _settings.ActiveDashboardApiUrl).Trim();
        _settings.ActiveSecret = GetString(root, "secret", _settings.ActiveSecret);
        _settings.StartCoreOnLaunch = GetBool(root, "startCoreOnLaunch", _settings.StartCoreOnLaunch);
        _settings.MinimizeToTray = GetBool(root, "minimizeToTray", _settings.MinimizeToTray);
        _settings.LightweightMode = GetBool(root, "lightweightMode", _settings.LightweightMode);
        if (root.TryGetProperty("autostart", out var autostart) && autostart.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _settings.Autostart = autostart.GetBoolean();
            AutostartManager.SetEnabled(_settings.Autostart);
        }
        _settings.Save();
        RefreshIconCache();

        if (showMessage)
        {
            _ = ShowDashboardNoticeAsync("设置已保存。");
        }
    }

    private static string GetString(JsonElement root, string propertyName, string fallback)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
    }

    private static bool GetBool(JsonElement root, string propertyName, bool fallback)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : fallback;
    }

    private void StartCore(bool showTrayNotification = false)
    {
        try
        {
            if (!_core.IsRunning && !IsRunningAsAdministrator())
            {
                _elevatedRetryPending = false;
                _tunPermissionFailureSeen = false;
                _ = ShowDashboardNoticeAsync("启动内核需要管理员权限，正在请求 UAC 提权启动。");
                RelaunchAsAdministrator(startCore: true, startMinimized: ShouldKeepMinimizedForRelaunch(), elevatedRestart: true);
                return;
            }

            _elevatedRetryPending = !IsRunningAsAdministrator();
            _tunPermissionFailureSeen = false;
            _core.Start(_settings);
            if (showTrayNotification)
            {
                _trayIcon.ShowBalloonTip(1800, "Dashboard", "内核已启动", ToolTipIcon.Info);
            }

            RefreshIconCache();
            _ = WaitForApiAndNotifyAsync();
        }
        catch (Exception ex)
        {
            _elevatedRetryPending = false;
            _tunPermissionFailureSeen = false;
            MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SendStateToDashboard();
        }
    }

    private void StopCore()
    {
        StopCore(showTrayNotification: false);
    }

    private void StopCore(bool showTrayNotification)
    {
        var wasRunning = _core.IsRunning;
        try
        {
            _core.Stop();
            if (showTrayNotification && wasRunning)
            {
                _trayIcon.ShowBalloonTip(1800, "Dashboard", "内核已关闭", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "停止失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SendStateToDashboard();
        }
    }

    private void RestartCore(bool showTrayNotification = false)
    {
        try
        {
            if (!_core.IsRunning)
            {
                StartCore();
                return;
            }

            _core.Stop();
            StartCore();
            _ = ShowDashboardNoticeAsync("内核已重启。");
            if (showTrayNotification)
            {
                _trayIcon.ShowBalloonTip(1800, "Dashboard", "内核已重启", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "重启失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SendStateToDashboard();
        }
    }

    private async Task SwitchCoreAsync(JsonElement root)
    {
        if (_coreSwitchInProgress || _coreUpgradeInProgress)
        {
            return;
        }

        var targetCoreType = AppSettings.NormalizeCoreType(GetString(
            root,
            "targetCoreType",
            _settings.IsSingBox ? AppSettings.CoreTypeMihomo : AppSettings.CoreTypeSingBox));
        var targetTitle = string.Equals(targetCoreType, AppSettings.CoreTypeSingBox, StringComparison.Ordinal)
            ? "sing-box"
            : "Mihomo Core";

        _coreSwitchInProgress = true;
        SendStateToDashboard();
        await ShowDashboardNoticeAsync($"正在切换到 {targetTitle}。");

        try
        {
            if (_core.IsRunning)
            {
                _core.Stop(TimeSpan.FromSeconds(8));
                await Task.Delay(600);
            }

            _settings.CoreType = targetCoreType;
            _settings.Save();
            RefreshIconCache();
            StartCore();

            if (_core.IsRunning)
            {
                await ShowDashboardNoticeAsync($"已切换到 {targetTitle}。");
            }
        }
        catch (Exception ex)
        {
            await ShowDashboardNoticeAsync($"切换内核失败：{ex.Message}");
        }
        finally
        {
            _coreSwitchInProgress = false;
            SendStateToDashboard();
        }
    }

    private async Task UpgradeCoreAsync()
    {
        if (_coreUpgradeInProgress)
        {
            return;
        }

        var wasRunning = _core.IsRunning;
        var stoppedForUpgrade = false;
        _coreUpgradeInProgress = true;
        SendStateToDashboard();
        await ShowDashboardNoticeAsync(_settings.IsSingBox
            ? "正在升级 sing-box 内核，请稍候。"
            : "正在升级内核，请稍候。");

        try
        {
            var result = _settings.IsSingBox
                ? await SingBoxUpdater.UpgradeLatestAsync(_settings.SingBoxCorePath, beforeReplace: StopRunningCoreForUpgrade)
                : await CoreUpdater.UpgradeLatestAsync(_settings.CorePath, beforeReplace: StopRunningCoreForUpgrade);

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
                await ShowDashboardNoticeAsync($"当前内核已经是最新版本（{result.Version}）。");
                return;
            }

            await ShowDashboardNoticeAsync($"内核已升级到 {result.Version}。");

            if (stoppedForUpgrade)
            {
                StartCore();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "升级内核失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (stoppedForUpgrade && !_core.IsRunning)
            {
                StartCore();
            }
        }
        finally
        {
            _coreUpgradeInProgress = false;
            SendStateToDashboard();
        }
    }

    private async Task WaitForApiAndNotifyAsync()
    {
        using var client = new HttpClient();
        var apiUrl = _settings.ActiveDashboardApiUrl;
        var secret = _settings.ActiveSecret;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        }

        var endpoint = $"{apiUrl.TrimEnd('/')}/version";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(endpoint);
                if (response.IsSuccessStatusCode)
                {
                    _elevatedRetryPending = false;
                    _tunPermissionFailureSeen = false;
                    RefreshIconCache();
                    BeginInvoke(new Action(SendStateToDashboard));
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(500);
        }

        _elevatedRetryPending = false;
        _tunPermissionFailureSeen = false;

        BeginInvoke(new Action(() =>
        {
            _ = ShowDashboardNoticeAsync($"内核已启动，但无法连接 API：{apiUrl}");
        }));
    }

    private void RefreshStatus()
    {
        var running = _core.IsRunning;
        _trayIcon.Text = running ? "Dashboard - 运行中" : "Dashboard - 未运行";
        SendStateToDashboard();
    }

    private void QueueStateRefresh(string? logEntry = null)
    {
        if (_elevatedRetryPending && IsTunPermissionFailure(logEntry))
        {
            _tunPermissionFailureSeen = true;
        }

        _stateRefreshPending = true;
        if (ShouldHoldDashboardUpdates() && !_elevatedRetryPending)
        {
            _dashboardStateDirty = true;
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = (now - _lastStateRefresh).TotalMilliseconds;

        // 如果距离上次刷新太近，延迟刷新
        if (elapsed < MinRefreshIntervalMs)
        {
            if (!_stateRefreshTimer.Enabled)
            {
                _stateRefreshTimer.Interval = Math.Max(50, MinRefreshIntervalMs - (int)elapsed);
                _stateRefreshTimer.Start();
            }
        }
        else if (elapsed > MaxRefreshDelayMs)
        {
            // 如果太久没刷新，立即刷新
            RefreshStateNow();
        }
        else
        {
            // 正常防抖
            if (!_stateRefreshTimer.Enabled)
            {
                _stateRefreshTimer.Interval = MinRefreshIntervalMs;
                _stateRefreshTimer.Start();
            }
        }
    }

    private void RefreshStateNow()
    {
        _stateRefreshTimer.Stop();
        _stateRefreshPending = false;
        _lastStateRefresh = DateTime.UtcNow;

        if (ShouldHoldDashboardUpdates())
        {
            _dashboardStateDirty = true;
        }
        else
        {
            SendStateToDashboard();
        }

        HandleTunPermissionFailure();
    }

    private void HandleTunPermissionFailure()
    {
        if (!_elevatedRetryPending || !_tunPermissionFailureSeen)
        {
            return;
        }

        _elevatedRetryPending = false;
        _tunPermissionFailureSeen = false;
        StopCore();
        _ = ShowDashboardNoticeAsync("TUN 启动需要管理员权限，正在请求 UAC 提权启动内核。");
        RelaunchAsAdministrator(startCore: true, startMinimized: ShouldKeepMinimizedForRelaunch(), elevatedRestart: true);
    }

    private static bool IsTunPermissionFailure(string? logEntry)
    {
        return !string.IsNullOrWhiteSpace(logEntry)
            && (logEntry.Contains("Start TUN listening error", StringComparison.OrdinalIgnoreCase)
                || logEntry.Contains("configure tun interface: Access is denied", StringComparison.OrdinalIgnoreCase));
    }

    private void SendStateToDashboard()
    {
        if (!HasDashboardWebView())
        {
            _dashboardStateDirty = true;
            return;
        }

        if (ShouldHoldDashboardUpdates())
        {
            _dashboardStateDirty = true;
            return;
        }

        var state = new
        {
            isRunning = _core.IsRunning,
            processId = _core.ProcessId,
            coreType = _settings.CoreType,
            coreTitle = _settings.CoreTitle,
            corePath = _settings.ActiveCorePath,
            configPath = _settings.ActiveConfigPath,
            apiUrl = _settings.ActiveDashboardApiUrl,
            secret = _settings.ActiveSecret,
            mihomoCorePath = _settings.CorePath,
            mihomoConfigPath = _settings.ConfigPath,
            mihomoApiUrl = _settings.DashboardApiUrl,
            mihomoSecret = _settings.Secret,
            singBoxCorePath = _settings.SingBoxCorePath,
            singBoxConfigPath = _settings.SingBoxConfigPath,
            singBoxApiUrl = _settings.SingBoxApiUrl,
            singBoxSecret = _settings.SingBoxSecret,
            readOnlyTunEnabled = _settings.IsSingBox ? IsSingBoxTunConfigured() : (bool?)null,
            startCoreOnLaunch = _settings.StartCoreOnLaunch,
            minimizeToTray = _settings.MinimizeToTray,
            lightweightMode = _settings.LightweightMode,
            autostart = _settings.Autostart,
            canUpgradeCore = true,
            isCoreUpgrading = _coreUpgradeInProgress,
            isCoreSwitching = _coreSwitchInProgress,
            isWindowMaximized = WindowState == FormWindowState.Maximized,
            logText = _core.GetLogTail(8000),
            iconCacheMap = _iconCache.GetDashboardMap(_dashboardUri)
        };
        PostDashboardMessage(new { type = "state", state });
        _dashboardStateDirty = false;

        if (!string.IsNullOrWhiteSpace(_pendingDashboardNotice))
        {
            var message = _pendingDashboardNotice;
            _pendingDashboardNotice = null;
            PostDashboardMessage(new { type = "notice", message });
        }
    }

    private bool IsSingBoxTunConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.SingBoxConfigPath) || !File.Exists(_settings.SingBoxConfigPath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(_settings.SingBoxConfigPath);
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
                if (inbound.ValueKind != JsonValueKind.Object
                    || !inbound.TryGetProperty("type", out var typeProperty)
                    || typeProperty.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!string.Equals(typeProperty.GetString(), "tun", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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
        catch
        {
        }

        return false;
    }

    private Task ShowDashboardNoticeAsync(string message)
    {
        if (ShouldHoldDashboardUpdates())
        {
            _pendingDashboardNotice = message;
            return Task.CompletedTask;
        }

        if (!HasDashboardWebView())
        {
            _pendingDashboardNotice = message;
            return Task.CompletedTask;
        }

        PostDashboardMessage(new { type = "notice", message });
        return Task.CompletedTask;
    }

    private void SendWindowChromeState()
    {
        if (!HasDashboardWebView() || ShouldHoldDashboardUpdates())
        {
            return;
        }

        PostDashboardMessage(new
        {
            type = "windowState",
            isMaximized = WindowState == FormWindowState.Maximized
        });
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
        _stateRefreshTimer.Stop();
        _dashboardStateDirty = true;

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
        _dashboardSuspendVersion++;
        _stateRefreshTimer.Stop();
        _stateRefreshPending = false;
        _dashboardStateDirty = true;
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

    private void FlushDashboardUpdates()
    {
        if (ShouldHoldDashboardUpdates())
        {
            return;
        }

        if (_stateRefreshPending || _dashboardStateDirty)
        {
            _stateRefreshTimer.Stop();
            _stateRefreshPending = false;
            SendStateToDashboard();
            HandleTunPermissionFailure();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingDashboardNotice))
        {
            var message = _pendingDashboardNotice;
            _pendingDashboardNotice = null;
            PostDashboardMessage(new { type = "notice", message });
        }
    }

    private void PostDashboardMessage(object message)
    {
        var coreWebView = _webView?.CoreWebView2;
        if (coreWebView is null)
        {
            return;
        }

        coreWebView.PostWebMessageAsJson(JsonSerializer.Serialize(message));
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
            _settings.ActiveCorePath = dialog.FileName;
            _settings.Save();
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
            _settings.ActiveConfigPath = dialog.FileName;
            _settings.Save();
            RefreshIconCache();
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

    private void RefreshIconCache()
    {
        if (_settings.IsSingBox)
        {
            return;
        }

        var configPath = _settings.ConfigPath;
        _ = Task.Run(async () =>
        {
            try
            {
                await _iconCache.RefreshAsync(configPath);
            }
            catch
            {
            }
        });
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private bool ShouldKeepMinimizedForRelaunch()
    {
        return _startMinimized
            || _hiddenToTray
            || !Visible
            || !ShowInTaskbar
            || WindowState == FormWindowState.Minimized;
    }

    private void RelaunchAsAdministrator(bool startCore, bool startMinimized, bool elevatedRestart)
    {
        try
        {
            var arguments = new List<string>();
            if (startCore)
            {
                arguments.Add("--start-core");
            }
            if (startMinimized)
            {
                arguments.Add("--minimized");
            }
            if (elevatedRestart)
            {
                arguments.Add("--elevated-restart");
            }

            var startInfo = new ProcessStartInfo(Application.ExecutablePath, string.Join(" ", arguments))
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(startInfo);
            _allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法以管理员权限重启：{ex.Message}", "管理员重启失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        if (!_allowClose && _settings.MinimizeToTray && ShouldHideToTrayOnClose(e.CloseReason))
        {
            e.Cancel = true;
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
        _trayMenu?.Close();

        try
        {
            _hiddenToTray = true;
            if (animate && Visible && WindowState != FormWindowState.Minimized)
            {
                ShowInTaskbar = true;
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

            ShowInTaskbar = false;
            Hide();
            if (_settings.LightweightMode)
            {
                DisposeDashboardView();
            }
        }
        finally
        {
            _trayTransitionInProgress = false;
        }
    }

    public void ShowFromTray()
    {
        if (_trayTransitionInProgress)
        {
            return;
        }

        _trayMenu?.Close();
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
            ShowInTaskbar = true;

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

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _core.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _stateRefreshTimer.Dispose();
            _trayMenu?.Dispose();
            _trayIconImage.Dispose();
            _appIcon.Dispose();
            _dashboardServer.Dispose();
            DisposeDashboardView();
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

    private static Icon LoadTrayIcon(Icon fallback)
    {
        var iconPath = Path.Combine(AppSettings.AppDirectory, "resources", "tray.ico");
        return File.Exists(iconPath)
            ? new Icon(iconPath)
            : (Icon)fallback.Clone();
    }

    private void SyncAutostartSetting()
    {
        var registryEnabled = AutostartManager.IsEnabled();
        if (_settings.Autostart)
        {
            AutostartManager.SetEnabled(true);
            return;
        }

        if (registryEnabled)
        {
            _settings.Autostart = true;
            _settings.Save();
        }
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
