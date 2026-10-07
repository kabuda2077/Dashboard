using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard.Tests;

public sealed class DesktopPerformanceTests
{
    private const string Instrumentation = """
        (() => {
          const timeouts = new Set(), intervals = new Set();
          const timeout = window.setTimeout.bind(window), cancelTimeout = window.clearTimeout.bind(window);
          const interval = window.setInterval.bind(window), cancelInterval = window.clearInterval.bind(window);
          window.setTimeout = (fn, delay, ...args) => {
            const id = timeout(() => { timeouts.delete(id); typeof fn === 'function' ? fn(...args) : (0,eval)(fn); }, delay);
            timeouts.add(id); return id;
          };
          window.clearTimeout = id => { timeouts.delete(id); cancelTimeout(id); };
          window.setInterval = (...args) => { const id = interval(...args); intervals.add(id); return id; };
          window.clearInterval = id => { intervals.delete(id); cancelInterval(id); };
          let frames = [], longTasks = [], last = performance.now();
          new PerformanceObserver(list => { for (const entry of list.getEntries()) if (longTasks.length < 4096) longTasks.push(entry.duration); }).observe({type:'longtask', buffered:false});
          const frame = now => { if(frames.length < 10000) frames.push(now-last); last=now; requestAnimationFrame(frame); };
          requestAnimationFrame(frame);
          window.__measurement = {
            reset: () => { frames=[]; longTasks=[]; last=performance.now(); },
            read: () => ({ timeouts:timeouts.size, intervals:intervals.size, frames, longTasks, liveElements:document.getElementsByTagName('*').length, resources:performance.getEntriesByType('resource').length })
          };
        })();
        """;

