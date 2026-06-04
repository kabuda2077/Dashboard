using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MihomoDashboard;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly MihomoManager _mihomo = new();
    private readonly DashboardServer _dashboardServer;
    private readonly Uri _dashboardUri;
    private readonly Icon _appIcon;
    private readonly Icon _trayIconImage;
    private readonly NotifyIcon _trayIcon;
    private readonly WebView2 _webView = new();
    private TrayMenuForm? _trayMenu;
    private Rectangle _trayRestoreBounds;
    private FormWindowState _trayRestoreWindowState = FormWindowState.Normal;
    private bool _hiddenToTray;
    private bool _trayTransitionInProgress;
    private bool _allowClose;
    private bool _initialized;
    private bool _startMinimized;
    private bool _startCoreAfterLaunch;
    private bool _coreUpgradeInProgress;

    public MainForm(bool startMinimized, bool startCoreAfterLaunch)
    {
        _startMinimized = startMinimized;
        _startCoreAfterLaunch = startCoreAfterLaunch;
        _settings = AppSettings.Load();
        SyncAutostartSetting();
        _dashboardServer = new DashboardServer(Path.Combine(AppContext.BaseDirectory, "resources", "dashboard"));
        _dashboardUri = _dashboardServer.Start();

        Text = "Mihomo Dashboard";
        FormBorderStyle = FormBorderStyle.Sizable;
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
        await InitializeWebViewAsync();
        LoadDashboard();
        RefreshStatus();

        if (_startMinimized)
        {
            HideToTray();
        }

        if (_settings.StartCoreOnLaunch || _startCoreAfterLaunch)
        {
            StartCore();
        }
    }

    private void BuildLayout()
    {
        _webView.Dock = DockStyle.Fill;
        _webView.Margin = Padding.Empty;
        Controls.Add(_webView);
    }

    private void BindEvents()
    {
        _mihomo.StatusChanged += (_, _) => BeginInvoke(new Action(RefreshStatus));
        _mihomo.LogReceived += (_, _) => BeginInvoke(new Action(SendStateToDashboard));
    }

    private NotifyIcon CreateTrayIcon()
    {
        var icon = new NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "Mihomo Dashboard",
            Visible = true
        };
        icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowFromTray();
            }
            else if (e.Button == MouseButtons.Right)
            {
                ShowTrayMenu(Cursor.Position);
            }
        };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private void ShowTrayMenu(Point location)
    {
        _trayMenu?.Close();

        var isRunning = _mihomo.IsRunning;
        _trayMenu = new TrayMenuForm(new[]
        {
            new TrayMenuItem("显示窗口", ShowFromTray),
            new TrayMenuItem("启动内核", StartCore, Enabled: !isRunning && !_coreUpgradeInProgress),
            new TrayMenuItem("重启内核", RestartCore, Enabled: isRunning && !_coreUpgradeInProgress),
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

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                SendStateToDashboard();
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"WebView2 初始化失败：{ex.Message}", "缺少 WebView2 Runtime", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadDashboard()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var uri = new Uri(_dashboardUri, $"?{BuildDashboardQuery()}#/core");
        _webView.CoreWebView2.Navigate(uri.ToString());
    }

    private string BuildDashboardQuery()
    {
        var query = new List<string>();
        if (Uri.TryCreate(_settings.DashboardApiUrl, UriKind.Absolute, out var apiUri))
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

        query.Add($"secret={Uri.EscapeDataString(_settings.Secret)}");
        query.Add("label=Mihomo%20Dashboard");
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
                case "stop":
                    StopCore();
                    break;
                case "upgradeCore":
                    SaveSettingsFromMessage(root, showMessage: false);
                    await UpgradeCoreAsync();
                    break;
                case "reload":
                    SaveSettingsFromMessage(root, showMessage: false);
                    if (_mihomo.IsRunning)
                    {
                        _ = WaitForApiAndNotifyAsync();
                    }
                    else
                    {
                        await ShowDashboardNoticeAsync("请先启动内核，启动成功后即可使用面板。");
                    }
                    break;
                case "browseCore":
                    BrowseCorePath();
                    break;
                case "browseConfig":
                    BrowseConfigPath();
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
        _settings.CorePath = GetString(root, "corePath", _settings.CorePath).Trim();
        _settings.ConfigPath = GetString(root, "configPath", _settings.ConfigPath).Trim();
        _settings.DashboardApiUrl = GetString(root, "apiUrl", _settings.DashboardApiUrl).Trim();
        _settings.Secret = GetString(root, "secret", _settings.Secret);
        _settings.StartCoreOnLaunch = GetBool(root, "startCoreOnLaunch", _settings.StartCoreOnLaunch);
        _settings.MinimizeToTray = GetBool(root, "minimizeToTray", _settings.MinimizeToTray);
        if (root.TryGetProperty("autostart", out var autostart) && autostart.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _settings.Autostart = autostart.GetBoolean();
            AutostartManager.SetEnabled(_settings.Autostart);
        }
        _settings.Save();

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

    private void StartCore()
    {
        try
        {
            if (!IsRunningAsAdministrator())
            {
                RelaunchAsAdministrator(startCore: true, startMinimized: ShouldKeepMinimizedForRelaunch());
                return;
            }

            _mihomo.Start(_settings);
            _ = WaitForApiAndNotifyAsync();
        }
        catch (Exception ex)
        {
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
        var wasRunning = _mihomo.IsRunning;
        try
        {
            _mihomo.Stop();
            if (showTrayNotification && wasRunning)
            {
                _trayIcon.ShowBalloonTip(1800, "Mihomo Dashboard", "内核已关闭", ToolTipIcon.Info);
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

    private void RestartCore()
    {
        try
        {
            if (!_mihomo.IsRunning)
            {
                StartCore();
                return;
            }

            _mihomo.Stop();
            StartCore();
            _ = ShowDashboardNoticeAsync("内核已重启。");
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

    private async Task UpgradeCoreAsync()
    {
        if (_coreUpgradeInProgress)
        {
            return;
        }

        var wasRunning = _mihomo.IsRunning;
        _coreUpgradeInProgress = true;
        SendStateToDashboard();
        await ShowDashboardNoticeAsync("正在升级内核，请稍候。");

        try
        {
            if (wasRunning)
            {
                _mihomo.Stop();
            }

            var result = await CoreUpdater.UpgradeLatestAsync(_settings.CorePath);
            await ShowDashboardNoticeAsync($"内核已升级到 {result.Version}。");
            _trayIcon.ShowBalloonTip(2200, "Mihomo Dashboard", "内核已升级", ToolTipIcon.Info);

            if (wasRunning)
            {
                StartCore();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "升级内核失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (wasRunning && !_mihomo.IsRunning)
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
        if (!string.IsNullOrWhiteSpace(_settings.Secret))
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.Secret);
        }

        var endpoint = $"{_settings.DashboardApiUrl.TrimEnd('/')}/version";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(endpoint);
                if (response.IsSuccessStatusCode)
                {
                    BeginInvoke(new Action(SendStateToDashboard));
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(500);
        }

        BeginInvoke(new Action(() =>
        {
            _ = ShowDashboardNoticeAsync($"内核已启动，但无法连接 API：{_settings.DashboardApiUrl}");
        }));
    }

    private void RefreshStatus()
    {
        var running = _mihomo.IsRunning;
        _trayIcon.Text = running ? "Mihomo Dashboard - 运行中" : "Mihomo Dashboard - 未运行";
        SendStateToDashboard();
    }

    private void SendStateToDashboard()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var state = new
        {
            isRunning = _mihomo.IsRunning,
            processId = _mihomo.ProcessId,
            corePath = _settings.CorePath,
            configPath = _settings.ConfigPath,
            apiUrl = _settings.DashboardApiUrl,
            secret = _settings.Secret,
            startCoreOnLaunch = _settings.StartCoreOnLaunch,
            minimizeToTray = _settings.MinimizeToTray,
            autostart = _settings.Autostart,
            isCoreUpgrading = _coreUpgradeInProgress,
            logText = TrimLog(_mihomo.LogText)
        };
        var json = JsonSerializer.Serialize(state);
        _ = _webView.CoreWebView2.ExecuteScriptAsync(
            $"window.__mihomoApplyBackend && window.__mihomoApplyBackend({json}); window.__mihomoControlSetState && window.__mihomoControlSetState({json}); window.__mihomoStartupSetState && window.__mihomoStartupSetState({json});");
    }

    private static string TrimLog(string log)
    {
        const int maxLength = 8000;
        return log.Length <= maxLength ? log : log[^maxLength..];
    }

    private async Task ShowDashboardNoticeAsync(string message)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var text = JsonSerializer.Serialize(message);
        await _webView.CoreWebView2.ExecuteScriptAsync($"window.__mihomoControlNotice ? window.__mihomoControlNotice({text}) : alert({text});");
    }

    private void BrowseCorePath()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 mihomo.exe",
            Filter = "Mihomo executable|mihomo*.exe;clash*.exe|Executable|*.exe|All files|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.CorePath = dialog.FileName;
            _settings.Save();
        }
    }

    private void BrowseConfigPath()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 config.yaml",
            Filter = "YAML config|*.yaml;*.yml|All files|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.ConfigPath = dialog.FileName;
            _settings.Save();
        }
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

    private void RelaunchAsAdministrator(bool startCore, bool startMinimized)
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
        if (WindowState == FormWindowState.Normal)
        {
            RememberTrayRestoreState();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (WindowState != FormWindowState.Minimized)
        {
            RememberTrayRestoreState();
        }

        if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray && !_trayTransitionInProgress)
        {
            HideToTray();
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
        if (!_allowClose && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnFormClosing(e);
    }

    private void HideToTray()
    {
        if (_hiddenToTray || _trayTransitionInProgress)
        {
            return;
        }

        RememberTrayRestoreState();
        _trayTransitionInProgress = true;
        _trayMenu?.Close();

        var previousOpacity = Opacity;
        try
        {
            Opacity = 0;
            ShowInTaskbar = false;
            Hide();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            _hiddenToTray = true;
        }
        finally
        {
            Opacity = previousOpacity;
            _trayTransitionInProgress = false;
        }
    }

    private void ShowFromTray()
    {
        _trayMenu?.Close();
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            Activate();
            return;
        }

        _trayTransitionInProgress = true;
        var previousOpacity = Opacity;
        try
        {
            Opacity = 0;
            WindowState = FormWindowState.Normal;
            if (!_trayRestoreBounds.IsEmpty)
            {
                Bounds = _trayRestoreBounds;
            }

            ShowInTaskbar = true;
            Show();
            if (_trayRestoreWindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            }

            _hiddenToTray = false;
            Activate();
            BeginInvoke(new Action(() => Opacity = previousOpacity));
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
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu?.Dispose();
            _trayIconImage.Dispose();
            _appIcon.Dispose();
            _mihomo.Dispose();
            _dashboardServer.Dispose();
            _webView.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "resources", "app.ico");
        return File.Exists(iconPath)
            ? new Icon(iconPath)
            : Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
    }

    private static Icon LoadTrayIcon(Icon fallback)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "resources", "tray.ico");
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

}
