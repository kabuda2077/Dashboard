using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard.Tests;

public sealed class DesktopDataIntegrationTests
{
    [Fact]
    [Trait("Category", "WebViewCoreIntegration")]
    public async Task UploadedImageAndTwoHistoryScopesSurviveColdViewAndHostResourceReplacement()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj"))) repository = repository.Parent;
        Assert.NotNull(repository);
        await using var api = new SyntheticClashApi { Rows = 0 };
        await using var mihomo = await IsolatedCore.CreateAsync(CoreKind.Mihomo, apiOverride: api.Origin);
        await using var singbox = await IsolatedCore.CreateAsync(CoreKind.SingBox, apiOverride: api.Origin);
        var profile = singbox.Store.Current.ActiveProfile;
        await mihomo.Store.UpdateProfileAsync(CoreKind.SingBox, 0, new() { ExePath = profile.ExePath, ConfigPath = profile.ConfigPath, ApiUrl = api.Origin });
        await mihomo.Store.CompleteSetupAsync();
        await mihomo.Store.SavePreferencesAsync(new Dictionary<string, string> { ["config/language"] = "en-US", ["config/auto-ip-check"] = "false", ["config/auto-theme"] = "false", ["config/default-theme"] = "light" });
        var assets = Path.Combine(mihomo.Root, "resources", "dashboard");
        void CopyAssets()
        {
            var source = Path.Combine(repository.FullName, "resources", "dashboard");
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(assets, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
            }
        }
        CopyAssets();
        var port = IsolatedCore.FreePort();
        string? image = null;
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                CopyAssets();
                await File.AppendAllTextAsync(Path.Combine(assets, "index.html"), "\n<!-- isolated same-format content update -->\n");
            }
            var initial = pass == 0;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                using var host = new DashboardHost(mihomo.Root, assets, true, () => true, testPort: port);
                using var form = new MainForm(host, WebViewDataMaintenance.PlanForCurrentContent(mihomo.Root));
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new(-15000, -15000);
                WebView2 View() => form.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<WebView2>()).Single();
                async Task<string> Script(string code) => await View().CoreWebView2.ExecuteScriptAsync(code);
                async Task Until(Func<Task<bool>> condition)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(25);
                    while (!await condition()) { if (DateTime.UtcNow >= deadline) throw new TimeoutException("Persistent data scenario timed out."); await Task.Delay(50); }
                }
                async Task<Dictionary<string, string>[]> ReadStore(string name)
                {
                    await Script("window.__records=undefined; window.__recordError=undefined; (()=>{ const req=indexedDB.open(" + JsonSerializer.Serialize(name) + "); req.onerror=()=>window.__recordError=String(req.error); req.onsuccess=()=>{ const db=req.result; const tx=db.transaction(" + JsonSerializer.Serialize(name) + ",'readonly'); const all=tx.objectStore(" + JsonSerializer.Serialize(name) + ").getAll(); tx.oncomplete=()=>{window.__records=all.result;db.close()};tx.onabort=()=>window.__recordError=String(tx.error) };})(); true");
                    await Until(async () => await Script("window.__records !== undefined || window.__recordError !== undefined") == "true");
                    Assert.Equal("null", await Script("window.__recordError ?? null"));
                    return JsonSerializer.Deserialize<Dictionary<string, string>[]>(await Script("window.__records"))!;
                }
                async Task AssertHistory()
                {
                    var rows = await ReadStore("connection-history");
                    foreach (var (kind, count) in new[] { ("mihomo", 13), ("sing-box", 7) })
                    {
                        var record = Assert.Single(rows, row => row["key"] == "desktop:" + kind + "-sourceIP");
                        using var values = JsonDocument.Parse(record["value"]);
                        Assert.Equal(count, values.RootElement.EnumerateArray().Sum(row => row.GetProperty("count").GetInt32()));
                    }
                }
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        await host.StartCoreAsync();
                        await Until(async () => View().CoreWebView2 is not null && await Script("!!document.querySelector('#core-exe')") == "true");
                        await Until(() => Task.FromResult(host.BuildState(false).Runtime.ApiStatus == "ready" && api.ActiveSockets > 0));
                        if (initial)
                        {
                            await Script("document.querySelector('[data-testid=settings-toggle]').click(); true");
                            await Until(async () => await Script("!!document.querySelector('input[type=file][accept=\"image/*\"]')") == "true");
                            await Script("(()=>{ window.confirm=()=>false;const canvas=document.createElement('canvas');canvas.width=4;canvas.height=4;const ctx=canvas.getContext('2d');ctx.fillStyle='white';ctx.fillRect(0,0,4,4);window.__uploaded=canvas.toDataURL('image/png');const bytes=Uint8Array.from(atob(window.__uploaded.split(',')[1]),c=>c.charCodeAt(0));const transfer=new DataTransfer();transfer.items.add(new File([bytes],'fixture.png',{type:'image/png'}));const input=document.querySelector('input[type=file][accept=\"image/*\"]');input.files=transfer.files;input.dispatchEvent(new Event('change',{bubbles:true}));return true})()");
                            await Until(() => Task.FromResult(host.Preferences.TryGetValue("config/custom-background-image", out var value) && value.Contains("local-image")));
                            Assert.True(await form.FlushPreferencesAsync());
                            image = JsonSerializer.Deserialize<string>(await Script("window.__uploaded"));
                            Assert.Equal(image, Assert.Single(await ReadStore("base64"))["value"]);
                            // Controlled close batches travel through real WS -> current session -> history -> IndexedDB.
                            api.Rows = 13; await Task.Delay(2400); api.Rows = 0; await Task.Delay(2400);
                            Assert.True(await form.FlushPreferencesAsync());
                            Assert.Equal("completed", (await host.ExecuteAsync(new() { Type = "switchCore", CoreType = CoreKind.SingBox })).Status);
                            await Until(() => Task.FromResult(host.BuildState(false).Runtime.ApiStatus == "ready"));
                            api.Rows = 7; await Task.Delay(2400); api.Rows = 0; await Task.Delay(2400);
                            Assert.True(await form.FlushPreferencesAsync());
                            await AssertHistory();
                            // Exercise current HTTP(S) remote-URL policy without DNS/TLS/network changes.
                            var core = View().CoreWebView2;
                            core.AddWebResourceRequestedFilter("http://remote-api.example/*", CoreWebView2WebResourceContext.All);
                            core.AddWebResourceRequestedFilter("https://remote-api.example/*", CoreWebView2WebResourceContext.All);
                            core.WebResourceRequested += (_, args) => args.Response = core.Environment.CreateWebResourceResponse(
                                new MemoryStream(Encoding.UTF8.GetBytes("{\"version\":\"fixture\"}")), 200, "OK",
                                "Content-Type: application/json\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Headers: Authorization\r\nAccess-Control-Allow-Methods: GET, OPTIONS\r\n");
                            await Script("window.__network=null;Promise.all(['http://remote-api.example/version','https://remote-api.example/version'].map(url=>fetch(url,{headers:{Authorization:'Bearer fixture-only'}}).then(r=>r.json()))).then(r=>window.__network=r.every(x=>x.version==='fixture'),e=>window.__network=String(e));true");
                            await Until(async () => await Script("window.__network!==null") == "true");
                            Assert.Equal("true", await Script("window.__network"));
                            var localhost = api.Origin.Replace("127.0.0.1", "localhost");
                            await Script("window.__localhost=null;fetch(" + JsonSerializer.Serialize(localhost + "/version") + ").then(r=>r.json()).then(r=>window.__localhost=typeof r.version==='string',e=>window.__localhost=String(e));true");
                            await Until(async () => await Script("window.__localhost!==null") == "true");
                            Assert.Equal("true", await Script("window.__localhost"));
                            await Script("window.__localSocket=null;(()=>{const ws=new WebSocket(" + JsonSerializer.Serialize(localhost.Replace("http:", "ws:") + "/connections") + ");ws.onmessage=e=>{window.__localSocket=Array.isArray(JSON.parse(e.data).connections);ws.close()};ws.onerror=()=>window.__localSocket=false})();true");
                            await Until(async () => await Script("window.__localSocket!==null") == "true");
                            Assert.Equal("true", await Script("window.__localSocket"));
                            var download = Path.Combine(mihomo.Root, "isolated-download.txt");
                            var downloaded = false;
                            core.DownloadStarting += (_, args) => {
                                args.ResultFilePath = download; args.Handled = true;
                                args.DownloadOperation.StateChanged += (_, _) => downloaded = args.DownloadOperation.State == CoreWebView2DownloadState.Completed;
                            };
                            await Script("(()=>{const a=document.createElement('a');a.href=URL.createObjectURL(new Blob(['isolated download'],{type:'text/plain'}));a.download='fixture.txt';a.click();setTimeout(()=>URL.revokeObjectURL(a.href),5000);return true})()");
                            await Until(() => Task.FromResult(downloaded));
                            Assert.Equal("isolated download", File.ReadAllText(download));
                            typeof(MainForm).GetMethod("DisposeDashboardView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, null);
                            await (Task)typeof(MainForm).GetMethod("EnsureDashboardInitializedAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, new object[] { "persistence-cold-restore" })!;
                            await Until(async () => await Script("!!document.querySelector('#core-exe')") == "true");
                        }
                        await AssertHistory();
                        Assert.Equal(image, Assert.Single(await ReadStore("base64"))["value"]);
                        await Until(async () => await Script("document.documentElement.outerHTML.includes(" + JsonSerializer.Serialize(image) + ")") == "true");
                        Assert.True(await form.FlushPreferencesAsync());
                        Assert.Empty(api.Errors);
                        await host.ShutdownAsync(); done.TrySetResult();
                    }
                    catch (Exception error) { done.TrySetException(error); await host.ShutdownAsync(); }
                    finally { form.CloseForApplicationExit(); }
                };
                using var watchdog = new System.Windows.Forms.Timer { Interval = 120000 };
                watchdog.Tick += (_, _) => { done.TrySetException(new TimeoutException("Persistent-data watchdog.")); form.CloseForApplicationExit(); };
                watchdog.Start(); Application.Run(form);
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            await done.Task.WaitAsync(TimeSpan.FromSeconds(130));
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        }
    }
}
