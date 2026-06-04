namespace MihomoDashboard;

public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "Local\\MihomoDashboard.SingleInstance";
    private const string ActivateEventName = "Local\\MihomoDashboard.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly RegisteredWaitHandle _registeredWaitHandle;

    private SingleInstance(Mutex mutex, EventWaitHandle activateEvent, Action activate)
    {
        _mutex = mutex;
        _activateEvent = activateEvent;
        _registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => activate(),
            null,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
    }

    public static bool TryCreate(Action activate, bool waitForPreviousExit, out SingleInstance? instance)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew && waitForPreviousExit)
        {
            createdNew = mutex.WaitOne(TimeSpan.FromSeconds(8));
        }

        if (!createdNew)
        {
            mutex.Dispose();
            SignalExistingInstance();
            instance = null;
            return false;
        }

        var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        instance = new SingleInstance(mutex, activateEvent, activate);
        return true;
    }

    public static void SignalExistingInstance()
    {
        try
        {
            using var activateEvent = EventWaitHandle.OpenExisting(ActivateEventName);
            activateEvent.Set();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _registeredWaitHandle.Unregister(null);
        _activateEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        _mutex.Dispose();
    }
}
