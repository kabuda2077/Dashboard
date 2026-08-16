namespace Dashboard.Tests;

public sealed class WebViewDataMaintenanceTests
{
    [Fact]
    public void ClearsWebViewDataOnlyWhenBundledContentChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "Dashboard.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var dashboardDirectory = Path.Combine(root, "resources", "dashboard");
            var webViewDirectory = Path.Combine(root, "resources", "EBWebView");
            Directory.CreateDirectory(dashboardDirectory);
            Directory.CreateDirectory(webViewDirectory);
            File.WriteAllText(Path.Combine(dashboardDirectory, "index.html"), "first");
            File.WriteAllText(Path.Combine(webViewDirectory, "cache.bin"), "old");

            Assert.True(WebViewDataMaintenance.PrepareForCurrentContent(root, "1.2.0"));
            Assert.False(Directory.Exists(webViewDirectory));

            Directory.CreateDirectory(webViewDirectory);
            File.WriteAllText(Path.Combine(webViewDirectory, "cache.bin"), "current");
            Assert.False(WebViewDataMaintenance.PrepareForCurrentContent(root, "1.2.0"));
            Assert.True(File.Exists(Path.Combine(webViewDirectory, "cache.bin")));

            File.WriteAllText(Path.Combine(dashboardDirectory, "index.html"), "second");
            Assert.True(WebViewDataMaintenance.PrepareForCurrentContent(root, "1.2.0"));
            Assert.False(Directory.Exists(webViewDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
