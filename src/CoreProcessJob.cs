using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dashboard;

// Descendants stay in this Windows job when mihomo replaces itself during an upgrade.
internal sealed class CoreProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;

    public CoreProcessJob()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(_handle, 9, ref limits, Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            _handle.Dispose();
            throw error;
        }
    }

    public void Add(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.SafeHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public Process? FindRestartedCore(string executable, int previousId)
    {
        foreach (var id in ProcessIds())
        {
            if (id == previousId) continue;
            Process process;
            try { process = Process.GetProcessById(id); }
            catch (ArgumentException) { continue; } // A job member may already have exited.
            var retained = false;
            try
            {
                if (!process.HasExited && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                {
                    retained = true;
                    return process;
                }
            }
            catch (Win32Exception) when (process.HasExited) { }
            finally { if (!retained) process.Dispose(); }
        }
        return null;
    }

    public bool ContainsProcess(int processId) => ProcessIds().Contains(processId);

    private int[] ProcessIds()
    {
        var capacity = 16;
        while (true)
        {
            var size = 8 + capacity * IntPtr.Size;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!QueryInformationJobObject(_handle, 3, buffer, size, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != 234) throw new Win32Exception(error); // ERROR_MORE_DATA
                    capacity = Math.Max(capacity * 2, Marshal.ReadInt32(buffer));
                    continue;
                }
                var count = Marshal.ReadInt32(buffer, 4);
                return Enumerable.Range(0, count).Select(index => checked((int)Marshal.ReadIntPtr(buffer, 8 + index * IntPtr.Size))).ToArray();
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits limits, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass, IntPtr information, int length, IntPtr returnedLength);
}
