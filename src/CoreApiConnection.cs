using System.Net;
using System.Net.Sockets;

namespace Dashboard;

internal sealed class CoreApiOwnershipException() : InvalidOperationException("The API connection does not belong to the managed core.");

// Check the actual connected socket, including localhost/IPv6, rather than infer ownership from a URL.
internal sealed class CoreApiConnection : IDisposable
{
    public HttpClient Client { get; }
    public bool IsManaged { get; private set; }

    public CoreApiConnection(int processId, Func<int, bool>? ownsProcess = null, bool requireOwnership = true)
    {
        Client = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                    IsManaged = ManagedCoreListener.OwnsConnection((IPEndPoint)socket.RemoteEndPoint!, (IPEndPoint)socket.LocalEndPoint!,
                        owner => ownsProcess?.Invoke(owner) ?? owner == processId);
                    if (requireOwnership && !IsManaged) throw new CoreApiOwnershipException();
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        });
    }

    public void Dispose() => Client.Dispose();
}
