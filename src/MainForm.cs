using Microsoft.Web.WebView2.WinForms;

namespace MihomoDashboard;

public sealed class MainForm : Form
{
    private static readonly Color ShellBackground = Color.FromArgb(14, 18, 24);
    private static readonly Color PanelBackground = Color.FromArgb(22, 27, 36);
    private static readonly Color CardBackground = Color.FromArgb(30, 37, 48);
    private static readonly Color InputBackground = Color.FromArgb(15, 19, 26);
    private static readonly Color TextPrimary = Color.FromArgb(241, 245, 249);
    private static readonly Color TextSecondary = Color.FromArgb(148, 163, 184);
    private static readonly Color Accent = Color.FromArgb(56, 189, 248);
    private static readonly Color Success = Color.FromArgb(52, 211, 153);
    private static readonly Color Warning = Color.FromArgb(251, 146, 60);

    private readonly AppSettings _settings;
    private readonly MihomoManager _mihomo = new();
    private readonly DashboardServer _dashboardServer;
    private readonly Uri _dashboardUri;
    private readonly NotifyIcon _trayIcon;
    private readonly WebView2 _webView = new();
    private readonly TextBox _corePathBox = new();
    private readonly TextBox _configPathBox = new();
    private readonly TextBox _apiUrlBox = new();
    private readonly TextBox _secretBox = new();
    private readonly TextBox _logBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly CheckBox _autostartBox = new();
    private readonly CheckBox _startCoreBox = new();
    private readonly CheckBox _minimizeToTrayBox = new();
    private SplitContainer? _rootSplit;
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
        Size = new Size(1280, 820);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;
        BackColor = ShellBackground;

