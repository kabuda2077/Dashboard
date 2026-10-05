using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Windows.Forms;

namespace Dashboard.Tests;

public sealed class DesktopSmokeTests
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
        var root = Path.Combine(Path.GetTempPath(), "Dashboard.DesktopSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var initial = new SettingsStore(root);
        await initial.SavePreferencesAsync(new Dictionary<string, string>
        {
            ["config/language"] = language, ["config/default-theme"] = theme, ["config/auto-theme"] = "false"
        });
        await initial.CompleteSetupAsync();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var host = new DashboardHost(root, Path.Combine(repository.FullName, "resources", "dashboard"), ephemeralPort: true, () => false);
            using var form = new MainForm(host, WebViewDataMaintenance.PlanForCurrentContent(root));
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
                    Assert.True(host.Settings.SetupCompleted);
                    Assert.Equal(2, host.Settings.SchemaVersion);
                    Assert.Equal("false", await Script("!!document.querySelector('[data-testid=\"setup-guide\"]')"));
                    Assert.Contains(language == "en-US" ? "Not connected" : "未连接", await Script("document.querySelector('.core-status-box').title"));
                    Assert.DoesNotContain(language == "en-US" ? "Stopped" : "未运行", await Script("document.querySelector('.core-status-box').textContent"));
                    Assert.Contains(language == "en-US" ? "Core logs" : "内核日志", await Script("document.body.textContent"));
                    Assert.Contains(language == "en-US" ? "Current downloads" : "当前下载", await Script("document.body.textContent"));
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
                    // Cold restoration is checked here; the real idle timer has its own slow lifecycle test.
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
                    var screenshots = Path.Combine(repository.FullName, ".tmp", "desktop-screenshots");
                    Directory.CreateDirectory(screenshots);
                    await using (var image = File.Create(Path.Combine(screenshots, $"{language}-{theme}-{width}.png")))
                        await View()!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
                    await host.ShutdownAsync();
                    completion.TrySetResult();
                }
                catch (Exception error) { completion.TrySetException(error); }
                finally { form.CloseForApplicationExit(); }
            };
            using var timer = new System.Windows.Forms.Timer { Interval = 120000 };
            timer.Tick += (_, _) => { completion.TrySetException(new TimeoutException("Production window smoke deadline expired.")); form.CloseForApplicationExit(); };
            timer.Start();
            Application.Run(form);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(130));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        try { Directory.Delete(root, true); } catch (IOException) { }
    }
}
