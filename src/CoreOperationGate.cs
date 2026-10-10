namespace Dashboard;

// One non-queuing gate shared by core operations and core configuration edits.
internal sealed class CoreOperationGate
{
    private readonly object _sync = new();
    private bool _busy;
    private bool _closing;

    public IDisposable? TryEnter()
    {
        lock (_sync)
        {
            if (_busy || _closing) return null;
            _busy = true;
            return new Lease(this);
        }
    }

    public void Close() { lock (_sync) _closing = true; }

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
                }
            }
        }
    }
}
