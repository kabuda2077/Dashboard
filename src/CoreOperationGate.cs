namespace Dashboard;

// One non-queuing gate shared by core operations and core configuration edits.
internal sealed class CoreOperationGate
{
    private readonly object _sync = new();
    private bool _busy;
    private bool _closing;
    private TaskCompletionSource _idle = CompletedSignal();
    public bool IsClosing { get { lock (_sync) return _closing; } }

    public IDisposable? TryEnter()
    {
        lock (_sync)
        {
            if (_busy || _closing) return null;
            _busy = true;
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new Lease(this);
        }
    }

    public void Close() { lock (_sync) _closing = true; }

    public async Task<bool> WaitForIdleAsync(TimeSpan timeout)
    {
        Task idle;
        lock (_sync) idle = _idle.Task;
        try
        {
            await idle.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    private sealed class Lease(CoreOperationGate owner) : IDisposable
    {
        private CoreOperationGate? _owner = owner;
        public void Dispose()
        {
            var gate = Interlocked.Exchange(ref _owner, null);
            if (gate is not null)
            {
                lock (gate._sync)
                {
                    gate._busy = false;
                    gate._idle.TrySetResult();
                }
            }
        }
    }
}
