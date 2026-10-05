using System.Threading.Channels;

namespace Dashboard;

// One bounded sink, owned by HostOperationLogger. The writer is injected so slow
// disks and failures can be exercised without blocking the application's UI.
internal sealed class HostLogQueue
{
    internal const int DefaultCapacity = 1024;
    internal const int MaximumEntryCharacters = 8192;
    private readonly Channel<Entry> _entries;
    private readonly Action<string, string> _write;
    private readonly Task _drain;
    private Entry? _overflowError;
    private long _dropped;
    private long _writerFailures;
    private int _closing;
    public long Dropped => Interlocked.Read(ref _dropped);
    public long WriterFailures => Interlocked.Read(ref _writerFailures);
    internal int PendingCount => _entries.Reader.Count;

    public HostLogQueue(Action<string, string> write, int capacity = DefaultCapacity)
    {
        _write = write;
        _entries = Channel.CreateBounded<Entry>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false
        });
        _drain = Task.Run(DrainAsync);
    }

    public void Enqueue(string category, string text, bool important)
    {
        if (Volatile.Read(ref _closing) != 0) return;
        if (text.Length > MaximumEntryCharacters) text = text[..MaximumEntryCharacters] + " [truncated]";
        var entry = new Entry(category, text);
        if (!_entries.Writer.TryWrite(entry))
        {
            Interlocked.Increment(ref _dropped);
            if (important && Volatile.Read(ref _closing) == 0)
                Interlocked.Exchange(ref _overflowError, entry);
        }
    }

    public Task StopAsync()
    {
        if (Interlocked.Exchange(ref _closing, 1) == 0) _entries.Writer.TryComplete();
        return _drain;
    }

    private void Write(Entry entry)
    {
        try { _write(entry.Category, entry.Text); }
        catch { Interlocked.Increment(ref _writerFailures); } // never recurse into this sink
    }

    private async Task DrainAsync()
    {
        await foreach (var entry in _entries.Reader.ReadAllAsync())
        {
            var important = Interlocked.Exchange(ref _overflowError, null);
            if (important is not null) Write(important);
            Write(entry);
        }
        var finalError = Interlocked.Exchange(ref _overflowError, null);
        if (finalError is not null) Write(finalError);
    }

    private sealed record Entry(string Category, string Text);
}
