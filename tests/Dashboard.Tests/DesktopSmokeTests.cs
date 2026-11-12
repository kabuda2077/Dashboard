using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Windows.Forms;

namespace Dashboard.Tests;

public sealed class DesktopSmokeTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData("en-US", "light", 1360, 840)]
    [InlineData("zh-CN", "dark", 1120, 720)]
    [Trait("Category", "WebViewIntegration")]
    public async Task ProductionWindowBootstrapsSavesAndRecreatesItsViewWithoutLosingNewPreferences(string language, string theme, int width, int height)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = TestRoot;
        Directory.CreateDirectory(root);
        var initial = new SettingsStore(root);
        await initial.SavePreferencesAsync(new Dictionary<string, string>
        {
            ["config/language"] = language, ["config/default-theme"] = theme, ["config/auto-theme"] = "false"
        });
        var manualCore = Path.Combine(root, "manual-core.exe");
        var manualConfig = Path.Combine(root, "manual-config.yaml");
        await File.WriteAllTextAsync(manualCore, "Version-reader fixture; never executed.");
        await File.WriteAllTextAsync(manualConfig, "external-controller: 127.0.0.1:19191\n");
        await initial.UpdateProfileAsync(CoreKind.Mihomo, 0, new() { ExePath = manualCore, ConfigPath = manualConfig, ApiUrl = "http://127.0.0.1:9090" });
        await initial.CompleteSetupAsync();
        var versionOutput = "Mihomo Meta TanakaLun-Alpha-9fc52a02 windows amd64";
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var host = new DashboardHost(root, Path.Combine(repository.FullName, "resources", "dashboard"), ephemeralPort: true, () => false,
                readCoreVersion: (_, _, _) => Task.FromResult(versionOutput));
            var contentUpdate = WebViewDataMaintenance.PlanForCurrentContent(root, dashboardDirectory: Path.Combine(repository.FullName, "resources", "dashboard"));
            using var form = new MainForm(host, contentUpdate);
            form.Size = new System.Drawing.Size(width, height);
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-15000, -15000);
            form.Shown += async (_, _) =>
            {
                try
                {
                    WebView2? View() => form.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<WebView2>()).SingleOrDefault();
                    async Task<string> Script(string code)
                    {
                        var view = View();
                        if (view?.CoreWebView2 is null) return "null";
                        return await view.CoreWebView2.ExecuteScriptAsync(code);
                    }
                    async Task Until(Func<Task<bool>> condition, int seconds = 20)
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(seconds);
                        while (!await condition())
                        {
                            if (DateTime.UtcNow > deadline) throw new TimeoutException("Production window smoke condition timed out.");
                            await Task.Delay(100);
                        }
                    }
                    await Until(async () => await Script("!!document.querySelector('#core-exe')") == "true");
                    Assert.True(Directory.Exists(Path.Combine(contentUpdate.BrowserDataDirectory, "Default")));
                    Assert.False(Directory.Exists(Path.Combine(contentUpdate.BrowserDataDirectory, "EBWebView")));
                    Assert.True(host.Settings.SetupCompleted);
                    Assert.Equal(2, host.Settings.SchemaVersion);
                    Assert.Equal("false", await Script("!!document.querySelector('[data-testid=\"setup-guide\"]')"));
                    Assert.Contains(language == "en-US" ? "Not connected" : "未连接", await Script("document.querySelector('.core-status-box').title"));
                    Assert.DoesNotContain(language == "en-US" ? "Stopped" : "未运行", await Script("document.querySelector('.core-status-box').textContent"));
                    Assert.Contains(language == "en-US" ? "Core logs" : "内核日志", await Script("document.body.textContent"));
                    Assert.Contains(language == "en-US" ? "Current downloads" : "当前下载", await Script("document.body.textContent"));
                    await Until(async () => await Script("!!document.querySelector('.dashboard-section-title a[href]')") == "true");
                    Assert.Equal("https://github.com/TanakaLun/mihomo", JsonSerializer.Deserialize<string>(await Script("document.querySelector('.dashboard-section-title a').href")));
                    Assert.Contains(language == "en-US" ? "project page" : "项目主页", await Script("document.querySelector('.dashboard-section-title a').title"));
                    await Until(async () => await Script("!!document.querySelector('[data-testid=core-upgrade]:not(:disabled)')") == "true");
                    Assert.Contains(language == "en-US" ? "Upgrade core" : "升级内核", await Script("document.querySelector('[data-testid=core-upgrade]').textContent"));
                    Assert.Contains(language == "en-US" ? "Manual update" : "手动更新", await Script("document.querySelector('[data-testid=core-upgrade]').title"));
                    versionOutput = "Mihomo Meta custom-core windows amd64";
                    await host.RefreshMetadataAsync(force: true);
                    await Until(async () => await Script("!!document.querySelector('.dashboard-section-title a[aria-disabled=true]')") == "true");
                    Assert.Equal("true", await Script("document.querySelector('[data-testid=core-upgrade]').disabled"));
                    Assert.Equal("true", await Script("document.querySelector('[data-testid=core-upgrade]').title===document.querySelector('.dashboard-section-title a').title"));
                    Assert.Equal("true", await Script("(() => { const link=document.querySelector('.dashboard-section-title a');link.focus();return document.activeElement===link && !link.hasAttribute('href') && link.getAttribute('aria-disabled')==='true' && !!document.getElementById(link.getAttribute('aria-describedby')).textContent; })()"));
                    versionOutput = "Mihomo Meta TanakaLun-Alpha-9fc52a02 windows amd64";
                    await host.RefreshMetadataAsync(force: true);
                    await Until(async () => await Script("!!document.querySelector('[data-testid=core-upgrade]:not(:disabled)')") == "true");
                    Assert.Equal("https://github.com/TanakaLun/mihomo", JsonSerializer.Deserialize<string>(await Script("document.querySelector('.dashboard-section-title a').href")));
                    await Script("const input = document.querySelector('#core-api'); input.value = 'http://127.0.0.1:19191'; input.dispatchEvent(new Event('input', {bubbles:true})); true");
                    await Until(async () => await Script("!!document.querySelector('[data-testid=discard-core-profile]')") == "true");
                    Assert.False(await form.FlushPreferencesAsync());
                    Assert.NotEqual("http://127.0.0.1:19191", host.Settings.ActiveProfile.ApiUrl);
                    Assert.Equal("http://127.0.0.1:19191", JsonSerializer.Deserialize<string>(await Script("document.querySelector('#core-api').value")));
                    await Script("document.querySelector('[data-testid=save-core-profile]').click(); true");
                    await Until(() => Task.FromResult(host.Settings.ActiveProfile.ApiUrl == "http://127.0.0.1:19191"));
                    await Script("localStorage.setItem('config/custom-css', 'body { --smoke: 1; }'); true");
                    Assert.True(await form.FlushPreferencesAsync());
                    Assert.Equal("body { --smoke: 1; }", host.Preferences["config/custom-css"]);
                    var warmView = View();
                    for (var repeat = 0; repeat < 3; repeat++)
                    {
                        form.Close();
                        form.ShowFromTray();
                        await Until(async () => form.Visible && form.WindowState != FormWindowState.Minimized
                            && !form.IsTrayTransitionInProgress && await Script("!!document.querySelector('#core-exe')") == "true");
                        Assert.Same(warmView, View());
                        Assert.True(form.Visible);
                        Assert.NotEqual(FormWindowState.Minimized, form.WindowState);
                    }
                    // Cold restoration is checked here; the real idle timer has its own accelerated lifecycle test.
                    typeof(MainForm).GetMethod("DisposeDashboardView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, null);
                    var init = (Task)typeof(MainForm).GetMethod("EnsureDashboardInitializedAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, new object[] { "integration-cold-restore" })!;
                    await init;
                    await Until(async () => await Script("!!document.querySelector('#core-exe')") == "true");
                    Assert.Equal("body { --smoke: 1; }", JsonSerializer.Deserialize<string>(await Script("localStorage.getItem('config/custom-css')")));
                    Assert.Equal("http://127.0.0.1:19191", JsonSerializer.Deserialize<string>(await Script("document.querySelector('#core-api').value")));
                    form.Close();
                    await Until(() => Task.FromResult(!form.Visible));
                    form.ShowFromTray();
                    await Until(async () => form.Visible && form.WindowState != FormWindowState.Minimized
                        && !form.IsTrayTransitionInProgress && await Script("!!document.querySelector('#core-exe')") == "true");
                    Assert.Equal("http://127.0.0.1:19191", JsonSerializer.Deserialize<string>(await Script("document.querySelector('#core-api').value")));
                    await Task.Delay(300);
                    var screenshots = TestDirectory.ReportDirectory("desktop-screenshots");
                    Directory.CreateDirectory(screenshots);
                    await using (var image = File.Create(Path.Combine(screenshots, $"{language}-{theme}-{width}.png")))
                        await View()!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);

                    await Script("document.querySelector('[data-testid=settings-toggle]').click(); true");
                    await Until(async () => await Script("!!document.querySelector('#core-settings-about-dashboard')") == "true");
                    await Script("document.querySelector('[data-testid=settings-toggle]').scrollIntoView({block:'center'}); true");
                    await Task.Delay(300);
                    await using (var image = File.Create(Path.Combine(screenshots, $"settings-{language}-{theme}-{width}.png")))
                        await View()!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);

                    // Emulate focus for this off-screen test window without activating a user window.
                    await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled", "{\"enabled\":true}");
                    // Real keyboard input establishes :focus-visible; programmatic focus alone does not.
                    async Task Key(string key, string code, int virtualKey)
                    {
                        foreach (var type in new[] { "keyDown", "keyUp" })
                            await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                                JsonSerializer.Serialize(new { type, key, code, windowsVirtualKeyCode = virtualKey,
                                    text = type == "keyDown" && key == "Enter" ? "\r" : "" }));
                    }
                    await Key("Tab", "Tab", 9);
                    await Script("document.querySelector('[data-testid=settings-toggle]').focus({preventScroll:true}); true");
                    await using (var image = File.Create(Path.Combine(screenshots, $"settings-focus-{language}-{theme}-{width}.png")))
                        await View()!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
                    Assert.Equal("true", await Script("(() => { const button=document.querySelector('[data-testid=settings-toggle]'),style=getComputedStyle(button); return button.matches(':focus-visible') && (style.boxShadow!=='none' || (style.outlineStyle!=='none' && parseFloat(style.outlineWidth)>0)); })()"));
                    Assert.Equal("status", JsonSerializer.Deserialize<string>(await Script("document.querySelector('#core-settings-about-dashboard [aria-live]').getAttribute('role')")));
                    await Key("Enter", "Enter", 13);
                    await Until(async () => await Script("document.querySelector('[data-testid=settings-toggle]').getAttribute('aria-expanded')==='false'") == "true");
                    Assert.Equal("true", await Script("document.activeElement===document.querySelector('[data-testid=settings-toggle]')"));

                    await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"no-preference\"}]}");
                    foreach (var collapsed in new[] { true, false })
                    {
                        await Script($$"""
                            (() => { for (const [key,value] of Object.entries({'config/split-overview-page':'true','config/is-sidebar-collapsed':'{{collapsed.ToString().ToLowerInvariant()}}'})) {
                              const oldValue=localStorage.getItem(key);localStorage.setItem(key,value);
                              window.dispatchEvent(new StorageEvent('storage',{key,oldValue,newValue:value,storageArea:localStorage}));
                            } return true; })()
                            """);
                        await Until(async () => await Script("!!document.querySelector('[data-sidebar-route=overview] .motion-tiles')") == "true");
                        await Script("document.querySelector('[data-sidebar-route=core] > a').focus(); true");
                        await Key("Enter", "Enter", 13);
                        await Until(async () => await Script("getComputedStyle(document.querySelector('[data-sidebar-route=core] svg')).animationName.startsWith('nav-pop-')") == "true");
                        await Until(async () => await Script("!document.querySelector('[data-sidebar-route=core] .is-playing')") == "true");
                        await Script("document.querySelector('[data-sidebar-route=core] > a').click(); true");
                        await Until(async () => await Script("!!document.querySelector('[data-sidebar-route=core] .is-playing')") == "true");
                        await Until(async () => await Script("!document.querySelector('[data-sidebar-route=core] .is-playing')") == "true");
                        await using var image = File.Create(Path.Combine(screenshots, $"navigation-{language}-{theme}-{collapsed}.png"));
                        await View()!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
                    }
                    await Script("document.querySelector('[data-sidebar-route=core] > a').click(); true");
                    await Until(async () => await Script("getComputedStyle(document.querySelector('[data-sidebar-route=core] svg')).animationName.startsWith('nav-pop-')") == "true");
                    await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");
                    await Until(async () => await Script("!document.querySelector('[data-sidebar-route=core] .is-playing')") == "true");
                    await Script("document.querySelector('[data-sidebar-route=core] > a').click(); true");
                    Assert.Equal("true", await Script("(() => { const icon=document.querySelector('[data-sidebar-route=core] svg');return !icon.classList.contains('is-playing') && getComputedStyle(icon).animationName==='none'; })()"));
                    await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"no-preference\"}]}");
                    Assert.Equal("true", await Script("getComputedStyle(document.querySelector('[data-sidebar-route=core] svg')).animationName==='none'"));
                    await host.ShutdownAsync();
                    await TestBrowser.DisposeAsync(form);
                    completion.TrySetResult();
                }
                catch (Exception error) { completion.TrySetException(error); }
                finally
                {
                    try { await host.ShutdownAsync(); await TestBrowser.DisposeAsync(form); }
                    catch (Exception error) { completion.TrySetException(error); }
                    finally { form.CloseForApplicationExit(); }
                }
            };
            using var timer = new System.Windows.Forms.Timer { Interval = 120000 };
            timer.Tick += (_, _) => { completion.TrySetException(new TimeoutException("Production window smoke deadline expired.")); form.CloseForApplicationExit(); };
            timer.Start();
            Application.Run(form);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(130)); }
        finally { Assert.True(thread.Join(TimeSpan.FromSeconds(30))); }
    }
}
