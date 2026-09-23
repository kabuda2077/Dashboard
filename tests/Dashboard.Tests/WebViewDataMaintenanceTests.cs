namespace Dashboard.Tests;

public sealed class WebViewDataMaintenanceTests
{
    [Fact]
    public void ContentUpgradePreservesEntireProfileAndRequestsCacheInvalidation()
    {
        var root = CreateTempDirectory();
        try
        {
            var dashboardDirectory = Path.Combine(root, "resources", "dashboard");
            var profileDirectory = Path.Combine(root, "resources", "EBWebView");
            Directory.CreateDirectory(dashboardDirectory);
            Directory.CreateDirectory(profileDirectory);
            File.WriteAllText(Path.Combine(dashboardDirectory, "index.html"), "first");

            var retainedFiles = new[]
            {
                Path.Combine(profileDirectory, "Local Storage", "config-settings"),
                Path.Combine(profileDirectory, "IndexedDB", "proxy-tags"),
                Path.Combine(profileDirectory, "IndexedDB", "connection-history")
            };
            foreach (var retainedFile in retainedFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(retainedFile)!);
                File.WriteAllText(retainedFile, "retained");
            }

            var update = WebViewDataMaintenance.PlanForCurrentContent(root);

            Assert.True(update.RequiresCacheInvalidation);
            Assert.True(Directory.Exists(profileDirectory));
            Assert.All(retainedFiles, path => Assert.Equal("retained", File.ReadAllText(path)));

            Assert.True(update.CompleteCacheInvalidation());
            Assert.False(update.RequiresCacheInvalidation);
            Assert.True(Directory.Exists(profileDirectory));
            Assert.All(retainedFiles, path => Assert.Equal("retained", File.ReadAllText(path)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompletedContentIsCurrentUntilAnEntryPointChanges()
    {
        var root = CreateTempDirectory();
        try
        {
            var dashboardDirectory = Path.Combine(root, "resources", "dashboard");
            Directory.CreateDirectory(dashboardDirectory);
            File.WriteAllText(Path.Combine(dashboardDirectory, "index.html"), "first");

            var firstUpdate = WebViewDataMaintenance.PlanForCurrentContent(root);
            Assert.True(firstUpdate.RequiresCacheInvalidation);
            Assert.True(firstUpdate.CompleteCacheInvalidation());
            Assert.False(WebViewDataMaintenance.PlanForCurrentContent(root).RequiresCacheInvalidation);

            File.WriteAllText(Path.Combine(dashboardDirectory, "index.html"), "second");
            Assert.True(WebViewDataMaintenance.PlanForCurrentContent(root).RequiresCacheInvalidation);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UncompletedInvalidationIsRetriedAndServiceWorkerChangesAreDetected()
    {
        var root = CreateTempDirectory();
        try
        {
            var dashboardDirectory = Path.Combine(root, "resources", "dashboard");
            Directory.CreateDirectory(dashboardDirectory);
            File.WriteAllText(Path.Combine(dashboardDirectory, "index.html"), "index");

            Assert.True(WebViewDataMaintenance.PlanForCurrentContent(root).RequiresCacheInvalidation);
            Assert.True(WebViewDataMaintenance.PlanForCurrentContent(root).RequiresCacheInvalidation);

            var completed = WebViewDataMaintenance.PlanForCurrentContent(root);
            Assert.True(completed.CompleteCacheInvalidation());
            File.WriteAllText(Path.Combine(dashboardDirectory, "sw.js"), "new-worker");

            Assert.True(WebViewDataMaintenance.PlanForCurrentContent(root).RequiresCacheInvalidation);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Dashboard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
