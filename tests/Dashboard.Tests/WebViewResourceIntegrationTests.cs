using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Windows.Forms;

namespace Dashboard.Tests;

public sealed class WebViewResourceIntegrationTests : TemporaryDirectoryTest
{
    [Fact]
    [Trait("Category", "WebViewIntegration")]
    public async Task SecureVirtualHostBlocksCurrentlySupportedPlainHttpRemoteApis()
    {
        var root = TestRoot;
        var content = Path.Combine(root, "ui");
        Directory.CreateDirectory(content);
        await File.WriteAllTextAsync(Path.Combine(content, "index.html"), """
            <!doctype html><meta charset="utf-8"><script>
            chrome.webview.postMessage('loaded');
            fetch('http://api.example/version', {headers:{Authorization:'Bearer test'}, signal: AbortSignal.timeout(3000)})
              .then(() => chrome.webview.postMessage('request-ended'))
              .catch(() => chrome.webview.postMessage('request-ended'));
            </script>
            """);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var form = new Form { Width = 640, Height = 480, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-15000, -15000) };
            using var view = new WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(view);
            string blockedReason = "";
            string stage = "waiting for Shown";
            form.Shown += async (_, _) =>
            {
                try
                {
                    stage = "creating environment";
                    var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(root, "profile"));
                    stage = "creating controller";
                    await view.EnsureCoreWebView2Async(environment);
                    stage = "enabling network diagnostics";
                    view.CoreWebView2.SetVirtualHostNameToFolderMapping("appassets.example", content, CoreWebView2HostResourceAccessKind.DenyCors);
                    await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                    view.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.loadingFailed").DevToolsProtocolEventReceived += (_, args) =>
                    {
                        using var json = JsonDocument.Parse(args.ParameterObjectAsJson);
                        if (json.RootElement.TryGetProperty("blockedReason", out var reason)) blockedReason = reason.GetString() ?? "";
                    };
                    view.CoreWebView2.NavigationCompleted += (_, args) => stage = $"navigation success={args.IsSuccess} status={args.WebErrorStatus}";
                    view.CoreWebView2.WebMessageReceived += async (_, args) =>
                    {
                        if (args.TryGetWebMessageAsString() == "loaded") { stage = "script loaded"; return; }
                        try
                        {
                            await Task.Delay(150);
                            await TestBrowser.DisposeAsync(form);
                            completion.TrySetResult(blockedReason);
                        }
                        catch (Exception error) { completion.TrySetException(error); }
                        finally { form.Close(); }
                    };
                    view.CoreWebView2.Navigate("https://appassets.example/index.html");
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                    try { await TestBrowser.DisposeAsync(form); }
                    catch (Exception cleanupError) { completion.TrySetException(cleanupError); }
                    finally { form.Close(); }
                }
            };
            using var timer = new System.Windows.Forms.Timer { Interval = 25000 };
            timer.Tick += (_, _) => { completion.TrySetException(new TimeoutException($"WebView resource probe did not complete: {stage}.")); form.Close(); };
            timer.Start();
            Application.Run(form);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            var blocked = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("mixed-content", blocked);
        }
        finally
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            CleanupTestDirectory();
        }
    }
}
