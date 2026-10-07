using Microsoft.Web.WebView2.WinForms;
using System.Windows.Forms;

namespace Dashboard.Tests;

public sealed class CoreSetupLayoutTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData("zh-CN", "light")]
    [InlineData("en-US", "dark")]
    [Trait("Category", "WebViewIntegration")]
    public async Task FreshWindowShowsFullSetupWithTwoProfilesAndNoImplicitLaunch(string language, string theme)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = TestRoot;
        var initial = new SettingsStore(root);
        await using var api = new SyntheticClashApi { Rows = 0 };
        foreach (var kind in Enum.GetValues<CoreKind>())
        {
            var profile = initial.Current.Profile(kind);
            await initial.UpdateProfileAsync(kind, profile.Revision, new()
            {
                ExePath = profile.ExePath, ConfigPath = profile.ConfigPath, ApiUrl = api.Origin
            });
        }
        await initial.SavePreferencesAsync(new Dictionary<string, string>
        {
            ["config/language"] = language, ["config/default-theme"] = theme, ["config/auto-theme"] = "false", ["config/auto-ip-check"] = "false"
        });
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var host = new DashboardHost(root, Path.Combine(repository.FullName, "resources", "dashboard"), ephemeralPort: true, () => false);
            using var form = new MainForm(host, WebViewDataMaintenance.PlanForCurrentContent(root, dashboardDirectory: Path.Combine(repository.FullName, "resources", "dashboard")));
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-15000, -15000);
            form.Shown += async (_, _) =>
            {
                try
                {
                    WebView2? View() => form.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<WebView2>()).SingleOrDefault();
                    async Task<string> Script(string code) => View()?.CoreWebView2 is { } view ? await view.ExecuteScriptAsync(code) : "null";
                    var deadline = DateTime.UtcNow.AddSeconds(20);
                    while (await Script("!!document.querySelector('[data-testid=setup-guide] #core-exe')") != "true")
                    {
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("First-time setup did not appear.");
                        await Task.Delay(50);
                    }
                    Assert.False(host.Settings.SetupCompleted);
                    Assert.False(host.IsRunning);
                    Assert.Equal("true", await Script("(() => { const d=document.querySelector('[data-testid=setup-guide]'); return d.getAttribute('role')==='dialog' && d.open && d.matches(':modal') && d.querySelectorAll('[role=group] > button[aria-pressed]').length===2 && !document.querySelector('.core-toolbar select') && d.querySelectorAll('#core-exe,#core-config,#core-api,#core-secret').length===4 })()"));
                    var evidence = TestDirectory.ReportDirectory("core-chrome");
                    Directory.CreateDirectory(evidence);
                    foreach (var kind in new[] { "mihomo", "sing-box" })
                    {
                        await Script($"[...document.querySelectorAll('[data-testid=setup-guide] [role=group] > button[aria-pressed]')].find(b=>b.textContent.trim()==='{kind}').click(); true");
                        await Task.Delay(100);
                        Assert.Contains(kind == "mihomo" ? "external-controller" : "experimental.clash_api.external_controller", await Script("document.querySelector('[data-testid=setup-guide]').textContent"));
                        Assert.Equal("true", await Script("(() => { const d=document.querySelector('.core-profile-dialog').getBoundingClientRect(); const b=document.querySelector('[data-testid=setup-guide] .modal-action .btn-primary').getBoundingClientRect(); return d.left>=0 && d.right<=innerWidth && b.bottom<=innerHeight && b.right<=d.right })()"));
                        await using var image = File.Create(Path.Combine(evidence, $"setup-{language}-{theme}-{kind}.png"));
                        await View()!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
                    }
                    // The offscreen WebView changes activeElement without dispatching
                    // focus events unless its document is focused. Emulate only this
                    // document's focus, without activating a window on the user's desktop.
                    await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled", "{\"enabled\":true}");
                    await Script("const secret=document.querySelector('#core-secret'); secret.value='fixture-only-secret'; secret.dispatchEvent(new Event('input',{bubbles:true})); const eye=document.querySelector('button[aria-controls=core-secret]'); eye.focus(); eye.click(); true");
                    var revealState = await Script("JSON.stringify({type:document.querySelector('#core-secret').type,value:document.querySelector('#core-secret').value,pressed:document.querySelector('button[aria-controls=core-secret]').getAttribute('aria-pressed'),active:document.activeElement?.outerHTML,focused:document.hasFocus()})");
                    Assert.True(await Script("document.querySelector('#core-secret').type==='text' && document.querySelector('#core-secret').value==='fixture-only-secret'") == "true", revealState);
                    await Script("document.querySelector('#core-exe').focus(); true");
                    await Task.Delay(50);
                    var concealedState = await Script("JSON.stringify({type:document.querySelector('#core-secret').type,active:document.activeElement?.id,focused:document.hasFocus()})");
                    Assert.True(await Script("document.querySelector('#core-secret').type==='password'") == "true", concealedState);
                    Assert.False(host.IsRunning);
                    Assert.False(host.Settings.SetupCompleted);
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
            Application.Run(form);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(40)); }
        finally { Assert.True(thread.Join(TimeSpan.FromSeconds(30))); }
    }
}
