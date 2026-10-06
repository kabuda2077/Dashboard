using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard.Tests;

[Collection("Release WebView")]
public sealed class WebViewResetIntegrationTests
{
    [Fact]
    [Trait("Category", "WebViewIntegration")]
    public async Task UpgradeDeletesOldBrowserStorageAndCreatesOnlyOneEbWebViewLevel()
    {
        var root = Path.Combine(Path.GetTempPath(), "Dashboard.WebViewReset", Guid.NewGuid().ToString("N"));
        var resources = Path.Combine(root, "resources");
        var assets = Path.Combine(resources, "dashboard");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(Path.Combine(resources, "icon-cache"));
        File.WriteAllText(Path.Combine(assets, "index.html"), "<!doctype html><body>old UI</body>");
        File.WriteAllText(Path.Combine(root, "settings.json"), "host settings sentinel");
        File.WriteAllText(Path.Combine(resources, "icon-cache", "icon.svg"), "icon sentinel");
        File.WriteAllText(Path.Combine(resources, WebViewContentUpdate.MarkerFileName), "2|old-policy");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var server = new DashboardServer(assets, Path.Combine(resources, "icon-cache"));
                var origin = server.StartForTests();
                using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-15000, -15000) };
                WebView2? view = null;
                CoreWebView2Environment? environment = null;
                TaskCompletionSource? browserExited = null;
                async Task Open(string userDataFolder)
                {
                    environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
                    browserExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var exit = browserExited;
                    environment.BrowserProcessExited += (_, _) => exit.TrySetResult();
                    view = new WebView2 { Dock = DockStyle.Fill };
                    form.Controls.Add(view);
                    await view.EnsureCoreWebView2Async(environment);
                    var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    view.CoreWebView2.NavigationCompleted += (_, e) =>
                    {
                        if (e.IsSuccess) navigated.TrySetResult();
                        else navigated.TrySetException(new IOException("Test page navigation failed: " + e.WebErrorStatus));
                    };
                    view.CoreWebView2.Navigate(origin.ToString());
                    await navigated.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
                async Task CloseBrowser()
                {
                    view?.Dispose();
                    view = null;
                    if (browserExited is not null) await browserExited.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    environment = null;
                }
                Task<string> Script(string code) => view!.CoreWebView2.ExecuteScriptAsync(code);
                async Task Until(string expression)
                {
                    var timer = Stopwatch.StartNew();
                    while (await Script(expression) != "true")
                    {
                        if (timer.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Browser storage operation timed out.");
                        await Task.Delay(25);
                    }
                }
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        // Reproduce the old SDK root exactly: it creates EBWebView/EBWebView.
                        await Open(Path.Combine(resources, "EBWebView"));
                        await Script("localStorage.setItem('old-browser-setting','old'); window.__seedDone=false; const r=indexedDB.open('old-profile-db',1); r.onupgradeneeded=()=>r.result.createObjectStore('items'); r.onsuccess=()=>{const db=r.result;const tx=db.transaction('items','readwrite');tx.objectStore('items').put('old image/history','sentinel');tx.oncomplete=()=>{db.close();window.__seedDone=true}}; true");
                        await Until("window.__seedDone === true");
                        Assert.True(Directory.Exists(Path.Combine(resources, "EBWebView", "EBWebView", "Default")));
                        await CloseBrowser(); // never delete a live browser's data

                        File.WriteAllText(Path.Combine(assets, "index.html"), "<!doctype html><body>new UI</body>");
                        var update = WebViewDataMaintenance.PlanForCurrentContent(root, "test-new-version");
                        Assert.True(update.RequiresDataReset);
                        update.PrepareUserDataDirectory();
                        Assert.False(Directory.Exists(update.BrowserDataDirectory));
                        Assert.Equal(resources, update.UserDataFolder);
                        Assert.Equal("host settings sentinel", File.ReadAllText(Path.Combine(root, "settings.json")));
                        Assert.Equal("icon sentinel", File.ReadAllText(Path.Combine(resources, "icon-cache", "icon.svg")));

                        await Open(update.UserDataFolder);
                        Assert.Equal("\"new UI\"", await Script("document.body.textContent"));
                        Assert.Equal("null", await Script("localStorage.getItem('old-browser-setting')"));
                        await Script("window.__dbNames=undefined; indexedDB.databases().then(xs=>window.__dbNames=xs.map(x=>x.name)); true");
                        await Until("window.__dbNames !== undefined");
                        Assert.Equal("false", await Script("window.__dbNames.includes('old-profile-db')"));
                        Assert.True(Directory.Exists(Path.Combine(resources, "EBWebView", "Default")));
                        Assert.False(Directory.Exists(Path.Combine(resources, "EBWebView", "EBWebView")));
                        var runtime = environment!.BrowserVersionString;
                        await Script("localStorage.setItem('current-browser-setting','keep'); true");
                        await CloseBrowser();

                        // Replacing frontend files without changing the app version must
                        // not trigger another full profile reset.
                        File.WriteAllText(Path.Combine(assets, "index.html"), "<!doctype html><body>same-version rebuild</body>");
                        var ordinaryRestart = WebViewDataMaintenance.PlanForCurrentContent(root, "test-new-version");
                        Assert.False(ordinaryRestart.RequiresDataReset);
                        ordinaryRestart.PrepareUserDataDirectory();
                        await Open(ordinaryRestart.UserDataFolder);
                        Assert.Equal("\"same-version rebuild\"", await Script("document.body.textContent"));
                        Assert.Equal("\"keep\"", await Script("localStorage.getItem('current-browser-setting')"));
                        await CloseBrowser();

                        var repository = new DirectoryInfo(AppContext.BaseDirectory);
                        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj"))) repository = repository.Parent;
                        Assert.NotNull(repository);
                        var evidence = Path.Combine(repository.FullName, ".tmp", "webview-reset");
                        Directory.CreateDirectory(evidence);
                        File.WriteAllText(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new
                        {
                            runtime, legacyDoubleLayoutReproduced = true,
                            oldLocalStorageRemoved = true, oldIndexedDbRemoved = true,
                            singleEbWebViewLevel = true, newUiLoaded = true,
                            sameVersionResourceChangePreservesData = true,
                            hostSettingsAndIconCacheUnchanged = true,
                            scope = "Isolated temporary data and real WebView; no installed Dashboard, core or system setting modified."
                        }, new JsonSerializerOptions { WriteIndented = true }));
                        done.TrySetResult();
                    }
                    catch (Exception error) { done.TrySetException(error); }
                    finally
                    {
                        try { await CloseBrowser(); }
                        catch (Exception error) { done.TrySetException(error); }
                        form.Close();
                    }
                };
                using var watchdog = new System.Windows.Forms.Timer { Interval = 100000 };
                watchdog.Tick += (_, _) => { done.TrySetException(new TimeoutException("WebView reset watchdog.")); view?.Dispose(); form.Close(); };
                watchdog.Start();
                Application.Run(form);
            }
            catch (Exception error) { done.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(110)); }
        finally
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WebView test thread did not exit.");
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
