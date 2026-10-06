using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard.Tests;

[Collection("Release WebView")]
public sealed class ReleaseWebViewTests
{
    [Fact]
    [Trait("Category", "WebViewIntegration")]
    public async Task PreferencesAndTrayRestoreSurviveTheRealLightweightTimer()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var assets = Path.Combine(repository.FullName, "resources", "dashboard");
        Assert.True(File.Exists(Path.Combine(assets, "index.html")), "Build the production frontend first.");
        var root = Path.Combine(Path.GetTempPath(), "Dashboard.ReleaseWebView", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settings = AppSettings.Load(Path.Combine(root, "settings.json"), DpapiSecretProtector.Instance);
        settings.SetupCompleted = true;
        settings.StartCoreOnLaunch = false;
        settings.Autostart = false;
        settings.MinimizeToTray = true;
        settings.LightweightMode = true;
        settings.CorePath = Path.Combine(root, "absent-mihomo.exe");
        settings.ConfigPath = Path.Combine(root, "absent.yaml");
        settings.SingBoxCorePath = Path.Combine(root, "absent-sing-box.exe");
        settings.SingBoxConfigPath = Path.Combine(root, "absent.json");
        settings.DashboardApiUrl = settings.SingBoxApiUrl = "http://127.0.0.1:1";
        settings.DashboardSettings = new Dictionary<string, string>
        {
            ["config/auto-ip-check"] = "false", ["config/auto-theme"] = "false",
            ["config/default-theme"] = "light", ["config/language"] = "en-US"
        };
        settings.Save();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var host = new DashboardHost(settings, assets, useEphemeralPort: true);
            var contentUpdate = new WebViewContentUpdate(Path.Combine(root, "resources"), "release-test", requiresDataReset: true);
            using var form = new MainForm(host, contentUpdate);
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new(-15000, -15000);
            WebView2? View() => form.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<WebView2>()).SingleOrDefault();
            async Task<string> Script(string code) => await View()!.CoreWebView2.ExecuteScriptAsync(code);
            async Task Until(Func<Task<bool>> condition, int seconds = 25)
            {
                var timer = Stopwatch.StartNew();
                while (!await condition())
                {
                    if (timer.Elapsed > TimeSpan.FromSeconds(seconds)) throw new TimeoutException("Release WebView condition timed out.");
                    await Task.Delay(25);
                }
            }
            form.Shown += async (_, _) =>
            {
                try
                {
                    await Until(async () => View()?.CoreWebView2 is not null
                        && await Script("!!document.querySelector('.core-status-box')") == "true");
                    Assert.NotEqual(DashboardServer.DashboardOrigin.Port, host.DashboardUri.Port);
                    Assert.True(Directory.Exists(Path.Combine(contentUpdate.BrowserDataDirectory, "Default")));
                    Assert.False(Directory.Exists(Path.Combine(contentUpdate.BrowserDataDirectory, "EBWebView")));
                    await Script("localStorage.setItem('config/release-smoke', 'first'); true");
                    var first = form.FlushPreferencesAsync();
                    var duplicate = form.FlushPreferencesAsync();
                    Assert.Same(first, duplicate);
                    Assert.True(await first);
                    Assert.Equal("first", settings.DashboardSettings!["config/release-smoke"]);
                    Assert.Contains("first", File.ReadAllText(Path.Combine(root, "settings.json")));

                    // Exercise the production acknowledgement gate without an actual disk failure.
                    await Script("window.__releasePost=window.chrome.webview.postMessage.bind(window.chrome.webview); window.chrome.webview.postMessage=(m)=>window.__releasePost(m.type==='dashboardPreferencesFlushed'?{...m,success:false}:m); true");
                    Assert.False(await form.FlushPreferencesAsync());
                    await Script("window.chrome.webview.postMessage=window.__releasePost; true");
                    Assert.True(await form.FlushPreferencesAsync());

                    var original = View();
                    form.Close();
                    Assert.True(form.IsTrayTransitionInProgress);
                    form.ShowFromTray(); // intentionally inside the 220ms hide animation
                    await Task.Delay(750);
                    Assert.True(form.Visible);
                    Assert.NotEqual(FormWindowState.Minimized, form.WindowState);
                    Assert.False(form.IsTrayTransitionInProgress);
                    Assert.Same(original, View());

                    // No artificial advance: close immediately after a preference edit,
                    // then exercise the actual 60-second lightweight disposal timer.
                    await Script("localStorage.setItem('config/release-smoke', 'before-close'); window.dispatchEvent(new Event('dashboard-settings-changed')); true");
                    var hidden = Stopwatch.StartNew();
                    form.Close();
                    await Until(() => Task.FromResult(View() is null), seconds: 80);
                    Assert.True(hidden.Elapsed >= TimeSpan.FromSeconds(59));
                    Assert.Equal("before-close", settings.DashboardSettings!["config/release-smoke"]);
                    form.ShowFromTray();
                    await Until(async () => View()?.CoreWebView2 is not null
                        && await Script("!!document.querySelector('.core-status-box')") == "true");
                    Assert.NotSame(original, View());
                    Assert.Equal("\"before-close\"", await Script("localStorage.getItem('config/release-smoke')"));
                    Assert.True(form.Visible);
                    Assert.NotEqual(FormWindowState.Minimized, form.WindowState);
                    Assert.False(host.IsRunning);
                    var evidence = Path.Combine(repository.FullName, ".tmp", "release-webview");
                    Directory.CreateDirectory(evidence);
                    using (var image = File.Create(Path.Combine(evidence, "core-restored.png")))
                        await View()!.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                    File.WriteAllText(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new
                    {
                        version = DashboardVersion.Current,
                        runtime = View()!.CoreWebView2.Environment.BrowserVersionString,
                        isolatedOrigin = host.DashboardUri.ToString(),
                        preferencePersisted = true, rejectedFlushRetainsView = true,
                        quickTrayRestore = true, coldRestore = true,
                        realTimerMilliseconds = hidden.Elapsed.TotalMilliseconds,
                        coreStarted = host.IsRunning,
                        scope = "Production MainForm/host/UI; no core, TUN, autostart, UAC or physical DPI acceptance."
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    done.TrySetResult();
                }
                catch (Exception error) { done.TrySetException(error); }
                finally { await host.ShutdownAsync(); form.CloseForApplicationExit(); }
            };
            using var watchdog = new System.Windows.Forms.Timer { Interval = 135000 };
            watchdog.Tick += (_, _) => { done.TrySetException(new TimeoutException("Release WebView watchdog.")); form.CloseForApplicationExit(); };
            watchdog.Start();
            Application.Run(form);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            await done.Task.WaitAsync(TimeSpan.FromSeconds(145));
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }
    }
}
