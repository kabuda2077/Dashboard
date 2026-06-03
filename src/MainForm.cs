using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MihomoDashboard;

public sealed class MainForm : Form
{
    private const int TitleBarHeight = 38;
    private const int ResizeBorder = 8;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcHitTest = 0x0084;
    private const int HtCaption = 2;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int CsDropShadow = 0x00020000;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private readonly AppSettings _settings;
    private readonly MihomoManager _mihomo = new();
    private readonly DashboardServer _dashboardServer;
    private readonly Uri _dashboardUri;
    private readonly Icon _appIcon;
    private readonly NotifyIcon _trayIcon;
    private readonly WebView2 _webView = new();
    private WindowCaptionButton? _maximizeButton;
    private TrayMenuForm? _trayMenu;
    private bool _allowClose;
    private bool _initialized;
    private bool _startMinimized;
    private bool _startCoreAfterLaunch;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CsDropShadow;
            return cp;
        }
    }

    public MainForm(bool startMinimized, bool startCoreAfterLaunch)
    {
        _startMinimized = startMinimized;
        _startCoreAfterLaunch = startCoreAfterLaunch;
        _settings = AppSettings.Load();
        _dashboardServer = new DashboardServer(Path.Combine(AppContext.BaseDirectory, "resources", "dashboard"));
        _dashboardUri = _dashboardServer.Start();

        Text = "Mihomo Dashboard";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(244, 244, 245);
        MinimumSize = new Size(1120, 720);
        Size = new Size(1360, 840);
        StartPosition = FormStartPosition.CenterScreen;
        _appIcon = LoadAppIcon();
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

        if (_settings.StartCoreOnLaunch || _startCoreAfterLaunch)
        {
            StartCore();
        }

        if (_startMinimized)
        {
            HideToTray();
        }
    }

    private void BuildLayout()
    {
        var titleBar = CreateTitleBar();
        _webView.Dock = DockStyle.Fill;
        _webView.Margin = Padding.Empty;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleBarHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(titleBar, 0, 0);
        layout.Controls.Add(_webView, 0, 1);
        Controls.Add(layout);
    }

    private Control CreateTitleBar()
    {
        var titleBar = new Panel
        {
            BackColor = Color.FromArgb(244, 244, 245),
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(12, 0, 0, 0)
        };
        titleBar.MouseDown += (_, e) => BeginWindowDrag(e);
        titleBar.DoubleClick += (_, _) => ToggleMaximize();

        var appIcon = new PictureBox
        {
            Image = _appIcon.ToBitmap(),
            Size = new Size(18, 18),
            SizeMode = PictureBoxSizeMode.StretchImage,
            Location = new Point(11, 10)
        };
        appIcon.MouseDown += (_, e) => BeginWindowDrag(e);
        titleBar.Controls.Add(appIcon);

        var title = new Label
        {
            Text = Text,
            AutoEllipsis = true,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(39, 39, 42),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            Location = new Point(38, 0),
            Size = new Size(280, TitleBarHeight),
            TextAlign = ContentAlignment.MiddleLeft
        };
        title.MouseDown += (_, e) => BeginWindowDrag(e);
        title.DoubleClick += (_, _) => ToggleMaximize();
        titleBar.Controls.Add(title);

        var minimizeButton = new WindowCaptionButton(WindowCaptionButtonKind.Minimize);
        minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;

        _maximizeButton = new WindowCaptionButton(WindowCaptionButtonKind.Maximize);
        _maximizeButton.Click += (_, _) => ToggleMaximize();

        var closeButton = new WindowCaptionButton(WindowCaptionButtonKind.Close);
        closeButton.Click += (_, _) => Close();

        minimizeButton.Dock = DockStyle.Right;
        _maximizeButton.Dock = DockStyle.Right;
        closeButton.Dock = DockStyle.Right;
        titleBar.Controls.Add(minimizeButton);
        titleBar.Controls.Add(_maximizeButton);
        titleBar.Controls.Add(closeButton);

        return titleBar;
    }

    private void BeginWindowDrag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
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
            Icon = _appIcon,
            Text = "Mihomo Dashboard",
            Visible = true
        };
        icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
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
        _trayMenu = new TrayMenuForm(_appIcon, isRunning, new[]
        {
            new TrayMenuItem("显示窗口", ShowFromTray),
            new TrayMenuItem("启动内核", StartCore, Enabled: !isRunning),
            new TrayMenuItem("停止内核", StopCore, Enabled: isRunning),
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

    private void ShowStartupPage()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        _webView.CoreWebView2.NavigateToString("""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Mihomo Core</title>
  <style>
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      background: #f7f7f8;
      color: #18181b;
      font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      display: grid;
      place-items: center;
      padding: 28px;
    }
    .shell { width: min(980px, 100%); }
    .hero, .card {
      background: #fff;
      border-radius: 18px;
      box-shadow: 0 1px 2px rgba(15,23,42,.06);
    }
    .hero {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 18px;
      padding: 24px;
      margin-bottom: 16px;
    }
    .heading { display: flex; align-items: center; gap: 12px; }
    .dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: #f97316;
      box-shadow: 0 0 0 4px rgba(249,115,22,.16);
    }
    body[data-running="true"] .dot {
      background: #22c55e;
      box-shadow: 0 0 0 4px rgba(34,197,94,.16);
    }
    h1 { margin: 0; font-size: 28px; }
    h2 { margin: 0 0 14px; font-size: 15px; }
    .sub { margin-top: 6px; color: #71717a; font-size: 14px; }
    .actions { display: flex; gap: 10px; flex-wrap: wrap; justify-content: flex-end; }
    button {
      height: 36px;
      border: 0;
      border-radius: 10px;
      padding: 0 14px;
      font: inherit;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      background: #18181b;
      color: #fff;
    }
    button.secondary { background: #f4f4f5; color: #27272a; }
    button.danger { background: #fff7ed; color: #c2410c; }
    .grid {
      display: grid;
      grid-template-columns: minmax(0, 1.2fr) minmax(280px, .8fr);
      gap: 16px;
    }
    .card { padding: 18px; }
    .note {
      padding: 12px;
      border-radius: 12px;
      background: #fff7ed;
      color: #9a3412;
      font-size: 13px;
      line-height: 1.55;
      margin-bottom: 12px;
    }
    label {
      display: block;
      margin-bottom: 6px;
      color: #71717a;
      font-size: 12px;
      font-weight: 700;
    }
    .field {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 8px;
      margin-bottom: 12px;
    }
    input[type="text"] {
      width: 100%;
      height: 34px;
      border: 1px solid #e4e4e7;
      border-radius: 10px;
      padding: 0 10px;
      outline: none;
      background: #fafafa;
      color: #18181b;
      font-size: 12px;
    }
    .options { display: grid; gap: 8px; margin-top: 8px; color: #3f3f46; font-size: 12px; }
    .options label { display: flex; align-items: center; gap: 8px; margin: 0; color: inherit; font-weight: 600; }
    .log {
      height: 260px;
      overflow: auto;
      margin: 0;
      padding: 10px;
      border-radius: 10px;
      background: #0f172a;
      color: #dbeafe;
      font: 11px/1.45 "Cascadia Mono", Consolas, monospace;
      white-space: pre-wrap;
    }
    .toast {
      display: none;
      margin-top: 12px;
      padding: 9px 10px;
      border-radius: 10px;
      background: #ecfeff;
      color: #0e7490;
      font-size: 12px;
      font-weight: 700;
    }
    .toast.show { display: block; }
    @media (max-width: 900px) { .grid { grid-template-columns: 1fr; } .hero { align-items: flex-start; flex-direction: column; } }
  </style>
</head>
<body>
  <main class="shell">
    <section class="hero">
      <div class="heading">
        <span class="dot"></span>
        <div>
          <h1>Mihomo Core</h1>
          <div class="sub" data-role="status">未运行</div>
        </div>
      </div>
      <div class="actions">
        <button data-action="start">启动内核</button>
        <button class="danger" data-action="stop">停止内核</button>
      </div>
    </section>
    <section class="grid">
      <div class="card">
        <h2>启动配置</h2>
        <div class="note">如果配置启用了 TUN，启动内核时会自动请求管理员权限。看到 UAC 提示后允许即可。</div>
        <label>内核路径</label>
        <div class="field"><input type="text" data-field="corePath" /><button class="secondary" data-action="browseCore">选择</button></div>
        <label>配置文件</label>
        <div class="field"><input type="text" data-field="configPath" /><button class="secondary" data-action="browseConfig">选择</button></div>
        <label>API 地址</label>
        <div class="field"><input type="text" data-field="apiUrl" /><button class="secondary" data-action="reload">进入面板</button></div>
        <label>Secret</label>
        <div class="field"><input type="text" data-field="secret" /><button data-action="save">保存</button></div>
        <div class="options">
          <label><input type="checkbox" data-field="startCoreOnLaunch" /> 启动软件时自动启动内核</label>
          <label><input type="checkbox" data-field="minimizeToTray" /> 关闭窗口时最小化到托盘</label>
          <label><input type="checkbox" data-field="autostart" /> 开机自启</label>
        </div>
        <div class="toast" data-role="toast"></div>
      </div>
      <div class="card">
        <h2>内核日志</h2>
        <pre class="log" data-role="log">暂无日志</pre>
      </div>
    </section>
  </main>
  <script>
    const post = (message) => window.chrome?.webview?.postMessage(message);
    const collect = () => ({
      type: 'save',
      corePath: document.querySelector('[data-field="corePath"]').value,
      configPath: document.querySelector('[data-field="configPath"]').value,
      apiUrl: document.querySelector('[data-field="apiUrl"]').value,
      secret: document.querySelector('[data-field="secret"]').value,
      startCoreOnLaunch: document.querySelector('[data-field="startCoreOnLaunch"]').checked,
      minimizeToTray: document.querySelector('[data-field="minimizeToTray"]').checked,
      autostart: document.querySelector('[data-field="autostart"]').checked
    });
    document.addEventListener('click', (event) => {
      const button = event.target.closest('button[data-action]');
      if (!button) return;
      const action = button.dataset.action;
      if (action === 'save') return post(collect());
      if (action === 'start') return post({ ...collect(), type: 'start' });
      if (action === 'stop') return post({ type: 'stop' });
      if (action === 'reload') return post({ ...collect(), type: 'reload' });
      if (action === 'browseCore') return post({ type: 'browseCore' });
      if (action === 'browseConfig') return post({ type: 'browseConfig' });
    });
    window.__mihomoStartupSetState = (state) => {
      document.body.dataset.running = state.isRunning ? 'true' : 'false';
      document.querySelector('[data-role="status"]').textContent = state.isRunning
        ? `运行中 · PID ${state.processId ?? ''}`
        : '未运行';
      document.querySelector('[data-action="start"]').disabled = state.isRunning;
      document.querySelector('[data-action="stop"]').disabled = !state.isRunning;
      document.querySelector('[data-field="corePath"]').value = state.corePath ?? '';
      document.querySelector('[data-field="configPath"]').value = state.configPath ?? '';
      document.querySelector('[data-field="apiUrl"]').value = state.apiUrl ?? '';
      document.querySelector('[data-field="secret"]').value = state.secret ?? '';
      document.querySelector('[data-field="startCoreOnLaunch"]').checked = !!state.startCoreOnLaunch;
      document.querySelector('[data-field="minimizeToTray"]').checked = !!state.minimizeToTray;
      document.querySelector('[data-field="autostart"]').checked = !!state.autostart;
      document.querySelector('[data-role="log"]').textContent = state.logText || '暂无日志';
    };
    window.__mihomoControlNotice = (message) => {
      const toast = document.querySelector('[data-role="toast"]');
      toast.textContent = message;
      toast.classList.add('show');
      window.clearTimeout(window.__mihomoNoticeTimer);
      window.__mihomoNoticeTimer = window.setTimeout(() => toast.classList.remove('show'), 2400);
    };
  </script>
</body>
</html>
""");
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
                case "stop":
                    StopCore();
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
        _settings.Save();

        if (root.TryGetProperty("autostart", out var autostart) && autostart.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            AutostartManager.SetEnabled(autostart.GetBoolean());
        }

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
                RelaunchAsAdministrator(startCore: true);
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
        try
        {
            _mihomo.Stop();
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
            autostart = AutostartManager.IsEnabled(),
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

    private void RelaunchAsAdministrator(bool startCore)
    {
        try
        {
            var arguments = startCore ? "--start-core" : "";
            var startInfo = new ProcessStartInfo(Application.ExecutablePath, arguments)
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryApplyWindowCorners();
        UpdateMaximizedBounds();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (WindowState == FormWindowState.Normal)
        {
            UpdateMaximizedBounds();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_maximizeButton is not null)
        {
            _maximizeButton.Kind = WindowState == FormWindowState.Maximized
                ? WindowCaptionButtonKind.Restore
                : WindowCaptionButtonKind.Maximize;
        }

        if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
        {
            HideToTray();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HtClient)
            {
                var hitTest = GetResizeHitTest(PointToClient(PointFromLParam(m.LParam)));
                if (hitTest != HtClient)
                {
                    m.Result = (IntPtr)hitTest;
                }
            }

            return;
        }

        base.WndProc(ref m);
    }

    private void UpdateMaximizedBounds()
    {
        if (IsHandleCreated)
        {
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
        }
    }

    private void TryApplyWindowCorners()
    {
        try
        {
            var preference = DwmwcpRound;
            _ = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref preference, Marshal.SizeOf<int>());
        }
        catch
        {
        }
    }

    private static Point PointFromLParam(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        return new Point(unchecked((short)(value & 0xffff)), unchecked((short)((value >> 16) & 0xffff)));
    }

    private int GetResizeHitTest(Point point)
    {
        var left = point.X < ResizeBorder;
        var right = point.X >= Width - ResizeBorder;
        var top = point.Y < ResizeBorder;
        var bottom = point.Y >= Height - ResizeBorder;

        if (top && left) return HtTopLeft;
        if (top && right) return HtTopRight;
        if (bottom && left) return HtBottomLeft;
        if (bottom && right) return HtBottomRight;
        if (left) return HtLeft;
        if (right) return HtRight;
        if (top) return HtTop;
        if (bottom) return HtBottom;
        return HtClient;
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
        Hide();
        ShowInTaskbar = false;
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
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

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, int wParam, int lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int attributeValue, int attributeSize);
}
