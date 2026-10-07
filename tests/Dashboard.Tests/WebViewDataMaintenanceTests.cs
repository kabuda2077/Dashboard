using System.Diagnostics;

namespace Dashboard.Tests;

public sealed class WebViewDataMaintenanceTests : TemporaryDirectoryTest
{
    [Fact]
    public void UpgradeRemovesTheEntireNestedProfileButNoOtherApplicationData()
    {
        var root = CreateFixture();
        var resources = Path.Combine(root, "resources");
        var userDataFolder = Path.Combine(resources, "webview-data-v2");
        var browser = Path.Combine(userDataFolder, "EBWebView");
        var oldData = Path.Combine(browser, "EBWebView", "Default", "IndexedDB", "old-history");
        Write(oldData, "old browser data");
        var preserved = new[]
        {
            Path.Combine(root, "settings.json"), Path.Combine(root, "mihomo", "config.yaml"),
            Path.Combine(resources, "icon-cache", "icon.png"), Path.Combine(resources, "logs", "host.log"),
            Path.Combine(resources, "EBWebView", "Default", "keep-stable-profile"),
            Path.Combine(userDataFolder, "keep-wrapper-data"), Path.Combine(resources, "app.ico")
        };
        foreach (var file in preserved) Write(file, "keep");
        var marker = Path.Combine(userDataFolder, WebViewContentUpdate.MarkerFileName);
        File.WriteAllText(marker, "2|old-cache-only-policy");
        var update = WebViewDataMaintenance.PlanForCurrentContent(root, "1.3.2");
        Assert.True(update.RequiresDataReset);
        Assert.True(File.Exists(oldData)); // planning never deletes data
        Assert.Equal(userDataFolder, update.UserDataFolder);
        Assert.Equal(browser, update.BrowserDataDirectory);
        update.PrepareUserDataDirectory();
        Assert.False(update.RequiresDataReset);
        Assert.False(Directory.Exists(browser));
        Assert.Equal("index", File.ReadAllText(Path.Combine(resources, "dashboard", "index.html")));
        Assert.All(preserved, file => Assert.Equal("keep", File.ReadAllText(file)));
        Assert.Equal("1.3.2", File.ReadAllText(marker));
    }