    [Fact]
    [Trait("Category", "PerformanceIntegration")]
    public async Task ProductionWindowReportsFixedWorkloadResourcesAndLifecycles()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var samples = new List<object>();
        for (var sample = 0; sample < 3; sample++)
        {
            await using var api = new SyntheticClashApi();
            await using var fixture = await IsolatedCore.CreateAsync(CoreKind.Mihomo, apiOverride: api.Origin);
            await fixture.Store.CompleteSetupAsync();
            await fixture.Store.SavePreferencesAsync(new Dictionary<string, string> { ["config/auto-ip-check"] = "false", ["config/language"] = "en-US" });
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var index = sample;
            var thread = new Thread(() =>
            {
                var cold = Stopwatch.StartNew();
                using var host = new DashboardHost(fixture.Root, Path.Combine(repository.FullName, "resources", "dashboard"), ephemeralPort: true, () => true);
                using var form = new MainForm(host, WebViewDataMaintenance.PlanForCurrentContent(fixture.Root, dashboardDirectory: Path.Combine(repository.FullName, "resources", "dashboard")));
                form.Size = new System.Drawing.Size(1360, 840);
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-15000, -15000);
                WebView2? View() => form.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<WebView2>()).SingleOrDefault();
                async Task<string> Script(string code) => View()?.CoreWebView2 is { } core ? await core.ExecuteScriptAsync(code) : "null";
                async Task Until(Func<Task<bool>> condition, int seconds = 30)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!await condition())
                    { if(DateTime.UtcNow > deadline) throw new TimeoutException("Measurement condition timed out at " + View()?.CoreWebView2?.Source); await Task.Delay(25); }
                }
                object Memory()
                {
                    using var parent = Process.GetCurrentProcess(); parent.Refresh();
                    var children = new List<object>();
                    long working = parent.WorkingSet64, privateBytes = parent.PrivateMemorySize64;
                    foreach (var info in View()!.CoreWebView2.Environment.GetProcessInfos())
                    {
                        try
                        {
                            using var process = Process.GetProcessById(info.ProcessId); process.Refresh();
                            children.Add(new { kind = info.Kind.ToString(), workingSet = process.WorkingSet64, privateBytes = process.PrivateMemorySize64 });
                            working += process.WorkingSet64; privateBytes += process.PrivateMemorySize64;
                        }
                        catch (ArgumentException) { }
                    }
                    return new { hostWorkingSet = parent.WorkingSet64, hostPrivateBytes = parent.PrivateMemorySize64, children, sumWorkingSet = working, sumPrivateBytes = privateBytes };
                }
                async Task<object> Phase(string name, int milliseconds = 3500)
                {
                    await Script("window.__measurement.reset(); true");
                    var messages = api.Messages;
                    await Task.Delay(milliseconds);
                    using var js = JsonDocument.Parse(await Script("window.__measurement.read()"));
                    using var metrics = JsonDocument.Parse(await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Performance.getMetrics", "{}"));
                    return new { name, rows = api.Rows, memory = Memory(), browser = js.RootElement.Clone(), cdp = metrics.RootElement.Clone(), apiSockets = api.ActiveSockets, messages = api.Messages - messages };
                }
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        await host.StartCoreAsync();
                        await Until(async () => await Script("!!document.querySelector('#core-api') && performance.getEntriesByName('core-interactive').length > 0") == "true");
                        var coldMs = cold.Elapsed.TotalMilliseconds;
                        var runtime = View()!.CoreWebView2.Environment.BrowserVersionString;
                        await Until(() => Task.FromResult(host.BuildState(false).Runtime.ApiStatus == "ready"));
                        await View()!.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(Instrumentation);
                        View()!.CoreWebView2.Reload();
                        await Until(async () => await Script("!!window.__measurement && !!document.querySelector('#core-api')") == "true");
                        await View()!.CoreWebView2.CallDevToolsProtocolMethodAsync("Performance.enable", "{}");
                        var phases = new List<object> { await Phase("core-idle") };
                        var firstHeavy = Stopwatch.StartNew();
                        await Script("location.hash='#/overview'; true");
                        await Until(async () => await Script("!!document.querySelector('canvas')") == "true");
                        var heavyMs = firstHeavy.Elapsed.TotalMilliseconds;
                        phases.Add(await Phase("overview"));
                        await Script("location.hash='#/connections'; true");
                        await Until(async () => await Script("document.body.textContent.includes('fixture.example')") == "true");
                        foreach (var rows in new[] { 100, 1000, 10000 })
                        { api.Rows = rows; await Task.Delay(1500); phases.Add(await Phase("connections-" + rows)); }
                        phases.Add(await Phase("steady-10000", 20000));
                        Assert.Equal(3, api.ActiveSockets); // one connections/traffic/memory stream, not one per route
                        await Script("location.hash='#/core'; true");
                        await Until(async () => await Script("!!document.querySelector('#core-api')") == "true");
                        var revision = host.Settings.PreferencesRevision;
                        await Script("for(let i=0;i<100;i++) localStorage.setItem('config/performance-burst', String(i)); true");
                        Assert.True(await form.FlushPreferencesAsync());
                        var preferenceWrites = host.Settings.PreferencesRevision - revision;
                        Assert.Equal(1, preferenceWrites);
                        var warm = new List<double>();
                        for (var repeat = 0; repeat < 5; repeat++)
                        {
                            var view = View(); var watch = Stopwatch.StartNew();
                            form.Close(); form.ShowFromTray();
                            await Until(async () => form.Visible && form.WindowState != FormWindowState.Minimized
                                && !form.IsTrayTransitionInProgress && await Script("!!document.querySelector('#core-api')") == "true");
                            Assert.Same(view, View());
                            Assert.True(form.Visible);
                            warm.Add(watch.Elapsed.TotalMilliseconds);
                        }
                        phases.Add(await Phase("after-hot-restores"));
                        Assert.True(await form.FlushPreferencesAsync());
                        typeof(MainForm).GetMethod("DisposeDashboardView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, null);
                        var recreate = Stopwatch.StartNew();
                        await (Task)typeof(MainForm).GetMethod("EnsureDashboardInitializedAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, new object[] { "performance-cold-restore" })!;
                        await Until(async () => await Script("!!document.querySelector('#core-api')") == "true");
                        var coldRestoreMs = recreate.Elapsed.TotalMilliseconds;
                        Assert.Equal("99", host.Preferences["config/performance-burst"]);
                        Assert.Empty(api.Errors);
                        samples.Add(new { sample = index, runtime, coldWindowMs = coldMs, firstHeavyPageMs = heavyMs, warmRestoreMs = warm, coldRestoreMs, preferenceWrites, phases, requests = api.Requests.ToDictionary(pair => pair.Key, pair => pair.Value) });
                        await host.ShutdownAsync();
                        await TestBrowser.DisposeAsync(form);
                        done.TrySetResult();
                    }
                    catch (Exception error) { done.TrySetException(error); await host.ShutdownAsync(); }
                    finally
                    {
                        try { await host.ShutdownAsync(); await TestBrowser.DisposeAsync(form); }
                        catch (Exception error) { done.TrySetException(error); }
                        finally { form.CloseForApplicationExit(); }
                    }
                };
                using var timer = new System.Windows.Forms.Timer { Interval = 150000 };
                timer.Tick += (_, _) => { done.TrySetException(new TimeoutException("Measurement watchdog.")); form.CloseForApplicationExit(); };
                timer.Start(); Application.Run(form);
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            try { await done.Task.WaitAsync(TimeSpan.FromSeconds(160)); }
            finally { Assert.True(thread.Join(TimeSpan.FromSeconds(30))); }
        }
        var output = Environment.GetEnvironmentVariable("DASHBOARD_PERFORMANCE_REPORT") ?? Path.Combine(TestDirectory.ReportDirectory("performance-v2"), "desktop.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new {
            measuredAt = DateTimeOffset.UtcNow, configuration = typeof(MainForm).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "unknown", framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            hostAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(typeof(MainForm).Assembly.Location))),
            uiManifestSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(repository.FullName, "resources", "dashboard", ".vite", "manifest.json")))),
            scope = "Production host/MainForm in a fresh testhost process, real WebView, isolated real mihomo with deterministic synthetic API data. Host totals include test harness. Working-set sums may double-count shared pages. 25ms polling quantizes readiness; offscreen rendering is not physical display FPS. Cold recreate invokes production disposal directly; the 60-second timer is tested separately.",
            samples
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
