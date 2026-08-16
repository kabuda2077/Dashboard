using System.Security.Cryptography;

namespace Dashboard;

internal static class WebViewDataMaintenance
{
    private const string MarkerFileName = ".webview-content-version";

    public static bool PrepareForCurrentContent(string appDirectory, string appVersion)
    {
        var resourceDirectory = Path.Combine(appDirectory, "resources");
        var dashboardIndexPath = Path.Combine(resourceDirectory, "dashboard", "index.html");
        var webViewDirectory = Path.Combine(resourceDirectory, "EBWebView");
        var markerPath = Path.Combine(resourceDirectory, MarkerFileName);
        var contentIdentity = CreateContentIdentity(appVersion, dashboardIndexPath);

        try
        {
            var previousIdentity = File.Exists(markerPath)
                ? File.ReadAllText(markerPath).Trim()
                : "";
            if (string.Equals(previousIdentity, contentIdentity, StringComparison.Ordinal))
            {
                return false;
            }

            if (Directory.Exists(webViewDirectory))
            {
                Directory.Delete(webViewDirectory, recursive: true);
            }

            Directory.CreateDirectory(resourceDirectory);
            File.WriteAllText(markerPath, contentIdentity);
            HostOperationLogger.Info(
                "webview",
                $"WebView data reset for Dashboard content {contentIdentity}.");
            return true;
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("webview", "Failed to reset WebView data after Dashboard content changed.", ex);
            return false;
        }
    }

    internal static string CreateContentIdentity(string appVersion, string dashboardIndexPath)
    {
        var normalizedVersion = AppUpdateChecker.NormalizeVersion(appVersion);
        if (string.IsNullOrWhiteSpace(normalizedVersion))
        {
            normalizedVersion = appVersion.Trim();
        }

        if (!File.Exists(dashboardIndexPath))
        {
            return $"{normalizedVersion}|missing-dashboard";
        }

        using var stream = File.OpenRead(dashboardIndexPath);
        var digest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return $"{normalizedVersion}|{digest}";
    }
}
