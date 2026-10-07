using System.Collections.Concurrent;

namespace Dashboard.Tests;

public sealed class HostLogPressureTests : TemporaryDirectoryTest
{
    [Fact]
    public async Task BlockedDiskDoesNotBlockProducersAndQueueAndErrorFallbackStayBounded()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var written = new ConcurrentQueue<string>();
        var queue = new HostLogQueue((_, text) => { entered.Set(); release.Wait(); written.Enqueue(text); });
        queue.Enqueue("test", "first", false);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            await Task.Run(() =>
            {
                for (var i = 0; i < 10000; i++) queue.Enqueue("test", i.ToString(), false);
                for (var i = 0; i < 10000; i++) queue.Enqueue("error", "important-" + i, true);
                queue.Enqueue("test", new string('x', 100000), false);
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HostLogQueue.DefaultCapacity, queue.PendingCount);
            Assert.Equal(20001 - HostLogQueue.DefaultCapacity, queue.Dropped);
            var stop = queue.StopAsync();
            Assert.False(stop.IsCompleted);
            release.Set();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("important-9999", written);
            Assert.Equal(HostLogQueue.DefaultCapacity + 2, written.Count); // queued + in-flight + one fallback
            Assert.Equal(0, queue.PendingCount);
            queue.Enqueue("test", "after shutdown", true);
            Assert.DoesNotContain("after shutdown", written);
        }
        finally { release.Set(); await queue.StopAsync(); }
    }

    [Fact]
    public async Task HugeEntriesAreTruncatedAndWriterExceptionsDoNotKillTheDrain()
    {
        var texts = new List<string>();
        var calls = 0;
        var queue = new HostLogQueue((_, text) =>
        {
            if (++calls == 1) throw new IOException("injected disk failure");
            texts.Add(text);
        });
        queue.Enqueue("test", "fails", false);
        queue.Enqueue("test", new string('x', 1000000), true);
        queue.Enqueue("test", "recovered", false);
        await queue.StopAsync();
        Assert.Equal(1, queue.WriterFailures);
        Assert.Equal(2, texts.Count);
        Assert.True(texts[0].Length < HostLogQueue.MaximumEntryCharacters + 100);
        Assert.EndsWith("[truncated]", texts[0]);
        Assert.Equal("recovered", texts[1]);
    }

    [Fact]
    public async Task UnwritableDirectoryCountsFailuresAndCanRecoverWithoutRecursiveLogging()
    {
        var root = TestRoot;
        Directory.CreateDirectory(root);
        var blocked = Path.Combine(root, "logs");
        File.WriteAllText(blocked, "not a directory");
        var writer = new HostLogFileWriter(blocked, 1024, 2);
        var queue = new HostLogQueue(writer.Write);
        try
        {
            for (var i = 0; i < 1000; i++) queue.Enqueue("test", "failed write", false);
            await queue.StopAsync();
            Assert.Equal(1000, writer.WriteFailures);
            Assert.Equal(0, queue.PendingCount);
            File.Delete(blocked);
            writer.Write("test", "recovered");
            Assert.Equal("recovered", File.ReadAllText(Path.Combine(blocked, "test.log")));
        }
        finally { await queue.StopAsync(); CleanupTestDirectory(); }
    }
}
