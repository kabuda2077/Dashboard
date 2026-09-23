namespace Dashboard;

// Owns only work that must become quiescent during host shutdown. Operations are
// registered before their delegate is allowed to run, so shutdown cannot miss a
// task in the start/register window.
internal sealed class ShutdownTaskTracker : IDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<Task> _tasks = [];
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _token;
    private bool _closing;

    public ShutdownTaskTracker()
    {
        _token = _cancellation.Token;
    }

    public CancellationToken Token => _token;
    public bool IsClosing { get { lock (_sync) return _closing; } }

    public Task Run(Func<CancellationToken, Task> operation)
    {
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_closing) return Task.CompletedTask;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _tasks.Add(completion.Task);
        }

        // Registration is visible before invoking the delegate, but invocation is
        // synchronous on the caller's context so UI-bound command continuations
        // are not silently moved to the thread pool.
        try
        {
            _ = CompleteAsync(operation(_token), completion);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            Remove(completion.Task);
        }
        return completion.Task;
    }

    private async Task CompleteAsync(Task operation, TaskCompletionSource completion)
    {
        try
        {
            await operation.ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            Remove(completion.Task);
        }
    }

    private void Remove(Task task)
    {
        lock (_sync) _tasks.Remove(task);
    }

    public async Task<bool> StopAndWaitAsync(TimeSpan timeout)
    {
        var unlimited = timeout == Timeout.InfiniteTimeSpan;
        var deadline = unlimited ? DateTime.MaxValue : DateTime.UtcNow + timeout;
        lock (_sync) _closing = true;
        // Cancellation callbacks may themselves finish cleanup, so never invoke
        // them while holding the tracker lock. New operations are already sealed.
        _cancellation.Cancel();

        while (true)
        {
            Task[] pending;
            lock (_sync) pending = [.. _tasks.Where(task => !task.IsCompleted)];
            if (pending.Length == 0) return true;

            var remaining = unlimited ? Timeout.InfiniteTimeSpan : deadline - DateTime.UtcNow;
            if (!unlimited && remaining <= TimeSpan.Zero) return false;

            try
            {
                await Task.WhenAll(pending).WaitAsync(remaining).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch
            {
                // Observe the finished batch and loop until the registered set is empty.
                foreach (var task in pending)
                    if (task.IsCompleted) _ = task.Exception;
            }
        }
    }

    public void Dispose() => _cancellation.Dispose();
}
