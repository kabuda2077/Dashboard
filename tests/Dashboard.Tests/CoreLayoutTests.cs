using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Windows.Forms;

namespace Dashboard.Tests;

public sealed class CoreLayoutTests
{
    [Theory]
    [InlineData("zh-CN", "light")]
    [InlineData("en-US", "dark")]
    [Trait("Category", "WebViewIntegration")]
    public async Task CoreLayoutPreservesAlignmentResponsiveFieldsAndBoundedOutput(string language, string theme)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = Path.Combine(Path.GetTempPath(), "Dashboard.CoreLayout", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var initial = new SettingsStore(root);
        await initial.SavePreferencesAsync(new Dictionary<string, string>
        {
            ["config/language"] = language, ["config/default-theme"] = theme,
            ["config/auto-theme"] = "false", ["config/is-sidebar-collapsed"] = "true"
        });
        await initial.CompleteSetupAsync();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var host = new DashboardHost(root, Path.Combine(repository.FullName, "resources", "dashboard"), ephemeralPort: true, () => false);
            using var form = new MainForm(host, WebViewDataMaintenance.PlanForCurrentContent(root));
            form.ShowInTaskbar = false;
            // Below the production desktop minimum, exercise the same UI's narrow
            // layout in this isolated window; do not change the product minimum.
            form.MinimumSize = new System.Drawing.Size(640, 480);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-15000, -15000);
            form.Shown += async (_, _) =>
            {
                try
                {
                    WebView2? View() => form.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<WebView2>()).SingleOrDefault();
                    async Task<string> Script(string code) => View()?.CoreWebView2 is { } core ? await core.ExecuteScriptAsync(code) : "null";
                    async Task Until(Func<Task<bool>> condition)
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(20);
                        while (!await condition())
                        {
                            if (DateTime.UtcNow > deadline) throw new TimeoutException("Core layout did not settle.");
                            await Task.Delay(50);
                        }
                    }
                    await Until(async () => await Script("!!document.querySelector('#core-exe')") == "true");
                    var evidence = Path.Combine(repository.FullName, ".tmp", "core-layout");
                    Directory.CreateDirectory(evidence);
                    // Chinese covers sidebar geometry and desktop minimum; English covers long labels
                    // at wide/narrow widths and the intermediate expanded-sidebar boundary.
                    var cases = language == "zh-CN"
                        ? new[] { (2200, true), (2200, false), (1120, true), (1120, false), (640, true) }
                        : new[] { (2200, true), (1500, false), (640, true) };
                    foreach (var (width, collapsed) in cases)
                    {
                        form.ClientSize = new System.Drawing.Size(width, 1000);
                        await Script($$"""
                            (() => {
                              const key = 'config/is-sidebar-collapsed', value = '{{collapsed.ToString().ToLowerInvariant()}}';
                              const oldValue = localStorage.getItem(key);
                              localStorage.setItem(key, value);
                              window.dispatchEvent(new StorageEvent('storage', {key, oldValue, newValue: value, storageArea: localStorage}));
                              const input = document.querySelector('#core-exe');
                              input.value = 'C:/very-long-directory/'.repeat(12) + 'mihomo.exe';
                              input.dispatchEvent(new Event('input', {bubbles:true}));
                              return true;
                            })()
                            """);
                        await Until(async () => await Script($"!!document.querySelector('.home-page.sidebar-' + (matchMedia('(max-width: 768px)').matches || {collapsed.ToString().ToLowerInvariant()} ? 'collapsed' : 'expanded'))") == "true");
                        await Task.Delay(400); // Finish the actual sidebar transition before geometry assertions.
                        var before = await Script(Measure);
                        using (var metrics = JsonDocument.Parse(before)) Assert.True(metrics.RootElement.GetProperty("errors").GetArrayLength() == 0, before);
                        View()!.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                        {
                            protocolVersion = 2, type = "logAppend", logText = string.Join('\n', Enumerable.Range(0, 600).Select(i => $"[{i:D4}] synthetic output for scroll verification"))
                        }));
                        await Until(async () => await Script("document.querySelector('.core-output-log').scrollHeight > document.querySelector('.core-output-log').clientHeight") == "true");
                        var after = await Script(Measure);
                        using (var metrics = JsonDocument.Parse(after))
                        {
                            Assert.True(metrics.RootElement.GetProperty("errors").GetArrayLength() == 0, after);
                            using var previous = JsonDocument.Parse(before);
                            Assert.InRange(Math.Abs(metrics.RootElement.GetProperty("outputHeight").GetDouble() - previous.RootElement.GetProperty("outputHeight").GetDouble()), 0, 1);
                        }
                        // A synthetic PID tests actual responsive rendering without starting a core.
                        View()!.CoreWebView2.PostWebMessageAsJson(HostBridgeJson.Serialize(HostOutboundMessage.Runtime(host.BuildRuntimeState(false) with { IsRunning = true, ProcessId = 12345 })));
                        await Until(async () => await Script("!!document.querySelector('.core-status-pid')") == "true");
                        Assert.Equal("true", await Script("(() => { const t=document.querySelector('.core-runtime-toolbar'), p=document.querySelector('.core-status-pid'); const s=getComputedStyle(t); const width=t.clientWidth-parseFloat(s.paddingLeft)-parseFloat(s.paddingRight); return (getComputedStyle(p).display==='none') === (width<=559) && document.querySelector('.core-status-box').title.includes('PID 12345') })()"));
                        if (width == 2200 && collapsed)
                        {
                            foreach (var contentWidth in new[] { 559, 560 })
                            {
                                await Script($"document.querySelector('.core-runtime-toolbar').style.width='{contentWidth + 30}px'; true");
                                Assert.Equal(contentWidth == 559 ? "true" : "false", await Script("getComputedStyle(document.querySelector('.core-status-pid')).display==='none'"));
                            }
                            await Script("document.querySelector('.core-runtime-toolbar').style.width=''; true");
                        }
                        View()!.CoreWebView2.PostWebMessageAsJson(HostBridgeJson.Serialize(HostOutboundMessage.Runtime(host.BuildRuntimeState(false))));
                        await Until(async () => await Script("!document.querySelector('.core-status-pid')") == "true");
                        var name = $"{language}-{theme}-{width}-{(collapsed ? "collapsed" : "expanded")}";
                        await File.WriteAllTextAsync(Path.Combine(evidence, name + ".json"), after);
                        await using var screenshot = File.Create(Path.Combine(evidence, name + ".png"));
                        await View()!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                        await Script("document.querySelector('.core-content').parentElement.scrollTop = 250; true");
                        Assert.Equal("true", await Script("Math.abs(document.querySelector('.core-toolbar').getBoundingClientRect().top - 12) < 2"));
                        await Script("document.querySelector('.core-content').parentElement.scrollTop = 0; true");
                        if (collapsed && width is 2200 or 640)
                        {
                            await Script("document.querySelector('.core-top-button.btn-primary').click(); true");
                            await Task.Delay(100);
                            Assert.Equal("true", await Script("(() => { const d=document.querySelector('[data-testid=switch-core-dialog]'); const r=d.querySelector('.core-profile-dialog').getBoundingClientRect(); const b=d.querySelector('.modal-action .btn-primary').getBoundingClientRect(); return d.open && d.matches(':modal') && r.left>=0 && r.right<=innerWidth && b.bottom<=innerHeight && b.right<=r.right })()"));
                            if (width == 2200)
                            {
                                await Script("const i=document.querySelector('#core-exe'); i.value='C:/unsaved-target.exe'; i.dispatchEvent(new Event('input',{bubbles:true})); true");
                                var original = host.BuildState(false);
                                var profiles = new Dictionary<string, CoreProfileState>(original.Profiles);
                                profiles["sing-box"] = profiles["sing-box"] with { Revision = 1, ExePath = "C:/remote-target.exe" };
                                View()!.CoreWebView2.PostWebMessageAsJson(HostBridgeJson.Serialize(new { protocolVersion = 2, type = "state", state = original with { Profiles = profiles } }));
                                await Until(async () => await Script("!!document.querySelector('[data-testid=reload-dialog-draft]')") == "true");
                                foreach (var confirm in new[] { false, true })
                                {
                                    await Script("document.querySelector('[data-testid=reload-dialog-draft]').click(); true");
                                    await Until(async () => await Script("!!document.querySelector('[data-testid=reload-draft-confirmation]')") == "true");
                                    Assert.Equal("true", await Script("(() => { const p=document.querySelector('[data-testid=reload-draft-confirmation]'), b=p.querySelector('button'), r=b.getBoundingClientRect(); b.focus(); return p.closest('dialog').matches(':modal') && document.activeElement===b && b.contains(document.elementFromPoint(r.left+r.width/2,r.top+r.height/2)) })()"));
                                    await Script($"document.querySelectorAll('[data-testid=reload-draft-confirmation] button')[{(confirm ? 1 : 0)}].click(); true");
                                    await Until(async () => await Script("!document.querySelector('[data-testid=reload-draft-confirmation]')") == "true");
                                    Assert.Equal(confirm ? "C:/remote-target.exe" : "C:/unsaved-target.exe", JsonSerializer.Deserialize<string>(await Script("document.querySelector('#core-exe').value")));
                                }
                            }
                            await using var switchImage = File.Create(Path.Combine(evidence, name + "-switch.png"));
                            await View()!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, switchImage);
                            await Script("document.querySelector('[data-testid=switch-core-dialog] .modal-action .dashboard-action-btn').click(); true");
                            await Until(async () => await Script("!document.querySelector('[data-testid=switch-core-dialog]')") == "true");
                        }
                    }
                    form.ClientSize = new System.Drawing.Size(2200, 1000);
                    await Script("location.hash='#/core?scrollTo=connectionSettings'; true");
                    await Until(async () => await Script("!!document.querySelector('#core-settings-connectionSettings .settings-grid')") == "true");
                    await Task.Delay(500); // Finish the user-visible smooth scroll.
                    Assert.Equal("true", await Script("(() => { const r=document.querySelector('#core-settings-connectionSettings').getBoundingClientRect(); return r.top>=document.querySelector('.core-toolbar').getBoundingClientRect().bottom && r.top<innerHeight })()"));
                    await host.ShutdownAsync();
                    completion.TrySetResult();
                }
                catch (Exception error) { completion.TrySetException(error); }
                finally { form.CloseForApplicationExit(); }
            };
            using var timer = new System.Windows.Forms.Timer { Interval = 90000 };
            timer.Tick += (_, _) => { completion.TrySetException(new TimeoutException("Core layout test deadline expired.")); form.CloseForApplicationExit(); };
            timer.Start();
            Application.Run(form);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(100));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private const string Measure = """
        (() => {
          const errors = [], near = (a,b) => Math.abs(a-b) < 2;
          const rect = el => el.getBoundingClientRect();
          const content = document.querySelector('.core-content');
          const profile = content.querySelector('.core-layout > section');
          const output = document.querySelector('.core-output-panel');
          const backend = document.querySelector('#core-settings-backendSettings .core-layout > div');
          const profileCard = profile.querySelector('.settings-grid'), backendCard = backend.querySelector('.settings-grid');
          const toolbar = document.querySelector('.core-toolbar');
          const cr = rect(content), pr = rect(profileCard), br = rect(backendCard), tr = rect(toolbar), or = rect(output);
          const contentWidth = content.clientWidth - parseFloat(getComputedStyle(content).paddingLeft) - parseFloat(getComputedStyle(content).paddingRight);
          const wide = contentWidth >= 1000;
          if (!near(tr.left, pr.left)) errors.push('toolbar/profile left edge');
          const lamp = toolbar.querySelector('.core-runtime-dot'), lr = rect(lamp), sr = rect(toolbar.querySelector('.core-status-box'));
          const precise = (a,b) => Math.abs(a-b) < 0.5;
          if (!precise(lr.left - pr.left, 2) || !precise(sr.left - pr.left, 30)) errors.push('v1.2.2 lamp/status optical insets');
          if (!precise(lr.width, 12) || !precise(lr.height, 12) || !precise(sr.left - lr.right, 16)) errors.push('lamp diameter/status gap');
          if (!precise(lr.top + lr.height/2, sr.top + sr.height/2)) errors.push('lamp/status vertical center');
          const originalClass = lamp.className, reference = document.createElement('span');
          toolbar.appendChild(reference);
          try {
            for (const state of ['bg-success', 'bg-warning']) {
              lamp.className = 'core-runtime-dot ' + state;
              const style = getComputedStyle(lamp);
              reference.style.boxShadow = `0 0 0 4px color-mix(in srgb, ${style.backgroundColor} 30%, transparent)`;
              if (style.boxShadow !== getComputedStyle(reference).boxShadow) errors.push(state + ' lamp glow');
            }
          } finally { lamp.className = originalClass; reference.remove(); }
          const save = document.querySelector('[data-testid=save-core-profile]');
          const secret = document.querySelector('#core-secret');
          if (save.parentElement !== secret.parentElement || !near(rect(save).top,rect(secret).top)) errors.push('save/secret inline');
          if (profile.querySelector('h2').textContent.includes('·')) errors.push('duplicate core name in heading');
          if (!near(pr.left, br.left) || !near(pr.width, br.width)) errors.push('profile/backend columns');
          const optionTitle = profileCard.nextElementSibling, optionRow = profile.querySelector('.setting-item');
          if (!precise(rect(optionTitle).top - pr.bottom, 16)) errors.push('profile/options title gap');
          if (contentWidth >= 640) {
            for (const row of profileCard.querySelectorAll('.core-profile-row'))
              if (Math.abs(rect(row).height - rect(optionRow).height) > 1) errors.push('profile/options row height');
          }
          const actions = [...toolbar.querySelectorAll('.core-runtime-actions button')];
          if (toolbar.querySelector('select') || actions.length !== 3) errors.push('runtime toolbar must only have switch/start/stop');
          if (wide && !near(tr.right, pr.right)) errors.push('toolbar/profile right edge');
          if (wide && (!near(pr.top, or.top) || !near(or.bottom, rect(profile).bottom))) errors.push('output does not align with left column');
          if (!wide && or.top < rect(profile).bottom) errors.push('narrow layout did not stack');
          for (const id of ['core-exe','core-config','core-api','core-secret']) {
            const input = document.getElementById(id), label = document.querySelector(`label[for="${id}"]`);
            const ir = rect(input), lr = rect(label);
            if (contentWidth >= 640 && (lr.right > ir.left || !near(lr.top+lr.height/2,ir.top+ir.height/2))) errors.push(id + ' label/input row');
            if (contentWidth < 640 && lr.bottom > ir.top) errors.push(id + ' narrow field');
            if (ir.width < 60 || ir.right > pr.right || ir.left < pr.left) errors.push(id + ' clipped input');
          }
          const style = getComputedStyle(output);
          if (style.backgroundColor === 'rgba(0, 0, 0, 0)' || parseFloat(style.borderTopLeftRadius) === 0) errors.push('output card surface');
          const windows = document.querySelector('[aria-label="最小化"]');
          if (windows && tr.right > rect(windows).left) errors.push('toolbar overlaps window controls');
          if (content.parentElement.scrollWidth > content.parentElement.clientWidth) errors.push('page horizontal overflow');
          return {errors, viewportWidth:innerWidth, contentWidth, wide, toolbarLeft:tr.left, profileLeft:pr.left, backendLeft:br.left, profileWidth:pr.width, backendWidth:br.width, outputHeight:or.height, lampInset:lr.left-pr.left, statusInset:sr.left-pr.left, lampGap:sr.left-lr.right, lampCenterOffset:lr.top+lr.height/2-sr.top-sr.height/2};
        })()
        """;
}
