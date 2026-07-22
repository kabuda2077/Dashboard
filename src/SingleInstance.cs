using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Dashboard;

public sealed class SingleInstance : IDisposable
{
    private const int ActivationTimeoutMs = 5000;
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly Action _activate;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _serverTask;
    private bool _ownsMutex;
    private bool _disposed;

    private SingleInstance(Mutex mutex, bool ownsMutex, string pipeName, Action activate)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
        _pipeName = pipeName;
        _activate = activate;
        _serverTask = Task.Run(RunActivationServerAsync);
    }

    public static bool TryCreate(Action activate, bool waitForPreviousExit, out SingleInstance? instance)
    {
        return TryCreate(activate, waitForPreviousExit, GetNames(), out instance);
    }

    internal static bool TryCreate(
        Action activate,
        bool waitForPreviousExit,
        SingleInstanceNames names,
        out SingleInstance? instance)
    {
        var mutex = CreateMutex(names.MutexName, out var createdNew);
        var ownsMutex = createdNew;
        if (!createdNew && waitForPreviousExit)
        {
            try
            {
                ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(8));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }
        }

        if (!ownsMutex)
        {
            mutex.Dispose();
            var acknowledged = SendActivation(names.PipeName);
            if (!acknowledged)
            {
                HostOperationLogger.Info("single-instance", "Existing instance did not acknowledge activation.");
            }
            instance = null;
            return false;
        }

        instance = new SingleInstance(mutex, true, names.PipeName, activate);
        return true;
    }

    public static void SignalExistingInstance()
    {
        _ = SendActivation(GetNames().PipeName);
    }

    internal static SingleInstanceNames GetNames(string? sid = null)
    {
        sid ??= GetCurrentUserSid();
        var suffix = sid.Replace('-', '.');
        return new SingleInstanceNames(
            $"Local\\Dashboard.SingleInstance.{suffix}",
            $"Dashboard.Activate.{suffix}");
    }

    private async Task RunActivationServerAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = CreatePipeServer(_pipeName);
                await server.WaitForConnectionAsync(_cancellation.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
                {
                    AutoFlush = true
                };
                var command = await reader.ReadLineAsync(_cancellation.Token);
                if (string.Equals(command, "activate", StringComparison.Ordinal))
                {
                    try
                    {
                        _activate();
                        await writer.WriteLineAsync("ok");
                    }
                    catch (Exception ex)
                    {
                        HostOperationLogger.Error("single-instance", "Activation callback failed.", ex);
                        await writer.WriteLineAsync("error");
                    }
                }
                else
                {
                    await writer.WriteLineAsync("unknown");
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                HostOperationLogger.Error("single-instance", "Activation pipe server failed.", ex);
                try
                {
                    await Task.Delay(200, _cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static bool SendActivation(string pipeName)
    {
        try
        {
            using var timeout = new CancellationTokenSource(ActivationTimeoutMs);
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            client.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            writer.WriteLine("activate");
            var response = reader.ReadLineAsync(timeout.Token).GetAwaiter().GetResult();
            return string.Equals(response, "ok", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("single-instance", "Failed to activate existing instance.", ex);
            return false;
        }
    }

    private static Mutex CreateMutex(string name, out bool createdNew)
    {
        var security = new MutexSecurity();
        foreach (var sid in GetAllowedSids())
        {
            security.AddAccessRule(new MutexAccessRule(
                sid,
                MutexRights.FullControl,
                AccessControlType.Allow));
        }

        return MutexAcl.Create(true, name, out createdNew, security);
    }

    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        var security = new PipeSecurity();
        foreach (var sid in GetAllowedSids())
        {
            security.AddAccessRule(new PipeAccessRule(
                sid,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096,
            4096,
            security);
    }

    private static IEnumerable<SecurityIdentifier> GetAllowedSids()
    {
        yield return new SecurityIdentifier(GetCurrentUserSid());
        yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
    }

    private static string GetCurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new InvalidOperationException("Unable to resolve the current user SID.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        try
        {
            _serverTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _cancellation.Dispose();
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            _ownsMutex = false;
        }
        _mutex.Dispose();
    }
}

internal sealed record SingleInstanceNames(string MutexName, string PipeName);