    [Fact]
    public void ReopeningTheSameVersionKeepsDataButAVersionOnlyUpgradeResetsIt()
    {
        var root = CreateFixture();
        var update = WebViewDataMaintenance.PlanForCurrentContent(root, "1.3.1");
        update.PrepareUserDataDirectory();
        var data = Path.Combine(update.BrowserDataDirectory, "Default", "Local Storage", "current");
        Write(data, "keep between launches");
        using (var activeFile = File.Open(data, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            update.PrepareUserDataDirectory();
            var nextLaunch = WebViewDataMaintenance.PlanForCurrentContent(root, "1.3.1");
            Assert.False(nextLaunch.RequiresDataReset);
            nextLaunch.PrepareUserDataDirectory();
        }
        Assert.Equal("keep between launches", File.ReadAllText(data));
        var upgraded = WebViewDataMaintenance.PlanForCurrentContent(root, "1.3.2");
        Assert.True(upgraded.RequiresDataReset);
        upgraded.PrepareUserDataDirectory();
        Assert.False(File.Exists(data));
    }

    [Theory]
    [InlineData("modified")]
    [InlineData("added")]
    [InlineData("deleted")]
    [InlineData("renamed")]
    [InlineData("entry-modified")]
    public void SameVersionResourceChangesDoNotResetBrowserData(string change)
    {
        var root = CreateFixture();
        var css = Path.Combine(root, "resources", "dashboard", "assets", "app-same-name.css");
        Write(css, "old style");
        var initial = WebViewDataMaintenance.PlanForCurrentContent(root, "test-version");
        initial.PrepareUserDataDirectory();
        var data = Path.Combine(initial.BrowserDataDirectory, "Default", "keep");
        Write(data, "current browser data");
        if (change == "modified") File.WriteAllText(css, "new style");
        else if (change == "added") Write(Path.Combine(Path.GetDirectoryName(css)!, "font.woff2"), "font");
        else if (change == "deleted") File.Delete(css);
        else if (change == "renamed") File.Move(css, css + ".renamed");
        else File.WriteAllText(Path.Combine(root, "resources", "dashboard", "index.html"), "new entry");
        var nextLaunch = WebViewDataMaintenance.PlanForCurrentContent(root, "test-version");
        Assert.False(nextLaunch.RequiresDataReset);
        nextLaunch.PrepareUserDataDirectory();
        Assert.Equal("current browser data", File.ReadAllText(data));
        Assert.Equal("test-version", File.ReadAllText(MarkerPath(root)));
    }

    [Fact]
    public void VersionCheckDoesNotReadFrontendFileContents()
    {
        var root = CreateFixture();
        WebViewDataMaintenance.PlanForCurrentContent(root, "1.3.2").PrepareUserDataDirectory();
        var entry = Path.Combine(root, "resources", "dashboard", "index.html");
        using var locked = File.Open(entry, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.False(WebViewDataMaintenance.PlanForCurrentContent(root, "1.3.2").RequiresDataReset);
        Assert.True(WebViewDataMaintenance.PlanForCurrentContent(root, "1.3.3").RequiresDataReset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2|legacy-fingerprint")]
    [InlineData("3|1.3.2|legacy-fingerprint")]
    public void MissingOrLegacyMarkerResetsOnceThenStoresOnlyTheVersion(string? oldMarker)
    {
        var root = CreateFixture();
        var marker = MarkerPath(root);
        if (oldMarker is not null) File.WriteAllText(marker, oldMarker);
        var oldData = Path.Combine(root, "resources", "webview-data-v2", "EBWebView", "Default", "old");
        Write(oldData, "old data");
        var update = WebViewDataMaintenance.PlanForCurrentContent(root, "1.3.2");
        Assert.True(update.RequiresDataReset);
        update.PrepareUserDataDirectory();
        Assert.False(File.Exists(oldData));
        Assert.Equal("1.3.2", File.ReadAllText(marker));
        Write(oldData, "new data");
        var restarted = WebViewDataMaintenance.PlanForCurrentContent(root, "1.3.2");
        Assert.False(restarted.RequiresDataReset);
        restarted.PrepareUserDataDirectory();
        Assert.Equal("new data", File.ReadAllText(oldData));
    }

    [Fact]
    public void SettingsIconCacheLogsAndTimestampsDoNotTriggerRepeatedResets()
    {
        var root = CreateFixture();
        WebViewDataMaintenance.PlanForCurrentContent(root).PrepareUserDataDirectory();
        Write(Path.Combine(root, "settings.json"), "changed");
        Write(Path.Combine(root, "resources", "icon-cache", "icon.svg"), "changed");
        Write(Path.Combine(root, "resources", "logs", "host.log"), "changed");
        File.SetLastWriteTimeUtc(Path.Combine(root, "resources", "dashboard", "index.html"), DateTime.UtcNow.AddHours(-1));
        Assert.False(WebViewDataMaintenance.PlanForCurrentContent(root).RequiresDataReset);
    }

    [Fact]
    public void LockedProfileDoesNotAdvanceTheMarkerAndCanBeRetriedAfterExit()
    {
        var root = CreateFixture();
        WebViewDataMaintenance.PlanForCurrentContent(root, "old").PrepareUserDataDirectory();
        var marker = MarkerPath(root);
        var previous = File.ReadAllText(marker);
        var update = WebViewDataMaintenance.PlanForCurrentContent(root, "new");
        var data = Path.Combine(update.BrowserDataDirectory, "Default", "locked");
        Write(data, "in use");
        using (var locked = File.Open(data, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(update.PrepareUserDataDirectory);
            Assert.True(update.RequiresDataReset);
            Assert.Equal(previous, File.ReadAllText(marker));
        }
        update.PrepareUserDataDirectory();
        Assert.False(update.RequiresDataReset);
        Assert.False(Directory.Exists(update.BrowserDataDirectory));
        Assert.False(WebViewDataMaintenance.PlanForCurrentContent(root, "new").RequiresDataReset);
    }

    [Fact]
    public void FailedMarkerPersistenceCannotMarkTheResetComplete()
    {
        var root = CreateFixture();
        WebViewDataMaintenance.PlanForCurrentContent(root, "old").PrepareUserDataDirectory();
        var marker = MarkerPath(root);
        var previous = File.ReadAllText(marker);
        var update = WebViewDataMaintenance.PlanForCurrentContent(root, "new");
        using (var locked = File.Open(marker, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(update.PrepareUserDataDirectory);
            Assert.True(update.RequiresDataReset);
            Assert.Equal(previous, File.ReadAllText(marker));
        }
        update.PrepareUserDataDirectory();
        Assert.False(update.RequiresDataReset);
        Assert.Empty(Directory.GetFiles(update.UserDataFolder, "*.tmp"));
    }

    [Fact]
    public void IncompleteUiDoesNotEraseTheExistingProfile()
    {
        var root = CreateFixture();
        File.Delete(Path.Combine(root, "resources", "dashboard", "index.html"));
        var data = Path.Combine(root, "resources", "webview-data-v2", "EBWebView", "Default", "keep");
        Write(data, "keep");
        Assert.Throws<IOException>(() => WebViewDataMaintenance.PlanForCurrentContent(root));
        Assert.Equal("keep", File.ReadAllText(data));
        Assert.False(File.Exists(MarkerPath(root)));
    }

    [Fact]
    public void ProfileJunctionCannotRedirectDeletionToAnotherDirectory()
    {
        var root = CreateFixture();
        var link = Path.Combine(root, "resources", "webview-data-v2", "EBWebView");
        var outside = Path.Combine(root, "not-browser-data");
        Write(Path.Combine(outside, "keep"), "keep");
        using var command = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{outside}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        });
        Assert.NotNull(command);
        Assert.True(command.WaitForExit(5000));
        Assert.Equal(0, command.ExitCode);
        var update = WebViewDataMaintenance.PlanForCurrentContent(root);
        Assert.Throws<IOException>(update.PrepareUserDataDirectory);
        Assert.True(update.RequiresDataReset);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "keep")));
    }

    private static string MarkerPath(string root) => Path.Combine(root, "resources", "webview-data-v2", WebViewContentUpdate.MarkerFileName);
    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
    private string CreateFixture()
    {
        Write(Path.Combine(TestRoot, "resources", "dashboard", "index.html"), "index");
        Directory.CreateDirectory(Path.Combine(TestRoot, "resources", "webview-data-v2"));
        return TestRoot;
    }
}
