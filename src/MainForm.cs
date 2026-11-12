using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard;

public sealed class MainForm : Form
{
    internal event EventHandler? ExitRequested;
    private readonly DashboardHost _host;
    private AppSettings _settings => _host.Settings;
    private long _navigationGeneration;
    private TaskCompletionSource<bool>? _preferenceFlush;
    private string? _preferenceFlushId;
    private Task<bool>? _preferenceFlushTask;
    private readonly HostMessageRouter _hostMessageRouter;
    private readonly DashboardStatePublisher _statePublisher;
    private readonly Uri _dashboardUri;
    private readonly WebViewTrustPolicy _webViewTrust;
    private readonly WebViewContentUpdate _webViewContentUpdate;
    private readonly Icon _appIcon;
    private Panel _contentPanel = null!;
    private WebView2? _webView;
    private Rectangle _trayRestoreBounds;
    private FormWindowState _trayRestoreWindowState = FormWindowState.Normal;
    private bool _hiddenToTray;
    private bool _trayTransitionInProgress;
    private bool _showAfterTrayTransition;
    internal bool IsTrayTransitionInProgress => _trayTransitionInProgress;
    private bool _allowClose;
    private bool _initialized;
    private bool _dashboardInitialized;
    private Task? _dashboardInitializationTask;
    private int _dashboardInitializationVersion;
    private ulong? _dashboardNavigationId;
    private long _dashboardNavigationStartedAt;
    private TaskCompletionSource<bool>? _dashboardNavigationCompletion;
    private Panel? _dashboardFailurePanel;
    private long _webViewInitializationSequence;
    private long _activeWebViewInitializationId;
    private long _activeWebViewInitializationStartedAt;
    private long? _dashboardReleasedAt;
    private string? _trayRestoreId;
    private long _trayRestoreStartedAt;
    private ulong? _trayRestoreNavigationId;
    private bool _trayRestoreFrontendReady;
    private double _trayRestoreLastDocumentMs;
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