        _trayIcon = CreateTrayIcon();
        BuildLayout();
        BindEvents();
        LoadSettingsToUi();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        ApplySplitLayout();
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
        var root = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            BackColor = ShellBackground,
            SplitterWidth = 1
        };
        _rootSplit = root;
        root.SizeChanged += (_, _) => ApplySplitLayout();

        Controls.Add(root);

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
            BackColor = PanelBackground
        };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Panel1.Controls.Add(left);

        var title = new Label
        {
            Text = "Mihomo",
            AutoSize = true,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Variable Display", 24, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 0)
        };
        left.Controls.Add(title, 0, 0);

        var subtitle = new Label
        {
            Text = "Core control and zashboard",
            AutoSize = true,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 9),
            Margin = new Padding(2, 0, 0, 18)
        };
        left.Controls.Add(subtitle, 0, 1);

        var settingsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 14),
            BackColor = CardBackground
        };
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        left.Controls.Add(settingsPanel, 0, 2);

        AddPathRow(settingsPanel, "内核路径", _corePathBox, BrowseCorePath);
        AddPathRow(settingsPanel, "配置文件", _configPathBox, BrowseConfigPath);
        AddTextRow(settingsPanel, "API 地址", _apiUrlBox);
        AddTextRow(settingsPanel, "Secret", _secretBox, password: false);

        StyleCheckBox(_startCoreBox);
        StyleCheckBox(_minimizeToTrayBox);
        StyleCheckBox(_autostartBox);
        _startCoreBox.Text = "启动软件时自动启动内核";
        _minimizeToTrayBox.Text = "关闭窗口时最小化到托盘";
        _autostartBox.Text = "开机自启";
        settingsPanel.Controls.Add(_startCoreBox, 0, settingsPanel.RowCount++);
        settingsPanel.SetColumnSpan(_startCoreBox, 3);
        settingsPanel.Controls.Add(_minimizeToTrayBox, 0, settingsPanel.RowCount++);
        settingsPanel.SetColumnSpan(_minimizeToTrayBox, 3);
        settingsPanel.Controls.Add(_autostartBox, 0, settingsPanel.RowCount++);
        settingsPanel.SetColumnSpan(_autostartBox, 3);

        var controlPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 0),
            Padding = new Padding(0)
        };
        left.Controls.Add(controlPanel, 0, 4);

        _startButton.Text = "启动内核";
        _startButton.Width = 100;
        _stopButton.Text = "停止内核";
        _stopButton.Width = 100;

        var saveButton = new Button { Text = "保存设置", Width = 100 };
        var reloadButton = new Button { Text = "刷新 UI", Width = 88 };
        var clearLogButton = new Button { Text = "清空日志", Width = 88 };

        StyleButton(_startButton, Accent, Color.FromArgb(3, 7, 18));
        StyleButton(_stopButton, Warning, Color.FromArgb(3, 7, 18));
        StyleButton(saveButton, Color.FromArgb(71, 85, 105), TextPrimary);
        StyleButton(reloadButton, Color.FromArgb(51, 65, 85), TextPrimary);
        StyleButton(clearLogButton, Color.FromArgb(51, 65, 85), TextPrimary);

        controlPanel.Controls.AddRange(new Control[] { _startButton, _stopButton, saveButton, reloadButton, clearLogButton });
        saveButton.Click += (_, _) => SaveSettingsFromUi(showMessage: true);
        reloadButton.Click += (_, _) => LoadDashboard();
        clearLogButton.Click += (_, _) =>
        {
            _mihomo.ClearLog();
            _logBox.Clear();
        };

        var logPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = ShellBackground,
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 14)
        };
        logPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Controls.Add(logPanel, 0, 3);
        left.SetRowSpan(logPanel, 1);

        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(12, 7, 12, 7);
        _statusLabel.Margin = new Padding(0, 0, 0, 10);
        _statusLabel.BackColor = CardBackground;
        _statusLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        logPanel.Controls.Add(_statusLabel, 0, 0);

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.ReadOnly = true;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.Font = new Font("Cascadia Mono", 9);
        _logBox.BackColor = InputBackground;
        _logBox.ForeColor = Color.FromArgb(203, 213, 225);
        _logBox.Margin = new Padding(0);
        logPanel.Controls.Add(_logBox, 0, 1);

        _webView.Dock = DockStyle.Fill;
        root.Panel2.Controls.Add(_webView);
    }

    private static void AddPathRow(TableLayoutPanel panel, string labelText, TextBox textBox, EventHandler browseHandler)
    {
        AddTextRow(panel, labelText, textBox);
        var browseButton = new Button { Text = "...", Width = 36, Height = 28, Margin = new Padding(6, 0, 0, 10) };
        StyleButton(browseButton, Color.FromArgb(51, 65, 85), TextPrimary);
        browseButton.Click += browseHandler;
        panel.Controls.Add(browseButton, 2, panel.RowCount - 1);
    }

    private static void AddTextRow(TableLayoutPanel panel, string labelText, TextBox textBox, bool password = false)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = new Padding(0, 8, 0, 5)
        };
        panel.Controls.Add(label, 0, row);
        panel.SetColumnSpan(label, 3);

        textBox.Dock = DockStyle.Top;
        textBox.UseSystemPasswordChar = password;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = InputBackground;
        textBox.ForeColor = TextPrimary;
        textBox.Font = new Font("Segoe UI", 9);
        textBox.Height = 28;
        textBox.Margin = new Padding(0, 0, 0, 10);
        panel.Controls.Add(textBox, 0, panel.RowCount++);
        panel.SetColumnSpan(textBox, 2);
    }

    private static void StyleButton(Button button, Color backColor, Color foreColor)
    {
        button.Height = 32;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        button.Margin = new Padding(0, 0, 8, 8);
        button.Cursor = Cursors.Hand;
    }

    private static void StyleCheckBox(CheckBox checkBox)
    {
        checkBox.AutoSize = true;
        checkBox.ForeColor = TextPrimary;
        checkBox.Font = new Font("Segoe UI", 9);
        checkBox.Margin = new Padding(0, 6, 0, 0);
    }

    private void BindEvents()
    {
        _startButton.Click += (_, _) => StartCore();
        _stopButton.Click += (_, _) => StopCore();
        _mihomo.StatusChanged += (_, _) => BeginInvoke(new Action(RefreshStatus));
        _mihomo.LogReceived += (_, text) =>
        {
            if (!string.IsNullOrEmpty(text))
            {
                BeginInvoke(new Action(() =>
                {
                    _logBox.AppendText(text);
                    _logBox.SelectionStart = _logBox.TextLength;
                    _logBox.ScrollToCaret();
                }));
            }
        };
        _autostartBox.CheckedChanged += (_, _) =>
        {
            try
            {
                AutostartManager.SetEnabled(_autostartBox.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "开机自启设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
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

    private void LoadSettingsToUi()
    {
        _corePathBox.Text = _settings.CorePath;
        _configPathBox.Text = _settings.ConfigPath;
        _apiUrlBox.Text = _settings.DashboardApiUrl;
        _secretBox.Text = _settings.Secret;
        _startCoreBox.Checked = _settings.StartCoreOnLaunch;
        _minimizeToTrayBox.Checked = _settings.MinimizeToTray;
        _autostartBox.Checked = AutostartManager.IsEnabled();
    }

    private void SaveSettingsFromUi(bool showMessage)
    {
        _settings.CorePath = _corePathBox.Text.Trim();
        _settings.ConfigPath = _configPathBox.Text.Trim();
        _settings.DashboardApiUrl = _apiUrlBox.Text.Trim();
        _settings.Secret = _secretBox.Text;
        _settings.StartCoreOnLaunch = _startCoreBox.Checked;
        _settings.MinimizeToTray = _minimizeToTrayBox.Checked;
        _settings.Save();
        LoadDashboard();

        if (showMessage)
        {
            MessageBox.Show(this, "设置已保存。", "Mihomo Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void StartCore()
    {
        SaveSettingsFromUi(showMessage: false);

        try
        {
            _mihomo.Start(_settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
    }

    private void RefreshStatus()
    {
        var running = _mihomo.IsRunning;
        _statusLabel.Text = running
            ? $"状态：运行中  PID：{_mihomo.ProcessId}"
            : "状态：未运行";
        _statusLabel.ForeColor = running ? Success : Warning;
        _startButton.Enabled = !running;
        _stopButton.Enabled = running;
        _trayIcon.Text = running ? "Mihomo Dashboard - 运行中" : "Mihomo Dashboard - 未运行";
    }

    private void ApplySplitLayout()
    {
        if (_rootSplit is null || _rootSplit.Width <= 0)
        {
            return;
        }

        const int panel1Min = 300;
        const int panel2Min = 420;
        const int desiredDistance = 360;

        if (_rootSplit.Width <= panel1Min + panel2Min)
        {
            return;
        }

        _rootSplit.Panel1MinSize = panel1Min;
        _rootSplit.Panel2MinSize = panel2Min;

        var maxDistance = _rootSplit.Width - panel2Min;
        var distance = Math.Min(Math.Max(desiredDistance, panel1Min), maxDistance);

        if (_rootSplit.SplitterDistance != distance)
        {
            _rootSplit.SplitterDistance = distance;
        }
    }

    private void BrowseCorePath(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 mihomo.exe",
            Filter = "Mihomo executable|mihomo*.exe;clash*.exe|Executable|*.exe|All files|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _corePathBox.Text = dialog.FileName;
        }
    }

    private void BrowseConfigPath(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 config.yaml",
            Filter = "YAML config|*.yaml;*.yml|All files|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _configPathBox.Text = dialog.FileName;
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
