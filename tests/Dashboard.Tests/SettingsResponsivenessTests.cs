using System.Windows.Forms;

namespace Dashboard.Tests;

public sealed class SettingsResponsivenessTests : TemporaryDirectoryTest
{
    [Fact]
    [Trait("Category", "WebViewIntegration")]
    public async Task SlowPersistenceKeepsTheWindowsMessageLoopResponsiveAndDoesNotPublishBeforeCommit()
    {
        var root = TestRoot;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = false;
        var store = new SettingsStore(root, persist: (path, json) =>
        {
            if (block) { entered.TrySetResult(); release.Wait(); }
            SettingsStore.WriteAtomically(path, json);
        });
        var thread = new Thread(() =>
        {
            using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-15000, -15000) };
            using var watchdog = new System.Windows.Forms.Timer { Interval = 10000 };
            watchdog.Tick += (_, _) => { release.Set(); complete.TrySetException(new TimeoutException("UI did not respond while persistence was blocked.")); form.Close(); };
            form.Shown += async (_, _) =>
            {
                try
                {
                    var before = store.Current;
                    block = true;
                    var save = store.SetOptionAsync("minimizeToTray", !before.DesktopOptions.MinimizeToTray);
                    await entered.Task;
                    var posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    form.BeginInvoke(() => posted.SetResult());
                    await posted.Task;
                    Assert.False(save.IsCompleted);
                    Assert.Same(before, store.Current);
                    release.Set(); await save;
                    Assert.NotEqual(before.DesktopOptions.MinimizeToTray, store.Current.DesktopOptions.MinimizeToTray);
                    complete.TrySetResult();
                }
                catch (Exception error) { complete.TrySetException(error); }
                finally { release.Set(); form.Close(); }
            };
            watchdog.Start(); Application.Run(form);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await complete.Task.WaitAsync(TimeSpan.FromSeconds(15)); Assert.True(thread.Join(TimeSpan.FromSeconds(5))); }
        finally { release.Set(); Assert.True(thread.Join(TimeSpan.FromSeconds(5))); CleanupTestDirectory(); }
    }
}
