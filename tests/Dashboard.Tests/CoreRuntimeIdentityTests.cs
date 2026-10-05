using System.Diagnostics;
using System.Reflection;

namespace Dashboard.Tests;

public sealed class CoreRuntimeIdentityTests
{
    [Fact]
    public async Task RuntimeEpochSeparatesStopAndPidReuseAtTheSameEndpoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Dashboard.Tests", Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(directory);
        using var observed = Process.GetCurrentProcess();
        // This test never terminates the observed process, including on assertion failure.
        using var process = new CoreProcessManager(_ => { }, (_, _) => false);
        using var lifecycle = new CoreLifecycleController(store, process, () => true);
        var current = typeof(CoreProcessManager).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var changed = typeof(CoreProcessManager).GetField("StatusChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Publish() => ((EventHandler?)changed.GetValue(process))?.Invoke(process, EventArgs.Empty);
        try
        {
            current.SetValue(process, observed); Publish();
            var started = lifecycle.Epoch;
            current.SetValue(process, null); Publish();
            var stopped = lifecycle.Epoch;
            current.SetValue(process, observed); Publish();
            Assert.True(stopped > started);
            Assert.True(lifecycle.Epoch > stopped);
        }
        finally
        {
            current.SetValue(process, null); Publish();
            await lifecycle.ShutdownAsync(TimeSpan.FromSeconds(2));
            Directory.Delete(directory, true);
        }
    }
}
