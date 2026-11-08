using System.Diagnostics;
using System.Reflection;
using System.Text;
using Dashboard;

namespace Dashboard.Tests;

public sealed class CoreProcessManagerTests
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private static T? Field<T>(CoreProcessManager manager, string name) where T : class =>
        (T?)typeof(CoreProcessManager).GetField(name, PrivateInstance)!.GetValue(manager);

    private static void StartChild(CoreProcessManager manager, string script, string? secret = null)
    {
        // Exercise the production ownership/reader path with an isolated shell,
        // without launching a configured core or touching the user's profile.
        var info = new ProcessStartInfo("pwsh.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) info.ArgumentList.Add(argument);
        lock (Field<object>(manager, "_operationLock")!)
        {
            typeof(CoreProcessManager).GetMethod("DisposeExitedProcess", PrivateInstance)!.Invoke(manager, null);
            typeof(CoreProcessManager).GetMethod("StartProcess", PrivateInstance)!
                .Invoke(manager, new object?[] { info, "isolated test child", secret });
        }
    }

    [Theory]
    [InlineData("cut-secret", 8189, "cut")]
    [InlineData("aaa", 8188, "aa")]
    public async Task HugeLinesDrainBothPipesAndPreserveFollowingLinesAndRedaction(
        string secret, int prefixLength, string forbiddenPrefix)
    {
        using var manager = new CoreProcessManager();
        StartChild(manager, $$"""
            $secret = '{{secret}}'
            [Console]::Out.Write(('x' * {{prefixLength}}) + ($secret * 2) + ('x' * 3000000))
            [Console]::Out.Write("`r"); [Console]::Out.Flush()
            Start-Sleep -Milliseconds 30
            [Console]::Out.Write("`nstdout-after $secret`n")
            [Console]::Error.Write(('y' * 3000000))
            [Console]::Error.Write("`nstderr-after $secret`n")
            [Console]::Out.Write(('z' * 8192))
            """, secret);
        var completion = Field<Task>(manager, "_outputTask")!;
        await completion.WaitAsync(TimeSpan.FromSeconds(20));

        var tail = manager.GetLogTail(100_000);
        var lines = tail.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var truncated = lines.Where(line => line.EndsWith(" [truncated]", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, truncated.Length);
        Assert.All(truncated, line => Assert.True(line.Length <= "[00:00:00] ".Length + 8192 + " [truncated]".Length));
        Assert.DoesNotContain(forbiddenPrefix, tail); // Includes partial and overlapping secrets at the boundary.
        Assert.Contains("stdout-after [redacted]", tail);
        Assert.Contains("stderr-after [redacted]", tail);
        Assert.Contains(lines, line => line.EndsWith(new string('z', 8192), StringComparison.Ordinal));
        Assert.False(manager.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAndDisposeJoinReadersAndAnOldExitCannotAffectTheNextChild(bool dispose)
    {
        var manager = new CoreProcessManager();
        try
        {
            StartChild(manager, "while ($true) { [Console]::Out.Write(('x' * 2048)); [Console]::Error.Write(('y' * 2048)) }");
            var previous = Field<Task>(manager, "_outputTask")!;
            using var previousChild = Process.GetProcessById(manager.ProcessId!.Value);
            if (dispose) manager.Dispose();
            else manager.Stop(TimeSpan.FromSeconds(5));
            Assert.True(previous.IsCompletedSuccessfully);
            Assert.True(previousChild.HasExited);
            Assert.Null(Field<Task>(manager, "_outputTask"));
            Assert.Null(Field<CancellationTokenSource>(manager, "_outputCancellation"));

            if (!dispose)
            {
                var stoppedFromNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler? onExited = null;
                onExited = (_, _) =>
                {
                    if (manager.IsRunning) return;
                    manager.StatusChanged -= onExited;
                    try
                    {
                        var current = Field<Task>(manager, "_outputTask")!;
                        manager.Stop(TimeSpan.FromSeconds(5));
                        Assert.True(current.IsCompletedSuccessfully);
                        stoppedFromNotification.TrySetResult();
                    }
                    catch (Exception error) { stoppedFromNotification.TrySetException(error); }
                };
                manager.StatusChanged += onExited;
                StartChild(manager, "[Console]::Out.WriteLine('next-child')");
                await stoppedFromNotification.Task.WaitAsync(TimeSpan.FromSeconds(15));
                var tail = manager.GetLogTail(100_000);
                Assert.Contains("next-child", tail);
                Assert.Equal(1, tail.Split("exited with code", StringSplitOptions.None).Length - 1);
                Assert.Null(Field<Task>(manager, "_outputTask"));
            }
        }
        finally { manager.Dispose(); }
    }

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
