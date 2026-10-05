using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard.Tests;

[Collection("Release WebView")]
public sealed class StartupWebViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "WebViewIntegration")]
    public async Task NullHostPreferencesBootFreshAndPreserveLegacyBrowserPreferences(bool legacy)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var assets = Path.Combine(repository.FullName, "resources", "dashboard");
        Assert.True(File.Exists(Path.Combine(assets, "index.html")), "Build the production frontend first.");
        var root = Path.Combine(Path.GetTempPath(), "Dashboard.StartupWebView", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");
        if (legacy) File.WriteAllText(settingsPath, "{\"SetupCompleted\":true}");
        Assert.Equal(legacy, File.Exists(settingsPath));
        var settings = AppSettings.Load(settingsPath, DpapiSecretProtector.Instance);
        Assert.True(File.Exists(settingsPath));
        Assert.Null(settings.DashboardSettings);
        Assert.Equal(legacy, settings.SetupCompleted);
        // Only redirect process/API targets; never pre-fill DashboardSettings.
        // A populated preferences dictionary hid the 1.3.0 startup regression.
        settings.StartCoreOnLaunch = settings.Autostart = false;
        settings.CorePath = Path.Combine(root, "absent-mihomo.exe");
        settings.ConfigPath = Path.Combine(root, "absent.yaml");
        settings.SingBoxCorePath = Path.Combine(root, "absent-sing-box.exe");
        settings.SingBoxConfigPath = Path.Combine(root, "absent.json");
        settings.DashboardApiUrl = settings.SingBoxApiUrl = "http://127.0.0.1:1";
        settings.Save();
        Directory.CreateDirectory(AppSettings.WebViewUserDataDirectory); // isolated testhost output; no legacy profile migration
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var host = new DashboardHost(settings, assets, useEphemeralPort: true);
                using var pump = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-15000, -15000) };
                MainForm? dashboard = null;
                WebView2? View() => dashboard?.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<WebView2>()).SingleOrDefault();
                async Task<string> Script(string code) => await View()!.CoreWebView2.ExecuteScriptAsync(code);
                pump.Shown += async (_, _) =>
                {
                    try
                    {
                        if (legacy)
                        {
                            // Seed this unique origin before application startup. There is no
                            // native message receiver on this temporary view, so the app waits
                            // for its handshake and cannot overwrite the seeded preferences.
                            using var seed = new WebView2 { Dock = DockStyle.Fill };
                            pump.Controls.Add(seed);
                            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: AppSettings.WebViewUserDataDirectory);
                            await seed.EnsureCoreWebView2Async(environment);
                            var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                            seed.CoreWebView2.NavigationCompleted += (_, e) =>
                            {
                                if (e.IsSuccess) navigated.TrySetResult();
                                else navigated.TrySetException(new InvalidOperationException("Seed navigation failed: " + e.WebErrorStatus));
                            };
                            seed.CoreWebView2.Navigate(host.DashboardUri.ToString());
                            await navigated.Task.WaitAsync(TimeSpan.FromSeconds(10));
                            await seed.CoreWebView2.ExecuteScriptAsync("localStorage.setItem('config/startup-sentinel','legacy-value'); localStorage.setItem('config/language','en-US'); localStorage.setItem('config/auto-ip-check','false'); true");
                            Assert.Null(settings.DashboardSettings);
                        }
                        dashboard = new MainForm(host, new WebViewContentUpdate(
                            Path.Combine(root, "content-marker"), "startup-test", requiresCacheInvalidation: true))
                        {
                            ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-15000, -15000)
                        };
                        dashboard.Show();
                        var elapsed = Stopwatch.StartNew();
                        while (true)
                        {
                            if (View()?.CoreWebView2 is not null)
                            {
                                var failure = await Script("document.querySelector('#app > p')?.textContent ?? ''");
                                Assert.DoesNotContain("无法加载", failure);
                                var ready = await Script("!!document.querySelector('.core-status-box') && "
                                    + (legacy ? "!document.querySelector('.modal.modal-open')" : "document.querySelector('.modal.modal-open h3')?.textContent === '首次启动设置'"));
                                if (ready == "true") break;
                            }
                            if (elapsed.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Production startup did not finish.");
                            await Task.Delay(25);
                        }
                        Assert.NotEqual(DashboardServer.DashboardOrigin.Port, host.DashboardUri.Port);
                        Assert.Equal(legacy ? "\"legacy-value\"" : "null", await Script("localStorage.getItem('config/startup-sentinel')"));
                        if (legacy) Assert.Equal("\"en-US\"", await Script("localStorage.getItem('config/language')"));
                        Assert.Equal(legacy, settings.SetupCompleted);
                        Assert.True(await dashboard.FlushPreferencesAsync());
                        Assert.NotNull(settings.DashboardSettings);
                        if (legacy) Assert.Equal("legacy-value", settings.DashboardSettings["config/startup-sentinel"]);
                        Assert.False(host.IsRunning);
                        var evidence = Path.Combine(repository.FullName, ".tmp", "startup-webview");
                        Directory.CreateDirectory(evidence);
                        var name = legacy ? "legacy-null-preferences" : "fresh-no-settings";
                        await Task.Delay(500); // let the initial modal transition settle for the evidence image
                        using (var image = File.Create(Path.Combine(evidence, name + ".png")))
                            await View()!.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                        File.WriteAllText(Path.Combine(evidence, name + ".json"), JsonSerializer.Serialize(new
                        {
                            version = DashboardVersion.Current,
                            runtime = View()!.CoreWebView2.Environment.BrowserVersionString,
                            initialSettingsFileExisted = legacy, initialHostPreferencesWereNull = true,
                            applicationMounted = true, setupGuideShown = !legacy,
                            legacyPreferencesPreserved = legacy, preferencesSaved = true, coreStarted = false,
                            scope = "Production loader/host/wire/UI, isolated settings and origin. No user data, core, UAC or system TUN changes."
                        }, new JsonSerializerOptions { WriteIndented = true }));
                        done.TrySetResult();
                    }
                    catch (Exception error) { done.TrySetException(error); }
                    finally
                    {
                        dashboard?.CloseForApplicationExit();
                        dashboard?.Dispose();
                        await host.ShutdownAsync();
                        pump.Close();
                    }
                };
                using var watchdog = new System.Windows.Forms.Timer { Interval = 40000 };
                watchdog.Tick += (_, _) =>
                {
                    done.TrySetException(new TimeoutException("Startup WebView watchdog."));
                    dashboard?.CloseForApplicationExit();
                    pump.Close();
                };
                watchdog.Start();
                Application.Run(pump);
            }
            catch (Exception error) { done.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            await done.Task.WaitAsync(TimeSpan.FromSeconds(45));
        }
        finally
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Startup test UI thread did not terminate.");
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
