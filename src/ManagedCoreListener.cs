using System.Net;
using System.Runtime.InteropServices;

namespace Dashboard;

// Snapshot proof only: never adopt an arbitrary listener from a configured port.
// Recheck after connect as well; the lifecycle epoch separately guards PID changes.
internal static class ManagedCoreListener
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    internal static bool Owns(IPEndPoint endpoint, int processId)
    {
        try
        {
            var ipv6 = endpoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            var family = ipv6 ? 23 : 2;
            var size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3, 0);
            if (size <= 0 || size > 16 * 1024 * 1024) return false;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, family, 3, 0) != 0) return false;
                var count = Marshal.ReadInt32(buffer);
                var rowSize = ipv6 ? 56 : 24;
                if (count < 0 || (long)count * rowSize + 4 > size) return false;
                for (var i = 0; i < count; i++)
                {
                    var row = IntPtr.Add(buffer, 4 + i * rowSize);
                    var portOffset = ipv6 ? 20 : 8;
                    var port = Marshal.ReadByte(row, portOffset) * 256 + Marshal.ReadByte(row, portOffset + 1);
                    if (port != endpoint.Port || Marshal.ReadInt32(row, ipv6 ? 52 : 20) != processId) continue;
                    var bytes = new byte[ipv6 ? 16 : 4];
                    Marshal.Copy(IntPtr.Add(row, ipv6 ? 0 : 4), bytes, 0, bytes.Length);
                    var address = ipv6
                        ? new IPAddress(bytes, unchecked((uint)Marshal.ReadInt32(row, 16)))
                        : new IPAddress(bytes);
                    if (address.Equals(endpoint.Address) || address.Equals(ipv6 ? IPAddress.IPv6Any : IPAddress.Any)) return true;
                }
                return false;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        { return false; }
    }
}
