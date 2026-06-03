using System.Diagnostics;
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
    private readonly NotifyIcon _trayIcon;
    private readonly WebView2 _webView = new();
    private bool _allowClose;
    private bool _initialized;
    private bool _startMinimized;

    public MainForm(bool startMinimized)
    {
        _startMinimized = startMinimized;
        _settings = AppSettings.Load();
        _dashboardServer = new DashboardServer(Path.Combine(AppContext.BaseDirectory, "resources", "dashboard"));
        _dashboardUri = _dashboardServer.Start();

        Text = "Mihomo Dashboard";
        MinimumSize = new Size(1120, 720);
        Size = new Size(1360, 840);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

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
        RefreshStatus();

        if (_settings.StartCoreOnLaunch)
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
        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);
    }

    private void BindEvents()
    {
        _mihomo.StatusChanged += (_, _) => BeginInvoke(new Action(RefreshStatus));
        _mihomo.LogReceived += (_, _) => BeginInvoke(new Action(SendStateToDashboard));
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => ShowFromTray());
        menu.Items.Add("启动内核", null, (_, _) => StartCore());
        menu.Items.Add("停止内核", null, (_, _) => StopCore());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        var icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Mihomo Dashboard",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                await InjectCorePanelAsync();
                SendStateToDashboard();
            };
            LoadDashboard();
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

        var apiUrl = Uri.EscapeDataString(_settings.DashboardApiUrl.TrimEnd('/'));
        var secret = Uri.EscapeDataString(_settings.Secret);
        var uri = new Uri(_dashboardUri, $"?hostname={apiUrl}&secret={secret}");
        _webView.CoreWebView2.Navigate(uri.ToString());
    }

    private async Task InjectCorePanelAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        const string script = """
(() => {
  if (window.__mihomoControlInstalled) return;
  window.__mihomoControlInstalled = true;

  const css = document.createElement('style');
  css.textContent = `
    #mihomo-core-entry {
      width: calc(100% - 24px);
      min-height: 42px;
      margin: 8px 12px;
      border: 0;
      border-radius: 10px;
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 0 16px;
      background: transparent;
      color: #27272a;
      cursor: pointer;
      font: 700 14px Inter, ui-sans-serif, system-ui, "Segoe UI", sans-serif;
      text-align: left;
    }
    #mihomo-core-entry[data-active="true"] {
      background: #18181b;
      color: #fff;
    }
    #mihomo-core-entry .mc-dot,
    #mihomo-core-page .mc-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #f97316;
      box-shadow: 0 0 0 3px rgba(249, 115, 22, .16);
      flex: 0 0 auto;
    }
    #mihomo-core-entry[data-running="true"] .mc-dot,
    #mihomo-core-page[data-running="true"] .mc-dot {
      background: #22c55e;
      box-shadow: 0 0 0 3px rgba(34, 197, 94, .16);
    }
    #mihomo-core-page {
      position: fixed;
      left: var(--mc-sidebar-left, 306px);
      top: var(--mc-content-top, 58px);
      right: 0;
      bottom: 0;
      z-index: 2147483000;
      overflow: auto;
      padding: 22px;
      background: #f7f7f8;
      color: #18181b;
      font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
    }
    #mihomo-core-page[hidden] { display: none !important; }
    #mihomo-core-page * { box-sizing: border-box; }
    #mihomo-core-page .mc-wrap {
      max-width: 980px;
      margin: 0 auto;
    }
    #mihomo-core-page .mc-hero {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 18px;
      padding: 24px;
      border-radius: 18px;
      background: #fff;
      box-shadow: 0 1px 2px rgba(15,23,42,.06);
    }
    #mihomo-core-page .mc-heading {
      display: flex;
      align-items: center;
      gap: 12px;
    }
    #mihomo-core-page h1 {
      margin: 0;
      font-size: 26px;
      line-height: 1.2;
    }
    #mihomo-core-page .mc-sub {
      margin-top: 6px;
      color: #71717a;
      font-size: 14px;
    }
    #mihomo-core-page .mc-actions {
      display: flex;
      border: 0;
      gap: 10px;
      flex-wrap: wrap;
      justify-content: flex-end;
    }
    #mihomo-core-page button {
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
    #mihomo-core-page button.secondary {
      background: #f4f4f5;
      color: #27272a;
    }
    #mihomo-core-page button.danger {
      background: #fff7ed;
      color: #c2410c;
    }
    #mihomo-core-page button.warn {
      background: #fef3c7;
      color: #92400e;
    }
    #mihomo-core-page .mc-grid {
      display: grid;
      grid-template-columns: minmax(0, 1.2fr) minmax(280px, .8fr);
      gap: 16px;
      margin-top: 16px;
    }
    #mihomo-core-page .mc-card {
      border-radius: 16px;
      background: #fff;
      padding: 18px;
      box-shadow: 0 1px 2px rgba(15,23,42,.06);
    }
    #mihomo-core-page h2 {
      margin: 0 0 14px;
      font-size: 15px;
    }
    #mihomo-core-page label {
      display: block;
      margin-bottom: 6px;
      color: #71717a;
      font-size: 12px;
      font-weight: 700;
    }
    #mihomo-core-page .mc-field {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 8px;
      margin-bottom: 12px;
    }
    #mihomo-core-page input[type="text"] {
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
    #mihomo-core-page .mc-options {
      display: grid;
      gap: 8px;
      margin-top: 8px;
      color: #3f3f46;
      font-size: 12px;
    }
    #mihomo-core-page .mc-options label {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 0;
      color: inherit;
      font-weight: 600;
    }
    #mihomo-core-page .mc-log {
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
    #mihomo-core-page .mc-note {
      padding: 12px;
      border-radius: 12px;
      background: #fff7ed;
      color: #9a3412;
      font-size: 13px;
      line-height: 1.55;
      margin-bottom: 12px;
    }
    #mihomo-core-page .mc-toast {
      display: none;
      margin-top: 12px;
      padding: 9px 10px;
      border-radius: 10px;
      background: #ecfeff;
      color: #0e7490;
      font-size: 12px;
      font-weight: 700;
    }
    #mihomo-core-page .mc-toast.show { display: block; }
    @media (max-width: 900px) {
      #mihomo-core-page {
        left: 0;
        top: 56px;
        padding: 14px;
      }
      #mihomo-core-page .mc-grid {
        grid-template-columns: 1fr;
      }
    }
  `;
  document.head.appendChild(css);

  const navLabels = ['概览', '代理', '连接', '日志', '规则'];
  const findTextElement = (text) => [...document.querySelectorAll('a,button,div,span')]
    .find((element) => element.textContent?.trim() === text);
  const findNavContainer = () => {
    const overview = findTextElement('概览');
    let node = overview;
    while (node && node !== document.body) {
      const rect = node.getBoundingClientRect();
      const text = node.textContent || '';
      if (rect.width >= 160 && rect.width <= 360 && rect.left < 40 && text.includes('概览') && text.includes('代理')) {
        return node;
      }
      node = node.parentElement;
    }
    return document.querySelector('aside') || overview?.parentElement || document.body;
  };
  const clickableNavItem = (label) => {
    const element = findTextElement(label);
    return element?.closest('a,button,[role="button"]') || element?.parentElement || element;
  };

  const navContainer = findNavContainer();
  const coreEntry = document.createElement('button');
  coreEntry.id = 'mihomo-core-entry';
  coreEntry.type = 'button';
  coreEntry.innerHTML = `<span class="mc-dot"></span><span>内核</span>`;
  navContainer.insertBefore(coreEntry, navContainer.firstChild);

  const page = document.createElement('div');
  page.id = 'mihomo-core-page';
  page.innerHTML = `
    <div class="mc-wrap">
      <div class="mc-hero">
        <div>
          <div class="mc-heading">
            <span class="mc-dot"></span>
            <div>
              <h1>Mihomo Core</h1>
              <div class="mc-sub" data-role="status">未运行</div>
            </div>
          </div>
        </div>
        <div class="mc-actions">
          <button data-action="start">启动内核</button>
          <button class="danger" data-action="stop">停止内核</button>
          <button class="warn" data-action="restartAdmin">管理员重启</button>
        </div>
      </div>
      <div class="mc-grid">
        <div class="mc-card">
          <h2>启动配置</h2>
          <div class="mc-note">如果配置启用了 TUN，普通权限启动可能会出现 Access is denied。请点击“管理员重启”后再启动内核，或关闭配置里的 TUN。</div>
          <label>内核路径</label>
          <div class="mc-field">
            <input type="text" data-field="corePath" />
            <button class="secondary" data-action="browseCore">选择</button>
          </div>
          <label>配置文件</label>
          <div class="mc-field">
            <input type="text" data-field="configPath" />
            <button class="secondary" data-action="browseConfig">选择</button>
          </div>
          <label>API 地址</label>
          <div class="mc-field">
            <input type="text" data-field="apiUrl" />
            <button class="secondary" data-action="reload">刷新 UI</button>
          </div>
          <label>Secret</label>
          <div class="mc-field">
            <input type="text" data-field="secret" />
            <button data-action="save">保存</button>
          </div>
          <div class="mc-options">
            <label><input type="checkbox" data-field="startCoreOnLaunch" /> 启动软件时自动启动内核</label>
            <label><input type="checkbox" data-field="minimizeToTray" /> 关闭窗口时最小化到托盘</label>
            <label><input type="checkbox" data-field="autostart" /> 开机自启</label>
          </div>
          <div class="mc-toast" data-role="toast"></div>
        </div>
        <div class="mc-card">
          <h2>内核日志</h2>
          <pre class="mc-log" data-role="log">暂无日志</pre>
        </div>
      </div>
    </div>
  `;
  document.body.appendChild(page);

  let corePageActive = true;
  let lastRunning = false;
  const nativeItems = navLabels.map(clickableNavItem).filter(Boolean);
  const updateLayout = () => {
    const rect = navContainer.getBoundingClientRect();
    document.documentElement.style.setProperty('--mc-sidebar-left', `${Math.max(0, rect.right)}px`);
  };
  const showCorePage = (show) => {
    corePageActive = show;
    page.hidden = !show;
    coreEntry.dataset.active = show ? 'true' : 'false';
  };
  const updateNavigation = (running) => {
    nativeItems.forEach((item) => {
      item.style.display = running ? '' : 'none';
    });
    if (!running) showCorePage(true);
  };

  coreEntry.addEventListener('click', () => showCorePage(true));
  nativeItems.forEach((item) => item.addEventListener('click', () => showCorePage(false)));
  window.addEventListener('resize', updateLayout);
  updateLayout();

  const root = page;
  const post = (message) => window.chrome?.webview?.postMessage(message);
  const collect = () => ({
    type: 'save',
    corePath: root.querySelector('[data-field="corePath"]').value,
    configPath: root.querySelector('[data-field="configPath"]').value,
    apiUrl: root.querySelector('[data-field="apiUrl"]').value,
    secret: root.querySelector('[data-field="secret"]').value,
    startCoreOnLaunch: root.querySelector('[data-field="startCoreOnLaunch"]').checked,
    minimizeToTray: root.querySelector('[data-field="minimizeToTray"]').checked,
    autostart: root.querySelector('[data-field="autostart"]').checked
  });

  root.addEventListener('click', (event) => {
    const button = event.target.closest('button[data-action]');
    if (!button) return;
    const action = button.dataset.action;

    if (action === 'save') return post(collect());
    if (action === 'start') return post({ ...collect(), type: 'start' });
    if (action === 'stop') return post({ type: 'stop' });
    if (action === 'reload') return post({ ...collect(), type: 'reload' });
    if (action === 'browseCore') return post({ type: 'browseCore' });
    if (action === 'browseConfig') return post({ type: 'browseConfig' });
    if (action === 'restartAdmin') return post({ ...collect(), type: 'restartAdmin' });
  });

  window.__mihomoControlSetState = (state) => {
    page.dataset.running = state.isRunning ? 'true' : 'false';
    coreEntry.dataset.running = state.isRunning ? 'true' : 'false';
    updateNavigation(!!state.isRunning);
    root.querySelector('[data-role="status"]').textContent = state.isRunning
      ? `运行中 · PID ${state.processId ?? ''}`
      : '未运行';
    root.querySelector('[data-action="start"]').disabled = state.isRunning;
    root.querySelector('[data-action="stop"]').disabled = !state.isRunning;
    root.querySelector('[data-field="corePath"]').value = state.corePath ?? '';
    root.querySelector('[data-field="configPath"]').value = state.configPath ?? '';
    root.querySelector('[data-field="apiUrl"]').value = state.apiUrl ?? '';
    root.querySelector('[data-field="secret"]').value = state.secret ?? '';
    root.querySelector('[data-field="startCoreOnLaunch"]').checked = !!state.startCoreOnLaunch;
    root.querySelector('[data-field="minimizeToTray"]').checked = !!state.minimizeToTray;
    root.querySelector('[data-field="autostart"]').checked = !!state.autostart;
    root.querySelector('[data-role="log"]').textContent = state.logText || '暂无日志';
    if (!state.isRunning) {
      showCorePage(true);
    } else if (!lastRunning) {
      showCorePage(false);
      nativeItems[0]?.click();
    } else if (corePageActive) {
      showCorePage(true);
    }
    lastRunning = !!state.isRunning;
  };

  window.__mihomoControlNotice = (message) => {
    const toast = root.querySelector('[data-role="toast"]');
    toast.textContent = message;
    toast.classList.add('show');
    window.clearTimeout(window.__mihomoControlNoticeTimer);
    window.__mihomoControlNoticeTimer = window.setTimeout(() => toast.classList.remove('show'), 2400);
  };
})();
""";

        await _webView.CoreWebView2.ExecuteScriptAsync(script);
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
                    LoadDashboard();
                    break;
                case "browseCore":
                    BrowseCorePath();
                    break;
                case "browseConfig":
                    BrowseConfigPath();
                    break;
                case "restartAdmin":
                    SaveSettingsFromMessage(root, showMessage: false);
                    RelaunchAsAdministrator();
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
            _mihomo.Start(_settings);
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
        _ = _webView.CoreWebView2.ExecuteScriptAsync($"window.__mihomoControlSetState && window.__mihomoControlSetState({json});");
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

    private void RelaunchAsAdministrator()
    {
        try
        {
            var startInfo = new ProcessStartInfo(Application.ExecutablePath)
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
        {
            HideToTray();
        }
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
            _mihomo.Dispose();
            _dashboardServer.Dispose();
            _webView.Dispose();
        }

        base.Dispose(disposing);
    }
}
