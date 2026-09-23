using System.Reflection;
using Dashboard;

namespace Dashboard.Tests;

public sealed class CoreProcessManagerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedStopKeepsLiveProcessOwnership(bool killFails)
    {
        // Never launch or kill a core: inject a no-op/failing terminator and observe this test process.
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var manager = new CoreProcessManager(
            _ => { if (killFails) throw new InvalidOperationException("test kill failure"); }, (_, _) => false);
        var field = typeof(CoreProcessManager).GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(manager, process);
        try
        {
            Assert.NotNull(Record.Exception(() => manager.Stop(TimeSpan.Zero)));
            Assert.True(manager.IsRunning);
            Assert.Equal(process.Id, manager.ProcessId);
            Assert.Same(process, field.GetValue(manager));
        }
        finally
        {
            field.SetValue(manager, null);
            manager.Dispose();
        }
    }

    [Fact]
    public void FailedDisposeRetainsLiveProcessButReleasesIndependentTimer()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var manager = new CoreProcessManager(_ => throw new IOException("termination failed"), (_, _) => false);
        var processField = typeof(CoreProcessManager).GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var timerField = typeof(CoreProcessManager).GetField("_logEventTimer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        processField.SetValue(manager, process);
        using var timer = new System.Threading.Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
        timerField.SetValue(manager, timer);
        try
        {
            Assert.Throws<IOException>(() => manager.Dispose());
            Assert.Same(process, processField.GetValue(manager));
            Assert.True(manager.IsRunning);
            Assert.Null(timerField.GetValue(manager));
            Assert.False(timer.Change(0, Timeout.Infinite));
        }
        finally
        {
            processField.SetValue(manager, null);
            manager.Dispose();
        }
    }

    [Fact]
    public void LogTailIsBoundedAndReturnsNewestEntries()
    {
        using var manager = new CoreProcessManager();
        var append = typeof(CoreProcessManager).GetMethod("AppendLog", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < 501; i++) append.Invoke(manager, new object?[] { $"line-{i}" });

        var tail = manager.GetLogTail(100_000);
        Assert.DoesNotContain("line-0", tail);
        Assert.Contains("line-1", tail);
        Assert.Contains("line-500", tail);
    }
}
