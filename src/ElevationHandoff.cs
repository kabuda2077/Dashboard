using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Dashboard;

// The old owner stays alive until its replacement confirms the same Windows identity.
internal sealed class ElevationHandoff : IDisposable
{
    private readonly NamedPipeServerStream _server;
    internal string PipeName { get; } = "Dashboard.Elevation." + Guid.NewGuid().ToString("N");
    internal string UserSid { get; } = CurrentUserSid();

    internal ElevationHandoff()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(UserSid),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        _server = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
    }

    internal async Task WaitForReadyAsync(Task replacementExit)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var connected = _server.WaitForConnectionAsync(budget.Token);
        try
        {
            if (await Task.WhenAny(connected, replacementExit).ConfigureAwait(false) == replacementExit)
                throw new InvalidOperationException("管理员进程未确认接力。请使用当前 Windows 账户提权；原 Dashboard 已保留。");
            await connected.ConfigureAwait(false);
            using var reader = new StreamReader(_server, Encoding.UTF8, leaveOpen: true);
            if (await reader.ReadLineAsync(budget.Token).ConfigureAwait(false) != "ready")
                throw new InvalidOperationException("管理员进程未能确认接力，原 Dashboard 已保留。");
            // Once readiness is read, commit the handoff without another cancellation
            // point. Until this acknowledgement arrives the child must not wait on the mutex.
            using var writer = new StreamWriter(_server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            writer.WriteLine("accepted");
        }
        finally
        {
            budget.Cancel();
            try { await connected.ConfigureAwait(false); }
            catch (OperationCanceledException) when (budget.IsCancellationRequested) { }
        }
    }

    internal static void ConfirmReplacement(string expectedSid, string pipeName, TimeSpan? connectionTimeout = null)
    {
        ValidateUser(expectedSid, CurrentUserSid());
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using (var budget = new CancellationTokenSource(connectionTimeout ?? TimeSpan.FromSeconds(15)))
            client.ConnectAsync(budget.Token).GetAwaiter().GetResult();
        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        writer.WriteLine("ready");
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        // After ready, only the parent's decision or a broken pipe may end this
        // wait. A child-side timeout could abandon an already accepted handoff.
        if (reader.ReadLine() != "accepted")
            throw new InvalidOperationException("原 Dashboard 未接受管理员接力。");
    }

    internal static void ValidateUser(string expectedSid, string actualSid)
    {
        if (!string.Equals(expectedSid, actualSid, StringComparison.Ordinal))
            throw new InvalidOperationException("管理员重启必须使用原 Windows 账户。请取消跨账户提权，原 Dashboard 将继续保留。");
    }

    private static string CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new InvalidOperationException("无法确认 Windows 账户。");
    }

    public void Dispose() => _server.Dispose();
}
