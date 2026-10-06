using System.Text;

namespace Dashboard;

internal static class WebViewDataMaintenance
{
    public static WebViewContentUpdate PlanForCurrentContent(
        string appDirectory, string? appVersion = null, string? dashboardDirectory = null)
    {
        var resourceDirectory = Path.Combine(Path.GetFullPath(appDirectory), "resources");
        var userDataFolder = Path.Combine(resourceDirectory, "webview-data-v2");
        if (!File.Exists(Path.Combine(dashboardDirectory ?? Path.Combine(resourceDirectory, "dashboard"), "index.html")))
            throw new IOException("桌面界面文件不完整，请完整解压 Dashboard 后重试。WebView 数据未清理。");

        var markerPath = Path.Combine(userDataFolder, WebViewContentUpdate.MarkerFileName);
        var version = appVersion ?? DashboardVersion.Current;
        string previousVersion;
        try
        {
            previousVersion = File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : "";
        }
        catch (Exception ex)
        {
            // An unreadable marker is not permission to repeatedly erase a profile.
            throw new IOException("无法读取 WebView 更新标记，请检查目录权限后重试。", ex);
        }
        // Only the application version decides whether to reset. Old fingerprint
        // markers differ once and are replaced with the plain version on success.
        // Same-version rebuilds must not erase browser data just because assets differ.
        return new WebViewContentUpdate(userDataFolder, version,
            !string.Equals(previousVersion, version, StringComparison.Ordinal));
    }
}

internal sealed class WebViewContentUpdate
{
    internal const string MarkerFileName = ".webview-content-version";
    private readonly string _markerPath;
    private readonly string _appVersion;

    internal WebViewContentUpdate(string userDataFolder, string appVersion, bool requiresDataReset)
    {
        UserDataFolder = Path.GetFullPath(userDataFolder);
        _markerPath = Path.Combine(UserDataFolder, MarkerFileName);
        _appVersion = appVersion;
        RequiresDataReset = requiresDataReset;
    }

    // Keep v2's independent SDK root and version marker. Reset only its
    // EBWebView child, never resources or the separate 1.x browser profile.
    public string UserDataFolder { get; }
    public string BrowserDataDirectory => Path.Combine(UserDataFolder, "EBWebView");
    public bool RequiresDataReset { get; private set; }

    public void PrepareUserDataDirectory()
    {
        Directory.CreateDirectory(UserDataFolder);
        if (!RequiresDataReset) return;

        try
        {
            if ((File.GetAttributes(UserDataFolder) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("WebView 数据根目录是链接，拒绝自动清理。");
            if (Directory.Exists(BrowserDataDirectory))
            {
                if ((File.GetAttributes(BrowserDataDirectory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("EBWebView 目录是链接，拒绝自动清理。");
                Directory.Delete(BrowserDataDirectory, recursive: true);
            }
            else if (File.Exists(BrowserDataDirectory))
            {
                throw new IOException("EBWebView 路径不是目录。");
            }

            // Publish the marker only after the complete directory was removed.
            // If deletion or marker persistence fails, do not create a WebView.
            var temporary = _markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, _appVersion, new UTF8Encoding(false));
                File.Move(temporary, _markerPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            RequiresDataReset = false;
            HostOperationLogger.Info("webview", $"WebView data directory reset for Dashboard version {_appVersion}.");
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("webview", "WebView data reset failed; update marker was not advanced.", ex);
            throw new IOException("无法清理旧 EBWebView 数据，请完全退出旧 Dashboard 及其 WebView 进程，检查目录权限后重试。", ex);
        }
    }
}
