using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard.Tests;

[Collection("Release WebView")]
public sealed class MainFormNavigationTests : TemporaryDirectoryTest
{
    [Fact]
    [Trait("Category", "WebViewIntegration")]
    public async Task FailedInitialNavigationCanRetryAndFailedReloadCanRecoverFromTray()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var assets = Path.Combine(repository.FullName, "resources", "dashboard");
        var root = TestRoot;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var host = new DashboardHost(root, assets, true, () => false);
            using var form = new MainForm(host, WebViewDataMaintenance.PlanForCurrentContent(root, dashboardDirectory: assets));
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new(-15000, -15000);
            WebView2? View() => form.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<WebView2>()).SingleOrDefault();
            bool Initialized() => (bool)typeof(MainForm).GetField("_dashboardInitialized", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Control? Failure() => form.Controls.Find("dashboardLoadFailure", true).SingleOrDefault();
            async Task WaitFor(Func<Task<bool>> ready)
            {
                var deadline = DateTime.UtcNow.AddSeconds(25);
                while (!await ready())
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException(
                        $"Dashboard navigation did not reach the expected state: initialized={Initialized()}, source={View()?.Source}, retryVisible={Failure()?.Visible}.");
                    await Task.Delay(25);
                }
            }
            Task WaitForReady() => WaitFor(async () => Initialized() && View()?.CoreWebView2 is { } core
                && await core.ExecuteScriptAsync("!!document.querySelector('#core-exe')") == "true");
            form.Shown += async (_, _) =>
            {
                try
                {
                    await WaitFor(() => Task.FromResult(View() is not null));
                    var original = View()!;
                    var cancelNext = true;
                    CoreWebView2NavigationCompletedEventArgs? lastCompletion = null;
                    void Attach()
                    {
                        var core = original.CoreWebView2 ?? throw new InvalidOperationException("WebView controller did not initialize.");
                        core.NavigationStarting += (_, args) =>
                        {
                            if (cancelNext && args.Uri.StartsWith(host.DashboardUri.AbsoluteUri, StringComparison.Ordinal))
                            {
                                cancelNext = false;
                                args.Cancel = true;
                            }
                        };
                        core.NavigationCompleted += (_, args) => lastCompletion = args;
                    }
                    if (original.CoreWebView2 is not null) Attach();
                    else original.CoreWebView2InitializationCompleted += (_, args) => { if (args.IsSuccess) Attach(); };

                    await WaitFor(() => Task.FromResult(Failure()?.Visible == true));
                    Assert.False(Initialized());
                    Assert.False(lastCompletion!.IsSuccess);
                    var retry = Assert.IsType<Button>(form.Controls.Find("retryDashboard", true).Single());
                    retry.PerformClick();
                    await WaitForReady();
                    Assert.Same(original, View());
                    Assert.False(Failure()!.Visible);
                    var previousSuccess = lastCompletion!;
                    Assert.True(previousSuccess.IsSuccess);

                    cancelNext = true;
                    original.CoreWebView2!.Reload();
                    await WaitFor(() => Task.FromResult(Failure()?.Visible == true));
                    Assert.False(Initialized());
                    Assert.False(lastCompletion!.IsSuccess);
                    // Replaying a real completion from the previous navigation must not
                    // hide the current failure or claim that this document initialized.
                    typeof(MainForm).GetMethod("OnDashboardNavigationCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(form, [original.CoreWebView2, previousSuccess]);
                    Assert.False(Initialized());
                    Assert.True(Failure()!.Visible);

                    form.Close();
                    await WaitFor(() => Task.FromResult(!form.Visible && !form.IsTrayTransitionInProgress));
                    form.ShowFromTray();
                    await WaitForReady();
                    Assert.True(form.Visible);
                    Assert.False(Failure()!.Visible);
                    Assert.Same(original, View());
                    await host.ShutdownAsync();
                    await TestBrowser.DisposeAsync(form);
                    done.TrySetResult();
                }
                catch (Exception error) { done.TrySetException(error); }
                finally
                {
                    try { await host.ShutdownAsync(); await TestBrowser.DisposeAsync(form); }
                    catch (Exception error) { done.TrySetException(error); }
                    finally { form.CloseForApplicationExit(); }
                }
            };
            using var watchdog = new System.Windows.Forms.Timer { Interval = 100000 };
            watchdog.Tick += (_, _) => { done.TrySetException(new TimeoutException("Navigation test watchdog.")); form.CloseForApplicationExit(); };
            watchdog.Start();
            Application.Run(form);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(110)); }
        finally { Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Navigation test thread did not exit."); }
    }
}
