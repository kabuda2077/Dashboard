using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Windows.Forms;

namespace Dashboard.Tests;

public sealed class CoreLayoutTests : TemporaryDirectoryTest
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
        // Comparison capture reuses this host; the default run keeps representative evidence.
        var dashboardDirectory = Environment.GetEnvironmentVariable("DASHBOARD_UPSTREAM_UI_RESOURCES")
            ?? Path.Combine(repository.FullName, "resources", "dashboard");
        var baselineEvidence = Environment.GetEnvironmentVariable("DASHBOARD_UPSTREAM_UI_BASELINE") == "1";
        var fullComparison = baselineEvidence || Environment.GetEnvironmentVariable("DASHBOARD_UI_VISUAL_COMPARISON") == "1";
        var root = TestRoot;
        Directory.CreateDirectory(root);
        var initial = new SettingsStore(root);
        await using var api = new SyntheticClashApi
        {
            Rows = 1,
            RulesResponse = new
            {
                rules = new[]
                {
                    new { type = "Domain", payload = "pilot-two.example", proxy = "Group-0", size = 1,
                        extra = new { disabled = false, hitCount = 2, missCount = 10, hitAt = "2025-01-02T03:04:05", missAt = "2025-02-03T04:05:06" } },
                    new { type = "DomainSuffix", payload = "pilot-ten.example", proxy = "Group-1", size = 10,
                        extra = new { disabled = false, hitCount = 10, missCount = 2, hitAt = "2025-01-02T03:04:05", missAt = "2025-02-03T04:05:06" } },
                    new { type = "DomainKeyword", payload = "pilot-large.example", proxy = "Group-2", size = 12345,
                        extra = new { disabled = false, hitCount = 1234567, missCount = 987654, hitAt = "2025-01-02T03:04:05", missAt = "2025-02-03T04:05:06" } },
                    new { type = "Match", payload = "", proxy = "DIRECT", size = 0,
                        extra = new { disabled = false, hitCount = 0, missCount = 0, hitAt = "", missAt = "" } }
                }
            },
            LogResponse = tick => new
            {
                type = tick % 3 == 0 ? "error" : tick % 3 == 1 ? "info" : "warning",
                payload = tick % 3 == 0 ? "pilot-log failed to resolve upstream.example: isolated fixture timeout"
                    : tick % 3 == 1 ? "pilot-log [TCP] 192.0.2.1:10000 --> fixture.example:443 match DomainSuffix(example) using Group-0[Node-0]"
                    : "pilot-log long payload " + string.Concat(Enumerable.Repeat("upstream.example:443 / fixture-only-routing-detail / ", 12))
            }
        };
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
            ["config/language"] = language, ["config/default-theme"] = theme,
            ["config/auto-theme"] = "false", ["config/is-sidebar-collapsed"] = "true", ["config/auto-ip-check"] = "false",
            ["config/auto-connection-check"] = "false", ["config/check-upgrade-core"] = "false"
        });
        await initial.CompleteSetupAsync();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var host = new DashboardHost(root, dashboardDirectory, ephemeralPort: true, () => false);
            using var form = new MainForm(host, WebViewDataMaintenance.PlanForCurrentContent(root, dashboardDirectory: dashboardDirectory));
            form.ShowInTaskbar = false;
            // Below the production desktop minimum, exercise the same UI's narrow
            // layout in this isolated window; do not change the product minimum.
            form.MinimumSize = new System.Drawing.Size(640, 480);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-15000, -15000);
            form.Shown += async (_, _) =>
            {
                WebView2? View() => form.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<WebView2>()).SingleOrDefault();
                async Task<string> Script(string code) => View()?.CoreWebView2 is { } core ? await core.ExecuteScriptAsync(code) : "null";
                async Task CaptureScreenshot(string path, bool representative = false)
                {
                    if (!fullComparison && !representative) return;
                    await using var image = File.Create(path);
                    await View()!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
                }
                try
                {
                    async Task Until(Func<Task<bool>> condition)
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(20);
                        while (!await condition())
                        {
                            if (DateTime.UtcNow > deadline)
                            {
                                var failureDirectory = TestDirectory.ReportDirectory("core-layout");
                                await File.WriteAllTextAsync(Path.Combine(failureDirectory, $"failure-{language}.json"), await Script("JSON.stringify({route:location.hash,downloads:document.querySelector('#core-settings-backendSettings .grid-rows-4')?.outerHTML,titles:[...document.querySelectorAll('.ctrls-bar [title]')].map(e=>({tag:e.tagName,title:e.title})),controls:[...document.querySelectorAll('.ctrls-bar [role=combobox]')].map(e=>e.textContent),animations:document.getAnimations().map(a=>({state:a.playState,iterations:a.effect?.getComputedTiming().iterations,target:a.effect?.target?.className}))})"));
                                throw new TimeoutException("Core layout did not settle; see failure-* layout evidence.");
                            }
                            await Task.Delay(50);
                        }
                    }
                    await Until(async () => await Script("!!document.querySelector('#core-exe')") == "true");
                    var evidence = TestDirectory.ReportDirectory("core-layout");
                    Directory.CreateDirectory(evidence);
                    // Chinese covers sidebar geometry and desktop minimum; English covers long labels
                    // at wide/narrow widths and the intermediate expanded-sidebar boundary.
                    var cases = language == "zh-CN"
                        ? new[] { (2200, true), (2200, false), (1360, false), (1120, true), (1120, false), (640, true) }
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
                        var name = $"{language}-{theme}-{width}-{(collapsed ? "collapsed" : "expanded")}";
                        var before = await Script(Measure);
                        // Persist evidence before asserting so CI also retains the failing layout.
                        await File.WriteAllTextAsync(Path.Combine(evidence, name + "-before.json"), before);
                        await CaptureScreenshot(Path.Combine(evidence, name + "-before.png"));
                        using (var metrics = JsonDocument.Parse(before))
                        {
                            Assert.True(metrics.RootElement.GetProperty("errors").GetArrayLength() == 0, before);
                            if (language == "zh-CN" && metrics.RootElement.GetProperty("contentWidth").GetDouble() >= 640)
                                Assert.InRange(metrics.RootElement.GetProperty("pathInset").GetDouble(), 0, 110);
                        }
                        View()!.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                        {
                            protocolVersion = 2, type = "logAppend", logText = string.Join('\n', Enumerable.Range(0, 600).Select(i => $"[{i:D4}] synthetic output for scroll verification"))
                        }));
                        await Until(async () => await Script("document.querySelector('.core-output-log').scrollHeight > document.querySelector('.core-output-log').clientHeight") == "true");
                        var after = await Script(Measure);
                        await File.WriteAllTextAsync(Path.Combine(evidence, name + ".json"), after);
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
                        await CaptureScreenshot(Path.Combine(evidence, name + ".png"), representative: collapsed && width is 2200 or 640);
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
                                await Script("const i=document.querySelector('#core-switch-exe'); i.value='C:/unsaved-target.exe'; i.dispatchEvent(new Event('input',{bubbles:true})); true");
                                var original = host.BuildState(false);
                                var profiles = new Dictionary<string, CoreProfileState>(original.Profiles);
                                profiles["sing-box"] = profiles["sing-box"] with { Revision = profiles["sing-box"].Revision + 1, ExePath = "C:/remote-target.exe" };
                                View()!.CoreWebView2.PostWebMessageAsJson(HostBridgeJson.Serialize(new { protocolVersion = 2, type = "state", state = original with { Profiles = profiles } }));
                                await Until(async () => await Script("!!document.querySelector('[data-testid=reload-dialog-draft]')") == "true");
                                foreach (var confirm in new[] { false, true })
                                {
                                    await Script("document.querySelector('[data-testid=reload-dialog-draft]').click(); true");
                                    await Until(async () => await Script("!!document.querySelector('[data-testid=reload-draft-confirmation]')") == "true");
                                    Assert.Equal("true", await Script("(() => { const p=document.querySelector('[data-testid=reload-draft-confirmation]'), b=p.querySelector('button'), r=b.getBoundingClientRect(); b.focus(); return p.closest('dialog').matches(':modal') && document.activeElement===b && b.contains(document.elementFromPoint(r.left+r.width/2,r.top+r.height/2)) })()"));
                                    await Script($"document.querySelectorAll('[data-testid=reload-draft-confirmation] button')[{(confirm ? 1 : 0)}].click(); true");
                                    await Until(async () => await Script("!document.querySelector('[data-testid=reload-draft-confirmation]')") == "true");
                                    Assert.Equal(confirm ? "C:/remote-target.exe" : "C:/unsaved-target.exe", JsonSerializer.Deserialize<string>(await Script("document.querySelector('#core-switch-exe').value")));
                                }
                            }
                            await CaptureScreenshot(Path.Combine(evidence, name + "-switch.png"));
                            await Script("document.querySelector('[data-testid=switch-core-dialog] .modal-action .dashboard-action-btn').click(); true");
                            await Until(async () => await Script("!document.querySelector('[data-testid=switch-core-dialog]')") == "true");
                        }
                    }
                    form.ClientSize = new System.Drawing.Size(2200, 1000);
                    await Script("location.hash='#/core?scrollTo=connectionSettings'; true");
                    await Until(async () => await Script("!!document.querySelector('#core-settings-connectionSettings .settings-grid')") == "true");
                    await Task.Delay(500); // Finish the user-visible smooth scroll.
                    Assert.Equal("true", await Script("(() => { const r=document.querySelector('#core-settings-connectionSettings').getBoundingClientRect(); return r.top>=document.querySelector('.core-toolbar').getBoundingClientRect().bottom && r.top<innerHeight })()"));
                    // Regression: opening a target editor used to remove the background
                    // profile/log block and clamp scrollTop from 240 to zero.
                    Assert.Equal("240", await Script("(() => { const page=document.querySelector('.core-content').parentElement; page.scrollTop=240; window.__uiBeforeSwitch={panel:document.querySelector('.core-layout'),value:document.querySelector('#core-exe').value}; return page.scrollTop })()"));
                    await Script("document.querySelector('.core-top-button.btn-primary').click(); true");
                    await Until(async () => await Script("!!document.querySelector('[data-testid=switch-core-dialog]')") == "true");
                    Assert.Equal("true", await Script("(() => { const ids=[...document.querySelectorAll('[id]')].map(e=>e.id); return document.querySelector('.core-layout')===window.__uiBeforeSwitch.panel && document.querySelector('#core-exe').value===window.__uiBeforeSwitch.value && new Set(ids).size===ids.length && document.querySelector('.core-content').parentElement.scrollTop===240 })()"));
                    await Script("document.querySelector('[data-testid=switch-core-dialog] .modal-action .dashboard-action-btn').click(); true");
                    await Until(async () => await Script("!document.querySelector('[data-testid=switch-core-dialog]') && document.activeElement===document.querySelector('.core-top-button.btn-primary')") == "true");
                    Assert.Equal("240", await Script("document.querySelector('.core-content').parentElement.scrollTop"));
                    await File.WriteAllTextAsync(Path.Combine(evidence, $"switch-scroll-{language}.json"), "{\"before\":240,\"after\":240,\"backgroundRetained\":true,\"focusReturned\":true}");

                    // Shared SelectInput remains upstream-owned; verify the desktop
                    // consumer's size on all three affected toolbars, not CSS strings.
                    form.ClientSize = new System.Drawing.Size(1360, 1000);
                    foreach (var route in new[] { "proxies", "connections", "logs" })
                    {
                        await Script($"window.__previousToolbar=document.querySelector('.ctrls-bar'); location.hash='#/{route}'; true");
                        // The previous page can still have a combobox during out-in
                        // transitions. Require its DOM to leave and the new page to settle.
                        await Until(async () => await Script("!window.__previousToolbar?.isConnected && !!document.querySelector('.ctrls-bar [role=combobox]') && !document.getAnimations().some(a=>a.playState==='running' && a.effect?.getComputedTiming().iterations!==Infinity)") == "true");
                        var controls = await Script("JSON.stringify([...document.querySelectorAll('.ctrls-bar [role=combobox]')].map(e=>({height:e.getBoundingClientRect().height,radius:parseFloat(getComputedStyle(e).borderRadius)})))");
                        var json = JsonSerializer.Deserialize<string>(controls)!;
                        await File.WriteAllTextAsync(Path.Combine(evidence, $"toolbar-{route}-{language}.json"), json);
                        await CaptureScreenshot(Path.Combine(evidence, $"toolbar-{route}-{language}.png"));
                        using var measured = JsonDocument.Parse(json);
                        Assert.Equal(route == "logs" ? 2 : 1, measured.RootElement.GetArrayLength());
                        foreach (var control in measured.RootElement.EnumerateArray())
                        {
                            Assert.InRange(control.GetProperty("height").GetDouble(), 35.5, 36.5);
                            Assert.InRange(control.GetProperty("radius").GetDouble(), 7.5, 8.5);
                        }
                    }
                    // Visiting the real proxies page must not bypass the stopped-session gate.
                    Assert.Equal(0, api.Requests.GetValueOrDefault("/proxies"));
                    Assert.Equal(0, api.Requests.GetValueOrDefault("/providers/proxies"));
                    // Verify the solid toolbar variant against the built CSS, including its cascade.
                    Assert.Equal("true", await Script("(() => { const bar=document.querySelector('.ctrls-bar'); const plain=getComputedStyle(bar).borderRadius; bar.classList.add('ctrls-bar-solid'); try { const solid=getComputedStyle(bar); return parseFloat(plain)===0 && parseFloat(solid.borderRadius)>0 && solid.boxShadow!=='none'; } finally { bar.classList.remove('ctrls-bar-solid'); } })()"));
                    // Render error/unknown/read-only states through the real API and
                    // v2 session owner, using only our ephemeral synthetic service.
                    await Script("location.hash='#/core'; true");
                    await Until(async () => await Script("!!document.querySelector('.core-content')") == "true");
                    long epoch = 100;
                    void SendBackendState(bool? readOnlyTun = null, string apiStatus = "ready")
                    {
                        var state = host.BuildState(false);
                        var kind = readOnlyTun.HasValue ? CoreKind.SingBox : CoreKind.Mihomo;
                        var runtime = state.Runtime with
                        {
                            CoreType = kind, CoreTitle = AppSettings.CoreTitleFor(kind),
                            IsRunning = true, ProcessId = 12345, ApiStatus = apiStatus, RuntimeEpoch = epoch++,
                            ApiUrl = api.Origin, ReadOnlyTunEnabled = readOnlyTun
                        };
                        View()!.CoreWebView2.PostWebMessageAsJson(HostBridgeJson.Serialize(new { protocolVersion = 2, type = "state", state = state with { Runtime = runtime } }));
                    }
                    async Task RecordBackendState(string name)
                    {
                        await Until(async () => await Script("!document.getAnimations().some(a=>a.playState==='running' && a.effect?.getComputedTiming().iterations!==Infinity)") == "true");
                        var json = await Script("(() => { const root=document.querySelector('#core-settings-backendSettings'); return {text:root.innerText,controls:[...root.querySelectorAll('input[type=checkbox]')].map(e=>({label:e.getAttribute('aria-label'),checked:e.checked,disabled:e.disabled}))} })()");
                        await File.WriteAllTextAsync(Path.Combine(evidence, $"backend-{name}-{language}.json"), json);
                        await CaptureScreenshot(Path.Combine(evidence, $"backend-{name}-{language}.png"));
                    }
                    api.ConfigStatusCode = 404; // permanent read failure; no background retry races
                    api.ConfigResponse = new { message = "fixture API denied <img src=x>" };
                    SendBackendState();
                    await Until(async () => await Script("!!document.querySelector('#core-settings-backendSettings [role=alert]')") == "true");
                    Assert.Equal("true", await Script("document.querySelectorAll('#core-settings-backendSettings input[type=checkbox]').length===0 && !document.querySelector('#core-settings-backendSettings [role=alert] img') && document.querySelector('#core-settings-backendSettings [role=alert]').textContent.includes('fixture API denied <img src=x>')"));
                    await RecordBackendState("error");
                    var readsBeforeRetry = api.Requests.GetValueOrDefault("/configs");
                    api.ConfigStatusCode = 200;
                    api.ConfigResponse = null;
                    await Script("document.querySelector('#core-settings-backendSettings [role=alert] button').click(); true");
                    await Until(async () => await Script("document.querySelectorAll('#core-settings-backendSettings input[type=checkbox]').length===2") == "true");
                    Assert.True(api.Requests.GetValueOrDefault("/configs") > readsBeforeRetry);
                    Assert.Equal("true", await Script("[...document.querySelectorAll('#core-settings-backendSettings input[type=checkbox]')].every(e=>!e.checked&&!e.disabled)"));
                    await RecordBackendState("recovered");
                    if (language == "en-US")
                    {
                        // Feed real connection frames through each core's session; measure the
                        // speed and text boxes, since a clipped ancestor can hide overflow.
                        var downloadLayouts = new List<string>();
                        foreach (var kind in Enum.GetValues<CoreKind>())
                        {
                            var coreName = kind == CoreKind.SingBox ? "sing-box" : "mihomo";
                            var longName = string.Concat(Enumerable.Repeat("long-routing-group-", 20));
                            api.ConnectionsResponse = tick => Enumerable.Range(0, 4).Select(i => (object)new
                            {
                                id = $"download-{coreName}-{i}", upload = 0L, download = (long)tick * (i + 1) * 8 * 1024 * 1024,
                                start = "2026-09-01T00:00:00Z",
                                chains = new[] { coreName + "-node-" + longName, "selector-" + longName, "fallback-" + longName },
                                rule = "DomainSuffix", rulePayload = longName + ".example",
                                metadata = new { network = "tcp", type = coreName + "-" + longName,
                                    sourceIP = "192.0.2.1", sourcePort = "10000", destinationIP = "198.51.100.1",
                                    destinationPort = "443", host = coreName + "-" + longName + ".example", process = "fixture.exe" }
                            }).ToArray();
                            SendBackendState(kind == CoreKind.SingBox ? false : null);
                            await Until(async () => await Script("(() => { const card=document.querySelector('#core-settings-backendSettings .grid-rows-4'); return card?.children.length===4 && [...card.children].every(row=>row.innerText.includes(" + JsonSerializer.Serialize(coreName + "-") + ") && row.lastElementChild.textContent.includes('MB')); })()") == "true");
                            foreach (var width in new[] { 640, 1500 })
                            {
                                form.Size = new System.Drawing.Size(width, 1000);
                                await Until(async () => await Script($"innerWidth==={width}") == "true");
                                await Script("document.querySelector('#core-settings-backendSettings .grid-rows-4').scrollIntoView({block:'center'}); new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))");
                                var measurement = JsonSerializer.Deserialize<string>(await Script("""
                                    (() => {
                                      const card=document.querySelector('#core-settings-backendSettings .grid-rows-4');
                                      const box=e=>{const r=e.getBoundingClientRect();return {left:r.left,right:r.right,width:r.width,client:e.clientWidth,scroll:e.scrollWidth}};
                                      const rows=[...card.children].map(row=>{
                                        const speed=row.lastElementChild, text=row.children[1], style=getComputedStyle(row);
                                        const spans=[...text.querySelectorAll('span')].filter(e=>e.textContent!=='->');
                                        const r=box(row), s=box(speed), c=box(card);
                                        return {row:r,speed:s,text:box(text),paddingLeft:parseFloat(style.paddingLeft),paddingRight:parseFloat(style.paddingRight),
                                          fields:spans.map(e=>({text:e.textContent.trim(),...box(e),overflow:getComputedStyle(e).overflowX,textOverflow:getComputedStyle(e).textOverflow})),
                                          bounded:r.left>=c.left-1 && r.right<=c.right+1 && row.scrollWidth<=row.clientWidth+1 && s.width>0 && s.left>=r.left+parseFloat(style.paddingLeft)-1 && s.right<=r.right-parseFloat(style.paddingRight)+1};
                                      });
                                      return JSON.stringify({viewport:innerWidth,card:box(card),rows});
                                    })()
                                    """))!;
                                await File.WriteAllTextAsync(Path.Combine(evidence, $"downloads-{coreName}-{width}.json"), measurement);
                                await CaptureScreenshot(Path.Combine(evidence, $"downloads-{coreName}-{width}.png"), representative: true);
                                downloadLayouts.Add(measurement);
                            }
                        }
                        api.ConnectionsResponse = null;
                        form.ClientSize = new System.Drawing.Size(1360, 1000);
                        foreach (var measurement in downloadLayouts)
                        {
                            using var layout = JsonDocument.Parse(measurement);
                            foreach (var row in layout.RootElement.GetProperty("rows").EnumerateArray())
                            {
                                Assert.True(row.GetProperty("bounded").GetBoolean(), "Current downloads overflow: " + measurement);
                                foreach (var field in row.GetProperty("fields").EnumerateArray())
                                {
                                    Assert.True(field.GetProperty("width").GetDouble() > 0, "Download text disappeared: " + measurement);
                                    Assert.True(field.GetProperty("scroll").GetDouble() > field.GetProperty("client").GetDouble(), "Long download text was not constrained: " + measurement);
                                    Assert.Equal("ellipsis", field.GetProperty("textOverflow").GetString());
                                }
                            }
                        }
                    }
                    api.ConfigResponse = new { tun = new { } };
                    SendBackendState();
                    var unknownLabel = language == "zh-CN" ? "状态未知" : "Unknown state";
                    await Until(async () => await Script("document.querySelector('#core-settings-backendSettings').textContent.includes(" + JsonSerializer.Serialize(unknownLabel) + ")") == "true");
                    Assert.Equal("0", await Script("document.querySelectorAll('#core-settings-backendSettings input[type=checkbox]').length"));
                    await RecordBackendState("unknown");
                    foreach (var enabled in new[] { false, true })
                    {
                        api.ConfigStatusCode = 404;
                        api.ConfigResponse = new { message = "fixture read-only API failure" };
                        SendBackendState(enabled);
                        var expected = enabled ? "true" : "false";
                        await Until(async () => await Script("(() => { const root=document.querySelector('#core-settings-backendSettings'), inputs=root.querySelectorAll('input[type=checkbox]'); return inputs.length===1 && inputs[0].disabled && inputs[0].checked===" + expected + " && !!root.querySelector('[role=alert]') })()") == "true");
                        await RecordBackendState("read-only-" + expected);
                    }
                    foreach (var status in new[] { "unauthorized", "unreachable" })
                    {
                        SendBackendState(apiStatus: status);
                        var message = (status, language) switch
                        {
                            ("unauthorized", "zh-CN") => "鉴权失败", ("unreachable", "zh-CN") => "无法连接",
                            ("unauthorized", _) => "Authentication failed", _ => "Unreachable"
                        };
                        await Until(async () => await Script("document.querySelector('#core-settings-backendSettings [role=alert]')?.textContent.includes(" + JsonSerializer.Serialize(message) + ")") == "true");
                        await RecordBackendState(status);
                    }
                    // Sorting/search semantics live in frontend tests; exercise browser geometry and host saves once.
                    if (fullComparison || language == "en-US")
                    {
                        api.ConfigStatusCode = 200;
                        api.ConfigResponse = null;
                        // Earlier cases leave unsaved drafts; reload so the host can acknowledge preference saves.
                        await Script("window.__pilotReloadPending=true; true");
                        View()!.CoreWebView2.Reload();
                        await Until(async () => await Script("!window.__pilotReloadPending && !!document.querySelector('#core-exe')") == "true");
                        await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled", "{\"enabled\":true}");
                        var pilotEvidence = Path.Combine(TestDirectory.ReportDirectory("upstream-ui"), baselineEvidence ? "d0" : "d1");
                        Directory.CreateDirectory(pilotEvidence);
                        await Script("""
                            (() => {
                              for (const [key,value] of Object.entries({
                                'config/is-sidebar-collapsed':'true',
                                'config/connection-display-style':'table',
                                'config/connection-table-columns':'["host","type"]',
                                'config/search-hidden-columns':'false',
                                'config/rule-display-style':'table',
                                'config/log-display-style':'table',
                                'config/logs-table-sorting':'[]',
                                'config/rules-table-sorting':'[]',
                                'config/overview-card-order':'[{"card":"RuleHitCountCard","visible":true}]'
                              })) {
                                const oldValue=localStorage.getItem(key);
                                localStorage.setItem(key,value);
                                window.dispatchEvent(new StorageEvent('storage',{key,oldValue,newValue:value,storageArea:localStorage}));
                              }
                              return true;
                            })()
                            """);
                        async Task RestorePilotSessionAfterPreferenceSave()
                        {
                            // Settings events and replies carry the stopped host runtime; restore only after acknowledgement.
                            Assert.True(await form.FlushPreferencesAsync());
                            SendBackendState();
                        }
                        await RestorePilotSessionAfterPreferenceSave();
                        await Until(async () => await Script("document.querySelectorAll('#core-settings-backendSettings input[type=checkbox]').length===2") == "true");
                        async Task<string> RecordPilot(string name, string measurement = PilotTableMeasure, bool representative = false)
                        {
                            await Until(async () => await Script("!document.getAnimations().some(a=>a.playState==='running' && a.effect?.getComputedTiming().iterations!==Infinity)") == "true");
                            var json = await Script(measurement);
                            var file = Path.Combine(pilotEvidence, $"{language}-{theme}-{name}");
                            await File.WriteAllTextAsync(file + ".json", json);
                            await CaptureScreenshot(file + ".png", representative);
                            return json;
                        }
                        async Task Pointer(string selector, bool click = true)
                        {
                            await Until(async () => await Script($$"""
                                (() => { const e=document.querySelector({{JsonSerializer.Serialize(selector)}}); if (!e) return false;
                                  const r=e.getBoundingClientRect();
                                  return r.width>0 && r.height>0 && e.contains(document.elementFromPoint(r.left+r.width/2,r.top+r.height/2))
                                    && !document.getAnimations().some(a=>a.playState==='running' && a.effect?.getComputedTiming().iterations!==Infinity); })()
                                """) == "true");
                            using var point = JsonDocument.Parse(await Script($$"""
                                (() => { const e=document.querySelector({{JsonSerializer.Serialize(selector)}}), r=e.getBoundingClientRect();
                                  const x=r.left+r.width/2,y=r.top+r.height/2;
                                  return {x,y,reachable:e.contains(document.elementFromPoint(x,y))}; })()
                                """));
                            Assert.True(point.RootElement.GetProperty("reachable").GetBoolean(), point.RootElement.ToString());
                            var x = point.RootElement.GetProperty("x").GetDouble();
                            var y = point.RootElement.GetProperty("y").GetDouble();
                            await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type = "mouseMoved", x, y }));
                            if (!click) return;
                            foreach (var type in new[] { "mousePressed", "mouseReleased" })
                                await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type, x, y, button = "left", clickCount = 1 }));
                        }
                        async Task SetConnectionSearch(string value)
                        {
                            await Script($$"""
                                (() => { const input=document.querySelector('.ctrls-bar input[placeholder*="Regex"]');
                                  input.value={{JsonSerializer.Serialize(value)}}; input.dispatchEvent(new Event('input',{bubbles:true})); return true; })()
                                """);
                        }
                        // Observe native canvas painting before Overview mounts, without first populating rules on /rules.
                        await Script("""
                            (() => {
                              const paint=window.__ruleChartPaint={labels:[],fills:0,colors:[],errors:[]};
                              const canvas=CanvasRenderingContext2D.prototype, gradient=CanvasGradient.prototype;
                              const fillText=canvas.fillText, fill=canvas.fill, addColorStop=gradient.addColorStop;
                              canvas.fillText=function(...args) { const result=fillText.apply(this,args); paint.labels.push(String(args[0])); return result; };
                              canvas.fill=function(...args) { const result=fill.apply(this,args); paint.fills++; return result; };
                              gradient.addColorStop=function(...args) {
                                paint.colors.push(args[1]);
                                try { return addColorStop.apply(this,args); }
                                catch(error) { paint.errors.push(String(error)); throw error; }
                              };
                              window.addEventListener('error',event=>paint.errors.push(event.message));
                              window.addEventListener('unhandledrejection',event=>paint.errors.push(String(event.reason)));
                              return true;
                            })()
                            """);
                        form.ClientSize = new System.Drawing.Size(1500, 1000);
                        await Script("location.hash='#/overview'; true");
                        const string ruleChartMeasure = """
                            (() => ({...window.__ruleChartPaint,route:location.hash,text:document.querySelector('.home-page')?.innerText,
                              canvases:[...document.querySelectorAll('canvas')].map(canvas=>({width:canvas.width,height:canvas.height,
                                visibleWidth:canvas.getBoundingClientRect().width,visibleHeight:canvas.getBoundingClientRect().height}))}))()
                            """;
                        string chartJson;
                        try
                        {
                            await Until(async () => await Script("['2','10','1234567'].every(value=>window.__ruleChartPaint.labels.includes(value))") == "true");
                        }
                        finally
                        {
                            await Script($"window.__ruleChartPaint.rulesRequests={api.Requests.GetValueOrDefault("/rules")}; true");
                            chartJson = await RecordPilot("overview-rule-hit-first-visit", ruleChartMeasure, representative: true);
                        }
                        using (var chart = JsonDocument.Parse(chartJson))
                        {
                            Assert.True(api.Requests.GetValueOrDefault("/rules") > 0, chartJson);
                            Assert.True(chart.RootElement.GetProperty("errors").GetArrayLength() == 0, chartJson);
                            Assert.True(chart.RootElement.GetProperty("fills").GetInt32() > 0, chartJson);
                            var canvas = Assert.Single(chart.RootElement.GetProperty("canvases").EnumerateArray());
                            foreach (var dimension in new[] { "width", "height", "visibleWidth", "visibleHeight" })
                                Assert.True(canvas.GetProperty(dimension).GetDouble() > 0, chartJson);
                        }
                        // Both widths retain real fixed-column and clipping checks, including narrow overflow.
                        await Script("location.hash='#/logs'; true");
                        await Until(async () => await Script("[...document.querySelectorAll('table tbody tr')].filter(r=>r.textContent.includes('pilot-log')).length>=3 && document.querySelector('table').textContent.includes('long payload') && document.querySelector('table').textContent.includes('failed to resolve')") == "true");
                        foreach (var width in new[] { 1500, 640 })
                        {
                            form.ClientSize = new System.Drawing.Size(width, 1000);
                            await Until(async () => await Script(width == 1500 ? "innerWidth>1400" : "innerWidth<700") == "true");
                            var json = await RecordPilot($"logs-{width}-populated", representative: width == 640);
                            using var metrics = JsonDocument.Parse(json);
                            Assert.Equal(4, metrics.RootElement.GetProperty("headers").GetArrayLength());
                            Assert.True(metrics.RootElement.GetProperty("rows").GetArrayLength() >= 3, json);
                            Assert.All(metrics.RootElement.GetProperty("rows").EnumerateArray(), row =>
                                Assert.Matches(@"\d{2}:\d{2}:\d{2}", row.GetProperty("cells")[1].GetProperty("text").GetString()!));
                            if (baselineEvidence) continue;
                            Assert.Equal("fixed", metrics.RootElement.GetProperty("tableLayout").GetString());
                            var headers = metrics.RootElement.GetProperty("headers").EnumerateArray().ToArray();
                            Assert.InRange(headers[0].GetProperty("rect").GetProperty("width").GetDouble(), 63, 65);
                            Assert.InRange(headers[1].GetProperty("rect").GetProperty("width").GetDouble(), 159, 161);
                            Assert.InRange(headers[2].GetProperty("rect").GetProperty("width").GetDouble(), 95, 97);
                            Assert.True(headers[3].GetProperty("rect").GetProperty("width").GetDouble() > 200, json);
                            Assert.True(metrics.RootElement.GetProperty("rows").EnumerateArray().Any(r => r.GetProperty("cells")[3].GetProperty("clipped").GetBoolean()), json);
                        }
                        form.ClientSize = new System.Drawing.Size(1500, 1000);
                        await Script("location.hash='#/rules'; true");
                        await Until(async () => await Script("[...document.querySelectorAll('table tbody tr')].filter(r=>r.textContent.includes('pilot-')).length===3") == "true");
                        var hitLabel = language == "zh-CN" ? "命中" : "Hits";
                        var missLabel = language == "zh-CN" ? "未命中" : "Misses";
                        var ruleMetrics = await RecordPilot("rules-1500-populated");
                        using (var metrics = JsonDocument.Parse(ruleMetrics))
                        {
                            var headers = metrics.RootElement.GetProperty("headers").EnumerateArray().Select(h => h.GetProperty("text").GetString()).ToArray();
                            Assert.Equal(1, headers.Count(h => h == hitLabel));
                            Assert.Equal(1, headers.Count(h => h == missLabel));
                            Assert.True(Array.IndexOf(headers, hitLabel) < Array.IndexOf(headers, missLabel), ruleMetrics);
                        }
                        Assert.Equal("true", await Script("[...document.querySelectorAll('table tbody tr')].filter(r=>r.textContent.includes('pilot-')).every(r=>r.cells[5].textContent.includes('01-02 03:04') && r.cells[5].querySelector('.text-xs')?.getBoundingClientRect().width>0)"));
                        if (!baselineEvidence)
                        {
                            await Script("[...document.querySelectorAll('table tbody tr')].find(r=>r.textContent.includes('pilot-large.example')).cells[5].firstElementChild.setAttribute('data-pilot-hit',''); true");
                            await Pointer("[data-pilot-hit]", click: false);
                            await Until(async () => await Script("[...document.querySelectorAll('[role=tooltip]')].some(e=>getComputedStyle(e).visibility==='visible' && e.textContent.includes('2025-01-02 03:04:05'))") == "true");
                            await RecordPilot("rules-1500-numeric-tooltip", representative: true);
                            Assert.Equal("true", await Script("(() => { const td=document.querySelector('[data-pilot-hit]').closest('td'), tip=[...document.querySelectorAll('[role=tooltip]')].find(e=>getComputedStyle(e).visibility==='visible'); return !td.hasAttribute('title') && tip.getBoundingClientRect().width>0; })()"));
                            await Pointer(".ctrls-bar", click: false);
                        }
                        if (fullComparison)
                        {
                            form.ClientSize = new System.Drawing.Size(640, 1000);
                            await Until(async () => await Script("innerWidth<700") == "true");
                            await RecordPilot("rules-640-populated");
                        }

                        await Script("location.hash='#/connections'; true");
                        await Until(async () => await Script("!!document.querySelector('.ctrls-bar input[placeholder*=Regex]') && !!document.querySelector('table tbody') && document.querySelector('table tbody').textContent.includes('fixture.example')") == "true");
                        foreach (var width in new[] { 1500, 640 })
                        {
                            form.ClientSize = new System.Drawing.Size(width, 1000);
                            await Until(async () => await Script(width == 1500 ? "innerWidth>1400 && !!document.querySelector('.ctrls-search')" : "innerWidth<700 && !document.querySelector('.ctrls-search')") == "true");
                            var searchMetrics = await RecordPilot($"connections-{width}-visible-scope", PilotSearchMeasure);
                            if (baselineEvidence) continue;
                            using (var metrics = JsonDocument.Parse(searchMetrics))
                                Assert.True(metrics.RootElement.GetProperty("errors").GetArrayLength() == 0, searchMetrics);
                            // Narrow geometry still hit-tests the funnel; repeat its save/remount sequence only for comparison.
                            if (width == 640 && !fullComparison) continue;
                            Assert.Equal("false", JsonSerializer.Deserialize<string>(await Script("document.querySelector('.ctrls-bar button[aria-pressed]').getAttribute('aria-pressed')")));
                            var visibleLabel = await Script("document.querySelector('.ctrls-bar button[aria-pressed]').getAttribute('aria-label')");
                            await SetConnectionSearch("fixture.exe"); // Process is deliberately absent from visible columns.
                            await Until(async () => await Script("!document.querySelector('table tbody').textContent.includes('fixture.example')") == "true");
                            if (fullComparison) await RecordPilot($"connections-{width}-hidden-process-excluded", PilotSearchMeasure);
                            await Pointer(".ctrls-bar button[aria-pressed]");
                            await Until(async () => await Script("document.querySelector('.ctrls-bar button[aria-pressed]').getAttribute('aria-pressed')==='true' && localStorage.getItem('config/search-hidden-columns')==='true'") == "true");
                            Assert.NotEqual(visibleLabel, await Script("document.querySelector('.ctrls-bar button[aria-pressed]').getAttribute('aria-label')"));
                            await RestorePilotSessionAfterPreferenceSave();
                            await Until(async () => await Script("document.querySelector('table tbody').textContent.includes('fixture.example')") == "true");
                            Assert.Equal("true", host.Preferences["config/search-hidden-columns"]);
                            var enabledMetrics = await RecordPilot($"connections-{width}-hidden-process-included", PilotSearchMeasure, representative: width == 1500);
                            using (var metrics = JsonDocument.Parse(enabledMetrics))
                                Assert.True(metrics.RootElement.GetProperty("errors").GetArrayLength() == 0, enabledMetrics);
                            // A route remount must retain both the button state and the chosen scope.
                            await Script("location.hash='#/logs'; true");
                            await Until(async () => await Script("document.querySelector('table')?.textContent.includes('pilot-log')") == "true");
                            await Script("location.hash='#/connections'; true");
                            await Until(async () => await Script("document.querySelector('.ctrls-bar button[aria-pressed]')?.getAttribute('aria-pressed')==='true' && document.querySelector('table tbody')?.textContent.includes('fixture.example')") == "true");
                            await Pointer(".ctrls-bar button[aria-pressed]");
                            await Until(async () => await Script("document.querySelector('.ctrls-bar button[aria-pressed]').getAttribute('aria-pressed')==='false' && localStorage.getItem('config/search-hidden-columns')==='false'") == "true");
                            await RestorePilotSessionAfterPreferenceSave();
                            await Until(async () => await Script("!document.querySelector('table tbody').textContent.includes('fixture.example')") == "true");
                            Assert.Equal("false", host.Preferences["config/search-hidden-columns"]);
                            await SetConnectionSearch("");
                            await Until(async () => await Script("document.querySelector('table tbody').textContent.includes('fixture.example')") == "true");
                        }
                        if (!baselineEvidence)
                        {
                            form.ClientSize = new System.Drawing.Size(1500, 1000);
                            await Until(async () => await Script("innerWidth>1400") == "true");
                            await Pointer(".ctrls-bar button[role=combobox]");
                            await Until(async () => await Script("!!document.querySelector('[role=listbox] [role=option]')") == "true");
                            var stackMetrics = await RecordPilot("connections-1500-source-popover", PilotStackMeasure, representative: true);
                            using (var metrics = JsonDocument.Parse(stackMetrics))
                                Assert.True(metrics.RootElement.GetProperty("aboveHeader").GetBoolean(), stackMetrics);
                            await Pointer(".ctrls-bar button[role=combobox]");
                            await Until(async () => await Script("!document.querySelector('[role=listbox]')") == "true");
                        }
                    }
                    Assert.Empty(api.Errors);
                    Assert.False(host.IsRunning);
                    await host.ShutdownAsync();
                    await TestBrowser.DisposeAsync(form);
                    completion.TrySetResult();
                }
                catch (Exception error)
                {
                    try
                    {
                        var failureDirectory = TestDirectory.ReportDirectory("core-layout");
                        await File.WriteAllTextAsync(Path.Combine(failureDirectory, $"failure-{language}.txt"), error.ToString());
                        if (View()?.CoreWebView2 is not null)
                            await CaptureScreenshot(Path.Combine(failureDirectory, $"failure-{language}.png"), representative: true);
                    }
                    catch { /* Keep the test failure if WebView diagnostics are unavailable. */ }
                    completion.TrySetException(error);
                }
                finally
                {
                    try { await host.ShutdownAsync(); await TestBrowser.DisposeAsync(form); }
                    catch (Exception error) { completion.TrySetException(error); }
                    finally { form.CloseForApplicationExit(); }
                }
            };
            using var timer = new System.Windows.Forms.Timer { Interval = 120000 };
            timer.Tick += (_, _) => { completion.TrySetException(new TimeoutException("Core layout test deadline expired.")); form.CloseForApplicationExit(); };
            timer.Start();
            Application.Run(form);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(130)); }
        finally { Assert.True(thread.Join(TimeSpan.FromSeconds(30))); }
    }

    private const string PilotTableMeasure = """
        (() => {
          const rect=e=>{const r=e.getBoundingClientRect(); return {left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height};};
          const table=document.querySelector('table');
          return {
            route:location.hash,viewport:{width:innerWidth,height:innerHeight},theme:document.documentElement.getAttribute('data-theme'),
            tableLayout:getComputedStyle(table).tableLayout,table:rect(table),
            scroll:rect(table.closest('.overflow-auto')),
            headers:[...table.querySelectorAll('th')].map(e=>({text:e.textContent.trim(),rect:rect(e)})),
            rows:[...table.querySelectorAll('tbody tr')].filter(r=>r.cells.length>1).map(r=>({rect:rect(r),cells:[...r.cells].map(e=>({
              text:e.textContent.trim(),title:e.getAttribute('title'),rect:rect(e),clipped:e.scrollWidth>e.clientWidth,
              textAlign:getComputedStyle(e).textAlign
            }))})),
            tooltips:[...document.querySelectorAll('[role=tooltip]')].map(e=>({text:e.textContent,rect:rect(e),visible:getComputedStyle(e).visibility==='visible'}))
          };
        })()
        """;

    private const string PilotSearchMeasure = """
        (() => {
          const rect=e=>{const r=e.getBoundingClientRect(); return {left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height};};
          const input=document.querySelector('.ctrls-bar input[placeholder*="Regex"]'),label=input.closest('label'),wrapper=label.parentElement;
          const button=wrapper.querySelector('button[aria-pressed]'), errors=[];
          let funnel=null;
          if (!button) errors.push('Search wrapper has no scope funnel');
          else {
            const b=rect(button),w=rect(wrapper),i=rect(input),style=getComputedStyle(button);
            const hit=document.elementFromPoint(b.left+b.width/2,b.top+b.height/2);
            if (b.width<20 || b.height<20 || style.visibility!=='visible' || style.display==='none' || Number(style.opacity)===0) errors.push('Funnel is not visible');
            if (b.left<w.left-1 || b.right>w.right+1 || b.top<w.top-1 || b.bottom>w.bottom+1) errors.push('Funnel escapes search wrapper');
            if (b.left<0 || b.right>innerWidth || b.top<0 || b.bottom>innerHeight) errors.push('Funnel escapes viewport');
            if (b.right>i.left+1) errors.push('Funnel overlaps input text');
            if (!button.contains(hit)) errors.push('Funnel is covered at its click point');
            funnel={rect:b,label:button.getAttribute('aria-label'),pressed:button.getAttribute('aria-pressed'),hit:hit?.tagName};
          }
          return {route:location.hash,viewport:{width:innerWidth,height:innerHeight},errors,funnel,wrapper:rect(wrapper),input:rect(input),
            preference:localStorage.getItem('config/search-hidden-columns'),search:input.value,
            tableText:document.querySelector('table').textContent};
        })()
        """;

    private const string PilotStackMeasure = """
        (() => {
          const rect=e=>{const r=e.getBoundingClientRect(); return {left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height};};
          const header=document.querySelector('table thead'),panel=document.querySelector('[role=listbox]'),h=rect(header),p=rect(panel);
          const left=Math.max(h.left,p.left),right=Math.min(h.right,p.right),top=Math.max(h.top,p.top),bottom=Math.min(h.bottom,p.bottom);
          const hit=right>left && bottom>top ? document.elementFromPoint((left+right)/2,(top+bottom)/2) : null;
          return {route:location.hash,header:h,panel:p,headerZ:getComputedStyle(header).zIndex,panelZ:getComputedStyle(panel).zIndex,
            panelText:panel.textContent,hit:hit?.outerHTML,aboveHeader:!!hit && panel.contains(hit)};
        })()
        """;

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
          // Retained visual contract; v2 density and actual-content breakpoints are also accepted.
          if (!precise(sr.height, 36)) errors.push('visual contract: status box height 36px');
          if (!precise(lr.left - pr.left, 2) || !precise(sr.left - pr.left, 30)) errors.push('retained lamp/status optical insets');
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
          if (!near(rect(save).top,rect(secret).top) || !near(rect(save).left - rect(secret).right, 8)) errors.push('save/secret inline');
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
          for (const [index, action] of actions.entries()) {
            const ar = rect(action);
            if (!precise(ar.width, 72) || !precise(ar.height, 34)) errors.push('visual contract: Core action 72x34px');
            if (index && !precise(ar.left - rect(actions[index-1]).right, 8)) errors.push('visual contract: Core action gap 8px');
          }
          if (wide && !near(tr.right, pr.right)) errors.push('toolbar/profile right edge');
          if (wide && (!near(pr.top, or.top) || !near(or.bottom, rect(profile).bottom))) errors.push('output does not align with left column');
          if (!wide && or.top < rect(profile).bottom) errors.push('narrow layout did not stack');
          for (const id of ['core-exe','core-config','core-api','core-secret']) {
            const input = document.getElementById(id), label = document.querySelector(`label[for="${id}"]`);
            const ir = rect(input), lr = rect(label);
            if (!precise(ir.height, 32)) errors.push('visual contract: field input height 32px');
            if (contentWidth >= 640 && (lr.right > ir.left || !near(lr.top+lr.height/2,ir.top+ir.height/2))) errors.push(id + ' label/input row');
            if (contentWidth < 640 && lr.bottom > ir.top) errors.push(id + ' narrow field');
            if (ir.width < 60 || ir.right > pr.right || ir.left < pr.left) errors.push(id + ' clipped input');
          }
          const style = getComputedStyle(output);
          if (style.backgroundColor === 'rgba(0, 0, 0, 0)' || parseFloat(style.borderTopLeftRadius) === 0) errors.push('output card surface');
          const windows = document.querySelector('[aria-label="最小化"]');
          if (windows && tr.right > rect(windows).left) errors.push('toolbar overlaps window controls');
          if (content.parentElement.scrollWidth > content.parentElement.clientWidth) errors.push('page horizontal overflow');
          return {errors, viewportWidth:innerWidth, contentWidth, wide, pathInset:rect(document.getElementById('core-exe')).left-pr.left, pathInputWidth:rect(document.getElementById('core-exe')).width, statusHeight:sr.height, actionSizes:actions.map(a=>({width:rect(a).width,height:rect(a).height})), toolbarLeft:tr.left, profileLeft:pr.left, backendLeft:br.left, profileWidth:pr.width, backendWidth:br.width, outputHeight:or.height, lampInset:lr.left-pr.left, statusInset:sr.left-pr.left, lampGap:sr.left-lr.right, lampCenterOffset:lr.top+lr.height/2-sr.top-sr.height/2};
        })()
        """;
}
