using System.Net;
using System.Runtime.InteropServices;

namespace Dashboard;

// Snapshot proof only: never adopt an arbitrary listener from a configured port.
// Recheck after connect as well; the lifecycle epoch separately guards PID changes.
internal static class ManagedCoreListener
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    internal static bool Owns(IPEndPoint endpoint, int processId) => Owns(endpoint, null, owner => owner == processId);

    // A connected socket identifies the accepted server connection even when IPv4
    // is carried by a dual-stack IPv6 listener or localhost resolves both families.
    internal static bool OwnsConnection(IPEndPoint server, IPEndPoint client, Func<int, bool> ownsProcess) => Owns(server, client, ownsProcess);

    private static bool Owns(IPEndPoint endpoint, IPEndPoint? peer, Func<int, bool> ownsProcess)
    {
        try
        {
            var families = peer is null
                ? new[] { endpoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 23 : 2 }
                : new[] { 2, 23 };
            foreach (var family in families)
            {
                var ipv6 = family == 23;
                var tableClass = peer is null ? 3 : 5; // OWNER_PID_LISTENER / OWNER_PID_ALL
                var size = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, tableClass, 0);
                if (size <= 0 || size > 16 * 1024 * 1024) continue;
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetExtendedTcpTable(buffer, ref size, false, family, tableClass, 0) != 0) continue;
                    var count = Marshal.ReadInt32(buffer);
                    var rowSize = ipv6 ? 56 : 24;
                    if (count < 0 || (long)count * rowSize + 4 > size) continue;
                    for (var i = 0; i < count; i++)
                    {
                        var row = IntPtr.Add(buffer, 4 + i * rowSize);
                        if (Port(row, ipv6 ? 20 : 8) != endpoint.Port) continue;
                        var processId = Marshal.ReadInt32(row, ipv6 ? 52 : 20);
                        var address = Address(row, ipv6, remote: false);
                        if (peer is null)
                        {
                            if ((address.Equals(endpoint.Address) || address.Equals(ipv6 ? IPAddress.IPv6Any : IPAddress.Any)) && ownsProcess(processId)) return true;
                        }
                        else if (SameAddress(address, endpoint.Address) && Port(row, ipv6 ? 44 : 16) == peer.Port
                            && SameAddress(Address(row, ipv6, remote: true), peer.Address) && ownsProcess(processId)) return true;
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            return false;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        { return false; }
    }

    private static int Port(IntPtr row, int offset) => Marshal.ReadByte(row, offset) * 256 + Marshal.ReadByte(row, offset + 1);
    private static IPAddress Address(IntPtr row, bool ipv6, bool remote)
    {
        var bytes = new byte[ipv6 ? 16 : 4];
        Marshal.Copy(IntPtr.Add(row, ipv6 ? (remote ? 24 : 0) : (remote ? 12 : 4)), bytes, 0, bytes.Length);
        return ipv6 ? new IPAddress(bytes, unchecked((uint)Marshal.ReadInt32(row, remote ? 40 : 16))) : new IPAddress(bytes);
    }
    private static bool SameAddress(IPAddress left, IPAddress right) =>
        (left.IsIPv4MappedToIPv6 ? left.MapToIPv4() : left).Equals(right.IsIPv4MappedToIPv6 ? right.MapToIPv4() : right);
}
