using System.Security.Cryptography;
using System.Text;

namespace Dashboard;

internal static class WebViewDataMaintenance
{
    private const string MarkerFileName = ".webview-content-version";
    private const string IdentityFormatVersion = "2";
    private static readonly string[] EntryPointFiles =
    [
        "index.html",
        "sw.js",
        "registerSW.js",
        "manifest.webmanifest"
    ];

    public static WebViewContentUpdate PlanForCurrentContent(string appDirectory)
    {
        var resourceDirectory = Path.Combine(appDirectory, "resources");
        var dashboardDirectory = Path.Combine(resourceDirectory, "dashboard");
        var markerPath = Path.Combine(resourceDirectory, MarkerFileName);
        var contentIdentity = CreateContentIdentity(dashboardDirectory);

        try
        {
            var previousIdentity = File.Exists(markerPath)
                ? File.ReadAllText(markerPath).Trim()
                : "";
            return new WebViewContentUpdate(
                markerPath,
                contentIdentity,
                !string.Equals(previousIdentity, contentIdentity, StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error(
                "webview",
                "Failed to read the Dashboard content marker; cache invalidation will be retried.",
                ex);
            return new WebViewContentUpdate(markerPath, contentIdentity, requiresCacheInvalidation: true);
        }
    }

    internal static string CreateContentIdentity(string dashboardDirectory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var fileName in EntryPointFiles)
        {
            var path = Path.Combine(dashboardDirectory, fileName);
            var nameBytes = Encoding.UTF8.GetBytes(fileName);
            hash.AppendData(nameBytes);
            hash.AppendData([0]);

            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, bytesRead);
                }
            }

            hash.AppendData([0]);
        }

        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return $"{IdentityFormatVersion}|{digest}";
    }
}

internal sealed class WebViewContentUpdate
{
    private readonly string _markerPath;
    private readonly string _contentIdentity;

    internal WebViewContentUpdate(
        string markerPath,
        string contentIdentity,
        bool requiresCacheInvalidation)
    {
        _markerPath = markerPath;
        _contentIdentity = contentIdentity;
        RequiresCacheInvalidation = requiresCacheInvalidation;
    }

    public bool RequiresCacheInvalidation { get; private set; }

    public bool CompleteCacheInvalidation()
    {
        if (!RequiresCacheInvalidation)
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            File.WriteAllText(_markerPath, _contentIdentity);
            RequiresCacheInvalidation = false;
            HostOperationLogger.Info(
                "webview",
                $"WebView HTTP cache and service workers invalidated for Dashboard content {_contentIdentity}.");
            return true;
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error(
                "webview",
                "Dashboard caches were invalidated, but the content marker could not be saved; invalidation will be retried.",
                ex);
            return false;
        }
    }
}