    internal MainForm(DashboardHost host, WebViewContentUpdate webViewContentUpdate)
    {
        _host = host;
        _dashboardUri = host.DashboardUri;
        _webViewContentUpdate = webViewContentUpdate;
        _webViewTrust = new WebViewTrustPolicy(_dashboardUri);
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
        _hostMessageRouter = new(new HostMessageHandlers
        {
            ExecuteAsync = _host.ExecuteAsync,
            RequestElevation = _host.RequestElevation,
            PreferencesFlushed = (id, success) => { if (id == _preferenceFlushId) _preferenceFlush?.TrySetResult(success); },
            BuildState = () => _host.BuildState(WindowState == FormWindowState.Maximized),
            Preferences = () => _host.Preferences,
            ChooseFile = ChooseCoreFile,
            OpenLocation = (kind, config) => OpenPathLocation(
                config ? _settings.Profile(kind).ConfigPath : _settings.Profile(kind).ExePath,
                config ? "配置文件" : "内核文件"),
            WindowCommand = (type, edge) =>
            {
                switch (type)
                {
                    case "windowDrag": BeginWindowDrag(); break;
                    case "windowResize": BeginWindowResize(edge!); break;
                    case "windowToggleMaximize": ToggleMaximize(); SendWindowChromeState(); break;
                    case "windowMinimize": MinimizeToTaskbar(); break;
                    case "windowClose": Close(); break;
                    case "requestWindowState": SendWindowChromeState(); break;
                }
            },
            CheckAppUpdateAsync = () => _host.CheckForAppUpdateAsync(manual: true),
            OpenAppRelease = _host.OpenAppReleasePage,
            OpenCoreRepository = _host.OpenCoreRepositoryPage,
            OpenCoreRelease = _host.OpenCoreReleasePage
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
        await EnsureDashboardInitializedAsync("window-shown");

        HostOperationLogger.Diagnostic("performance", $"host:onShown durationMs={Stopwatch.GetElapsedTime(shownStartedAt).TotalMilliseconds:0}");
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

    private void BeginWindowResize(string edge)
    {
        if (!IsHandleCreated || WindowState == FormWindowState.Maximized)
        {
            return;
        }

        var hitTest = GetResizeHitTest(edge);
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

    private Task EnsureDashboardInitializedAsync(string trigger)
    {
        if (_dashboardInitialized && HasDashboardWebView())
        {
            return Task.CompletedTask;
        }

        if (_dashboardInitializationTask is { IsCompleted: false })
        {
            return _dashboardInitializationTask;
        }

        _dashboardInitializationTask = InitializeDashboardAsync(trigger);
        return _dashboardInitializationTask;
    }

    private async Task InitializeDashboardAsync(string trigger)
    {
        var version = ++_dashboardInitializationVersion;
        HideDashboardFailure();
        if (!HasDashboardWebView() && !await InitializeWebViewAsync(trigger))
        {
            if (version == _dashboardInitializationVersion && !IsDisposed && !Disposing)
            {
                if (_webView is { } failedView) ResetFailedWebView(failedView);
                ShowDashboardFailure();
            }
            return;
        }
        if (version != _dashboardInitializationVersion || IsDisposed || Disposing) return;

        if (await LoadDashboardAsync() && version == _dashboardInitializationVersion) RefreshStatus();
    }

    private void ShowDashboardFailure()
    {
        EndTrayRestoreTrace("failed");
        _dashboardInitialized = false;
        if (_dashboardFailurePanel is null)
        {
            _dashboardFailurePanel = new Panel { Dock = DockStyle.Fill, Name = "dashboardLoadFailure" };
            var retry = new Button { Text = "重试加载 Dashboard", Name = "retryDashboard", AutoSize = true, Dock = DockStyle.Top };
            retry.Click += async (_, _) => await EnsureDashboardInitializedAsync("visible-retry");
            _dashboardFailurePanel.Controls.Add(retry);
            _dashboardFailurePanel.Controls.Add(new Label
            {
                Text = "Dashboard 加载失败。请重试，或从托盘重新打开窗口。",
                AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(16)
            });
            _contentPanel.Controls.Add(_dashboardFailurePanel);
        }
        _dashboardFailurePanel.Show();
        _dashboardFailurePanel.BringToFront();
    }

    private void HideDashboardFailure() => _dashboardFailurePanel?.Hide();

    private void BindEvents()
    {
        _host.StateChanged += OnHostStateChanged;
        _host.RuntimeStateChanged += OnHostRuntimeStateChanged;
        _host.LogReceived += OnHostLogReceived;
        _host.IconCacheChanged += OnHostIconCacheChanged;
        _host.NoticeRequested += OnHostNoticeRequested;
        _host.AppUpdateResultRequested += OnAppUpdateResultRequested;
        _dashboardDisposeTimer.Tick += async (_, _) =>
        {
            _dashboardDisposeTimer.Stop();
            var version = _dashboardSuspendVersion;
            var view = _webView;
            if (_settings.DesktopOptions.LightweightMode && _hiddenToTray && !Visible && !_trayTransitionInProgress
                && await FlushPreferencesAsync()
                && version == _dashboardSuspendVersion && ReferenceEquals(view, _webView)
                && _settings.DesktopOptions.LightweightMode && _hiddenToTray && !Visible && !_trayTransitionInProgress)
                DisposeDashboardView();
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
        RunOnUiThread(() => ShowDashboardNotice(message));
    }

    private void OnAppUpdateResultRequested(object? sender, object message)
    {
        RunOnUiThread(() => PostDashboardMessage(message));
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

    private async Task<bool> InitializeWebViewAsync(string trigger)
    {
        var restoreId = _trayRestoreId;
        void LogInitializationStage(string stage, string details = "")
        {
            if (restoreId is not null && restoreId == _trayRestoreId) LogTrayRestoreStage(stage, details);
        }
        var initializationId = Interlocked.Increment(ref _webViewInitializationSequence);
        HostOperationLogger.Diagnostic(
            "performance",
            $"webview:initializationStarted id={initializationId} trigger={trigger}");
        var startedAt = Stopwatch.GetTimestamp();
        _activeWebViewInitializationId = initializationId;
        _activeWebViewInitializationStartedAt = startedAt;
        LogInitializationStage("webview:initializationStarted");
        for (var attempt = 1; attempt <= WebViewInitializationAttempts; attempt++)
        {
            var stage = "control";
            var stageStartedAt = Stopwatch.GetTimestamp();
            EnsureWebViewCreated();
            var controlMs = Stopwatch.GetElapsedTime(stageStartedAt).TotalMilliseconds;
            LogInitializationStage("webview:control", $"attempt={attempt} durationMs={controlMs:0.0}");
            var webView = _webView;
            if (webView is null)
            {
                return false;
            }

            try
            {
                stage = "profile";
                stageStartedAt = Stopwatch.GetTimestamp();
                _webViewContentUpdate.PrepareUserDataDirectory();
                var profileMs = Stopwatch.GetElapsedTime(stageStartedAt).TotalMilliseconds;
                LogInitializationStage("webview:profile", $"attempt={attempt} durationMs={profileMs:0.0}");

                stage = "environment";
                stageStartedAt = Stopwatch.GetTimestamp();
                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: _webViewContentUpdate.UserDataFolder);
                var environmentMs = Stopwatch.GetElapsedTime(stageStartedAt).TotalMilliseconds;
                LogInitializationStage("webview:environment", $"attempt={attempt} durationMs={environmentMs:0.0}");

                stage = "controller";
                stageStartedAt = Stopwatch.GetTimestamp();
                await webView.EnsureCoreWebView2Async(environment);
                var controllerMs = Stopwatch.GetElapsedTime(stageStartedAt).TotalMilliseconds;
                LogInitializationStage("webview:controller", $"attempt={attempt} durationMs={controllerMs:0.0}");
                if (!ReferenceEquals(webView, _webView) || webView.CoreWebView2 is null)
                {
                    return false;
                }

                stage = "settings";
                stageStartedAt = Stopwatch.GetTimestamp();
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                var settingsMs = Stopwatch.GetElapsedTime(stageStartedAt).TotalMilliseconds;
                LogInitializationStage("webview:settings", $"attempt={attempt} durationMs={settingsMs:0.0}");

                webView.CoreWebView2.NavigationStarting += OnDashboardNavigationStarting;
                webView.CoreWebView2.NavigationCompleted += OnDashboardNavigationCompleted;
                webView.CoreWebView2.NewWindowRequested += OnDashboardNewWindowRequested;
                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                HostOperationLogger.Info(
                    "performance",
                    $"webview:initialized id={initializationId} trigger={trigger} attempt={attempt} "
                        + $"controlMs={controlMs:0.0} profileMs={profileMs:0.0} "
                        + $"environmentMs={environmentMs:0.0} controllerMs={controllerMs:0.0} "
                        + $"settingsMs={settingsMs:0.0} "
                        + $"totalMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0} "
                        + $"runtime={environment.BrowserVersionString}");
                return true;
            }
            catch (Exception ex) when (IsWebViewInitializationAborted(ex))
            {
                var message = $"WebView2 initialization was canceled; id={initializationId} trigger={trigger} "
                    + $"attempt={attempt}/{WebViewInitializationAttempts} stage={stage} "
                    + $"totalMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}; rebuilding control.";
                if (attempt == WebViewInitializationAttempts)
                {
                    HostOperationLogger.Error("webview", message, ex);
                }
                else
                {
                    HostOperationLogger.Diagnostic("webview", message);
                }
                LogInitializationStage("webview:retry", $"attempt={attempt} stage={stage}");
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

    private Task<bool> LoadDashboardAsync()
    {
        var coreWebView = _webView?.CoreWebView2;
        if (coreWebView is null) return Task.FromResult(false);

        _dashboardInitialized = false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dashboardNavigationCompletion = completion;
        try
        {
            LogTrayRestoreStage("webview:navigationRequested");
            // An error document can retain the dashboard URL. Navigating to that
            // same URL/fragment may only change its hash, so force a real reload.
            if (_webViewTrust.IsTrustedDocument(coreWebView.Source)) coreWebView.Reload();
            else coreWebView.Navigate(new Uri(_dashboardUri, "#/core").ToString());
        }
        catch (Exception error)
        {
            HostOperationLogger.Error("webview", "Failed to navigate to Dashboard.", error);
            ShowDashboardFailure();
            completion.TrySetResult(false);
        }
        return completion.Task;
    }

    private void OnDashboardNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        var current = _webView?.CoreWebView2;
        if (current is null || !ReferenceEquals(sender, current) || args.NavigationId != _dashboardNavigationId) return;

        HostOperationLogger.Info(
            "performance",
            $"webview:navigationCompleted id={_activeWebViewInitializationId} success={args.IsSuccess} "
                + $"status={args.WebErrorStatus} navigationMs={Stopwatch.GetElapsedTime(_dashboardNavigationStartedAt).TotalMilliseconds:0.0} "
                + $"totalMs={Stopwatch.GetElapsedTime(_activeWebViewInitializationStartedAt).TotalMilliseconds:0.0}");
        _dashboardInitialized = args.IsSuccess && _webViewTrust.IsTrustedDocument(current.Source);
        LogTrayRestoreStage("webview:navigationCompleted",
            $"success={_dashboardInitialized} durationMs={Stopwatch.GetElapsedTime(_dashboardNavigationStartedAt).TotalMilliseconds:0.0}");
        _dashboardNavigationCompletion?.TrySetResult(_dashboardInitialized);
        if (_dashboardInitialized)
        {
            HideDashboardFailure();
            SendStateToDashboard();
            if (_trayRestoreFrontendReady) EndTrayRestoreTrace("completed");
        }
        else ShowDashboardFailure();
    }

    private void OnDashboardNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var current = _webView?.CoreWebView2;
        if (!ReferenceEquals(sender, current) || current is null)
        {
            e.Cancel = true;
            return;
        }

        var target = _webViewTrust.ClassifyNavigation(e.Uri);
        if (target == DashboardNavigationTarget.Dashboard)
        {
            if (_trayRestoreNavigationId is { } restoreNavigation && restoreNavigation != e.NavigationId)
                EndTrayRestoreTrace("navigation-replaced");
            if (_trayRestoreId is not null) _trayRestoreNavigationId = e.NavigationId;
            LogTrayRestoreStage("webview:navigationStarting");
            _dashboardNavigationId = e.NavigationId;
            _dashboardNavigationStartedAt = Stopwatch.GetTimestamp();
            _dashboardInitialized = false;
            HideDashboardFailure();
            Interlocked.Increment(ref _navigationGeneration);
            _dashboardSuspendVersion++;
            _preferenceFlush?.TrySetResult(false);
            return;
        }

        e.Cancel = true;
        // Redirects and programmatic navigation cannot launch arbitrary applications.
        if (target == DashboardNavigationTarget.ExternalWeb && e.IsUserInitiated && !e.IsRedirected
            && _webViewTrust.IsTrustedDocument(current.Source))
        {
            OpenExternalWebLink(e.Uri);
        }
    }

    private void OnDashboardNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        var current = _webView?.CoreWebView2;
        if (current is null || !ReferenceEquals(sender, current) || !e.IsUserInitiated
            || !_webViewTrust.IsTrustedDocument(current.Source)) return;

        switch (_webViewTrust.ClassifyNavigation(e.Uri))
        {
            case DashboardNavigationTarget.Dashboard:
                current.Navigate(e.Uri);
                break;
            case DashboardNavigationTarget.ExternalWeb:
                OpenExternalWebLink(e.Uri);
                break;
        }
    }

    private void OpenExternalWebLink(string address)
    {
        if (_webViewTrust.ClassifyNavigation(address) != DashboardNavigationTarget.ExternalWeb) return;
        try
        {
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("webview", "Failed to open external web link.", ex);
            ShowDashboardNotice("无法打开链接，请检查系统默认浏览器。");
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var current = _webView?.CoreWebView2;
        if (!_webViewTrust.CanReceiveMessage(sender, current, e.Source, current?.Source))
        {
            // Do not log untrusted payloads or URLs, which may contain credentials.
            HostOperationLogger.Info("host-bridge", "Rejected message from an untrusted or obsolete document.");
            return;
        }

        try
        {
            var generation = Interlocked.Read(ref _navigationGeneration);
            await _hostMessageRouter.RouteAsync(e.WebMessageAsJson, message =>
            {
                if (generation == Interlocked.Read(ref _navigationGeneration) && ReferenceEquals(current, _webView?.CoreWebView2))
                    PostDashboardMessage(message);
            }, (stage, durationMs) =>
            {
                if (generation != Interlocked.Read(ref _navigationGeneration) || !ReferenceEquals(current, _webView?.CoreWebView2)) return;
                LogTrayRestoreStage(stage, FormatTrayRestoreMetric(stage, durationMs, ref _trayRestoreLastDocumentMs));
                if (stage == "frontend:startupFailed") EndTrayRestoreTrace("frontend-failed");
                if (stage == "frontend:frontendReady" && _trayRestoreId is not null)
                {
                    _trayRestoreFrontendReady = true;
                    if (_dashboardInitialized) EndTrayRestoreTrace("completed");
                }
            });
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("host-bridge", "Failed to process dashboard message.", ex);
            ShowDashboardNotice($"操作失败：{ex.Message}");
        }
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

    private void RefreshStatus()
    {
        _statePublisher.SendRuntimeState();
    }

    private void SendStateToDashboard()
    {
        _statePublisher.SendState();
    }

    private void ShowDashboardNotice(string message)
    {
        _statePublisher.ShowNotice(message);
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

    internal Task<bool> FlushPreferencesAsync()
    {
        if (_webView?.CoreWebView2 is null || _webViewSuspended) return Task.FromResult(true);
        if (_preferenceFlushTask is { IsCompleted: false }) return _preferenceFlushTask;
        return _preferenceFlushTask = FlushPreferencesOwnedAsync();
    }

    private async Task<bool> FlushPreferencesOwnedAsync()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _preferenceFlush = pending;
        _preferenceFlushId = "flush-" + Guid.NewGuid().ToString("N");
        try
        {
            PostDashboardMessage(new { protocolVersion = 2, type = "flushPreferences", requestId = _preferenceFlushId });
            var saved = await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!saved) _host.ShowNotice("存在未保存修改或保存尚未确认，暂时保留界面。请保存或放弃核心草稿后重试。");
            return saved;
        }
        catch (Exception error)
        {
            HostOperationLogger.Error("settings", "Preferences did not flush before view suspension.", error);
            return false;
        }
        finally
        {
            if (ReferenceEquals(_preferenceFlush, pending)) { _preferenceFlush = null; _preferenceFlushId = null; }
        }
    }

    private async void SuspendDashboard()
    {
        var coreWebView = _webView?.CoreWebView2;
        if (_webViewSuspended || coreWebView is null)
        {
            return;
        }

        var suspendVersion = ++_dashboardSuspendVersion;
        _statePublisher.MarkDirty();

        try
        {
            if (!await FlushPreferencesAsync() || suspendVersion != _dashboardSuspendVersion
                || !ReferenceEquals(coreWebView, _webView?.CoreWebView2) || !ShouldHoldDashboardUpdates()) return;
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

    private void DisposeDashboardView() => ReleaseDashboardView("lightweight-timeout");

    private void ReleaseDashboardView(string reason)
    {
        EndTrayRestoreTrace("released-again");
        CancelDelayedDashboardDispose();
        _dashboardSuspendVersion++;
        _preferenceFlush?.TrySetResult(false);
        _statePublisher.MarkDirty();
        _webViewSuspended = false;
        _dashboardInitialized = false;
        _dashboardInitializationVersion++;
        _dashboardInitializationTask = null;
        _dashboardNavigationId = null;
        _dashboardNavigationCompletion?.TrySetResult(false);
        _dashboardNavigationCompletion = null;
        HideDashboardFailure();

        var webView = _webView;
        if (webView is null)
        {
            return;
        }

        _contentPanel.Controls.Remove(webView);
        _webView = null;
        webView.Dispose();
        _dashboardReleasedAt = reason == "lightweight-timeout" ? Stopwatch.GetTimestamp() : null;
        HostOperationLogger.Info(
            "performance",
            $"webview:disposed id={_activeWebViewInitializationId} reason={reason}");
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
        if (coreWebView is null || !_webViewTrust.IsTrustedDocument(coreWebView.Source))
        {
            return;
        }

        coreWebView.PostWebMessageAsJson(HostBridgeJson.Serialize(message));
    }

    private string? ChooseCoreFile(CoreKind kind, bool config)
    {
        using var dialog = new OpenFileDialog
        {
            Title = config ? "选择配置文件" : $"选择 {AppSettings.WireKind(kind)} 内核",
            Filter = config
                ? kind == CoreKind.SingBox ? "JSON config|*.json|All files|*.*" : "YAML config|*.yaml;*.yml|All files|*.*"
                : "Executable|*.exe|All files|*.*"
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void OpenPathLocation(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowDashboardNotice($"请先设置{label}路径。");
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
            ShowDashboardNotice($"{label}不存在，已打开所在文件夹。");
            return;
        }

        ShowDashboardNotice($"找不到{label}所在位置。");
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
        HostOperationLogger.Diagnostic(
            "window-lifecycle",
            $"formClosing reason={e.CloseReason} allowClose={_allowClose} minimizeToTray={_settings.DesktopOptions.MinimizeToTray} visible={Visible} windowState={WindowState}");
        if (!_allowClose && _settings.DesktopOptions.MinimizeToTray && ShouldHideToTrayOnClose(e.CloseReason))
        {
            e.Cancel = true;
            HostOperationLogger.Diagnostic("window-lifecycle", "formClosing cancelled; hiding to tray.");
            HideToTray();
            return;
        }

        if (!_allowClose && ShouldHideToTrayOnClose(e.CloseReason))
        {
            e.Cancel = true;
            ExitRequested?.Invoke(this, EventArgs.Empty);
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

        EndTrayRestoreTrace("hidden");
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

            if (IsDisposed || _showAfterTrayTransition)
            {
                return;
            }

            Hide();
            if (_settings.DesktopOptions.LightweightMode)
            {
                ScheduleDashboardViewDispose();
            }
        }
        finally
        {
            CompleteTrayTransition();
        }
    }

    private void CompleteTrayTransition()
    {
        _trayTransitionInProgress = false;
        var show = _showAfterTrayTransition;
        _showAfterTrayTransition = false;
        if (show && !IsDisposed && !Disposing && !_allowClose) ShowFromTray();
    }

    public void ShowFromTray()
    {
        var requestedAt = Stopwatch.GetTimestamp();
        var dashboardReady = HasDashboardWebView();
        HostOperationLogger.Diagnostic(
            "performance",
            $"tray:showRequested dashboardReady={dashboardReady} visible={Visible} windowState={WindowState}");
        CancelDelayedDashboardDispose();
        if (_trayTransitionInProgress)
        {
            _showAfterTrayTransition = true;
            return;
        }

        if (_dashboardReleasedAt is { } releasedAt && !dashboardReady && _trayRestoreId is null)
        {
            _trayRestoreId = Guid.NewGuid().ToString("N");
            _trayRestoreStartedAt = requestedAt;
            _trayRestoreNavigationId = null;
            _trayRestoreFrontendReady = false;
            _trayRestoreLastDocumentMs = 0;
            _dashboardReleasedAt = null;
            LogTrayRestoreStage("requested", $"releasedAgoMs={Stopwatch.GetElapsedTime(releasedAt).TotalMilliseconds:0.0}");
        }

        _hiddenToTray = false;
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            ResumeDashboard();
            Activate();
            BringToFront();
            LogTrayRestoreStage("window:showDispatched");
            _ = EnsureDashboardInitializedAsync("tray-visible");
            HostOperationLogger.Diagnostic(
                "performance",
                $"tray:showDispatched dashboardReady={dashboardReady} durationMs={Stopwatch.GetElapsedTime(requestedAt).TotalMilliseconds:0.0}");
            return;
        }

        ShowPreparedDashboardFromTray(requestedAt, dashboardReady);
    }

    internal static string FormatTrayRestoreMetric(string stage, double durationMs, ref double previousDocumentMs)
    {
        if (stage is "frontend:bootstrapRequested" or "frontend:bootstrapReceived" or "frontend:preferencesRestored"
            or "frontend:applicationImportRequested" or "frontend:applicationImportsReady" or "frontend:frontendMounted"
            or "frontend:coreInteractiveFromNavigation" or "frontend:frontendReady" or "frontend:startupFailed")
        {
            var details = $"documentMs={durationMs:0.0} sincePreviousMs={durationMs - previousDocumentMs:0.0}";
            previousDocumentMs = durationMs;
            return details;
        }
        return $"durationMs={durationMs:0.0}";
    }

    private void LogTrayRestoreStage(string stage, string details = "")
    {
        if (_trayRestoreId is null) return;
        HostOperationLogger.Info("performance",
            $"trayRestore id={_trayRestoreId} webviewId={_activeWebViewInitializationId} stage={stage} "
                + $"elapsedMs={Stopwatch.GetElapsedTime(_trayRestoreStartedAt).TotalMilliseconds:0.0} {details}".TrimEnd());
    }

    private void EndTrayRestoreTrace(string status)
    {
        LogTrayRestoreStage("finished", $"status={status}");
        _trayRestoreId = null;
        _trayRestoreNavigationId = null;
        _trayRestoreFrontendReady = false;
    }

    private void ShowPreparedDashboardFromTray(long requestedAt, bool dashboardReady)
    {
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
            LogTrayRestoreStage("window:showDispatched");
            _ = EnsureDashboardInitializedAsync("tray-restore");
            HostOperationLogger.Diagnostic(
                "performance",
                $"tray:showDispatched dashboardReady={dashboardReady} durationMs={Stopwatch.GetElapsedTime(requestedAt).TotalMilliseconds:0.0}");
        }
        finally
        {
            CompleteTrayTransition();
        }
    }

    internal void CloseForApplicationExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        HostOperationLogger.Diagnostic("window-lifecycle", $"formClosed reason={e.CloseReason}");
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            EndTrayRestoreTrace("disposed");
            _host.StateChanged -= OnHostStateChanged;
            _host.RuntimeStateChanged -= OnHostRuntimeStateChanged;
            _host.LogReceived -= OnHostLogReceived;
            _host.IconCacheChanged -= OnHostIconCacheChanged;
            _host.NoticeRequested -= OnHostNoticeRequested;
            _host.AppUpdateResultRequested -= OnAppUpdateResultRequested;
            ReleaseDashboardView("shutdown");
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
