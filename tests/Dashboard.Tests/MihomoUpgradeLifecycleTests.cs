using System.Diagnostics;

namespace Dashboard.Tests;

public sealed class MihomoUpgradeLifecycleTests : TemporaryDirectoryTest
{
    [Fact]
    public async Task UpgradeFollowsItsReplacementAndKeepsLogsRestartAndStopWorking()
    {
        var store = await PrepareAsync("restart");
        using var process = new CoreProcessManager();
        using var controller = Controller(store, process);
        try
        {
            Assert.Equal("completed", (await controller.ExecuteAsync(new() { Type = "start", CoreType = CoreKind.Mihomo })).Status);
            await ReadyAsync(controller);
            var originalPid = process.ProcessId;
            Assert.True(controller.ApiIsManaged);
            var result = await controller.ExecuteAsync(new() { Type = "upgradeCore", CoreType = CoreKind.Mihomo });
            Assert.Equal("completed", result.Status);
            Assert.Equal("upgraded", result.Code);
            Assert.NotEqual(originalPid, process.ProcessId);
            Assert.Equal("2.0.0", controller.ApiVersion);
            Assert.True(controller.ApiIsManaged);
            using var replacement = Process.GetProcessById(process.ProcessId!.Value);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!process.GetLogTail(8000).Contains("replacement /version")) await Task.Delay(20, budget.Token);

            Assert.Equal("completed", (await controller.ExecuteAsync(new() { Type = "restart", CoreType = CoreKind.Mihomo })).Status);
            await ReadyAsync(controller);
            Assert.True(replacement.HasExited);
            Assert.NotEqual(replacement.Id, process.ProcessId);
            Assert.True(controller.ApiIsManaged);
            Assert.Equal("completed", (await controller.ExecuteAsync(new() { Type = "stop", CoreType = CoreKind.Mihomo })).Status);
            Assert.False(process.IsRunning);
        }
        finally { Assert.True(await controller.ShutdownAsync(TimeSpan.FromSeconds(5))); }
    }

    [Fact]
    public async Task SuccessfulUpgradeResponseWithoutAReplacementDoesNotReportUpgraded()
    {
        var store = await PrepareAsync("exit");
        using var process = new CoreProcessManager();
        using var controller = Controller(store, process);
        try
        {
            await controller.ExecuteAsync(new() { Type = "start", CoreType = CoreKind.Mihomo });
            await ReadyAsync(controller);
            controller.ProbeTiming = (TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(20));
            var result = await controller.ExecuteAsync(new() { Type = "upgradeCore", CoreType = CoreKind.Mihomo });
            Assert.Equal("failed", result.Status);
            Assert.Equal("upgradeUnconfirmed", result.Code);
            Assert.False(process.IsRunning);
            Assert.False(controller.ApiIsManaged);
        }
        finally { Assert.True(await controller.ShutdownAsync(TimeSpan.FromSeconds(5))); }
    }

    [Fact]
    public async Task ReachableExternalApiStaysConnectedButCannotUpgradeTheManagedCore()
    {
        await using var external = new SyntheticClashApi();
        var store = await PrepareAsync("restart", external.Origin);
        using var process = new CoreProcessManager();
        using var controller = Controller(store, process);
        try
        {
            await controller.ExecuteAsync(new() { Type = "start", CoreType = CoreKind.Mihomo });
            await ReadyAsync(controller);
            Assert.False(controller.ApiIsManaged);
            Assert.Equal("1.19.31", controller.ApiVersion);
            var result = await controller.ExecuteAsync(new() { Type = "upgradeCore", CoreType = CoreKind.Mihomo });
            Assert.Equal("rejected", result.Status);
            Assert.Equal("apiNotManaged", result.Code);
            Assert.False(external.Requests.ContainsKey("/upgrade"));
            Assert.True(process.IsRunning);
        }
        finally { Assert.True(await controller.ShutdownAsync(TimeSpan.FromSeconds(5))); }
    }

    private static CoreLifecycleController Controller(SettingsStore store, CoreProcessManager process) => new(store, process, () => true,
        readCoreVersion: (_, _, _) => Task.FromResult("Mihomo Meta v1.19.29 windows amd64"))
    {
        ProbeTiming = (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(20))
    };

    private static async Task ReadyAsync(CoreLifecycleController controller)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        while (controller.ApiStatus == "checking") await Task.Delay(20, budget.Token);
        Assert.Equal("ready", controller.ApiStatus);
    }

    private async Task<SettingsStore> PrepareAsync(string mode, string? apiOverride = null)
    {
        var source = Path.Combine(TestRoot, "Core.cs");
        var executable = Path.Combine(TestRoot, "mihomo.exe");
        var config = Path.Combine(TestRoot, "core.txt");
        var port = IsolatedCore.FreePort();
        await File.WriteAllTextAsync(config, port + "\n" + mode);
        await File.WriteAllTextAsync(source, """
            using System;
            using System.Diagnostics;
            using System.IO;
            using System.Net;
            using System.Net.Sockets;
            using System.Reflection;
            using System.Runtime.InteropServices;
            using System.Text;
            class Core {
                [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
                struct Startup {
                    public int size;
                    public string reserved, desktop, title;
                    public int x, y, width, height, xChars, yChars, fill, flags;
                    public short show, reservedSize;
                    public IntPtr reservedBytes, input, output, error;
                }
                struct Started { public IntPtr process, thread; public int processId, threadId; }
                [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int id);
                [DllImport("kernel32.dll")] static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
                [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
                [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                static extern bool CreateProcess(string app, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
                    bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out Started started);
                static void Restart(string path) {
                    // Match Go's explicit os.Stdout/os.Stderr inheritance, not .NET's default Process.Start handles.
                    var startup = new Startup { size = Marshal.SizeOf(typeof(Startup)), flags = 0x100,
                        input = GetStdHandle(-10), output = GetStdHandle(-11), error = GetStdHandle(-12) };
                    SetHandleInformation(startup.output, 1, 1); SetHandleInformation(startup.error, 1, 1);
                    var exe = Assembly.GetExecutingAssembly().Location;
                    Started child;
                    if (!CreateProcess(exe, new StringBuilder("\"" + exe + "\" -f \"" + path + "\" --replacement"), IntPtr.Zero, IntPtr.Zero,
                        true, 0x08000000, IntPtr.Zero, null, ref startup, out child)) throw new System.ComponentModel.Win32Exception();
                    CloseHandle(child.thread); CloseHandle(child.process);
                }
                static void Main(string[] args) {
                    var path = args[Array.IndexOf(args, "-f") + 1];
                    var config = File.ReadAllLines(path);
                    var replacement = Array.IndexOf(args, "--replacement") >= 0;
                    var listener = new TcpListener(IPAddress.Loopback, int.Parse(config[0]));
                    listener.Start();
                    while (true) {
                        using (var client = listener.AcceptTcpClient())
                        using (var stream = client.GetStream())
                        using (var reader = new StreamReader(stream)) {
                            var line = reader.ReadLine();
                            if (line == null) continue;
                            var request = line.Split(' ')[1];
                            while (!string.IsNullOrEmpty(reader.ReadLine())) { }
                            Console.WriteLine((replacement ? "replacement " : "original ") + request);
                            var body = request == "/version" ? "{\"version\":\"" + (replacement ? "2.0.0" : "1.0.0") + "\"}" : "{}";
                            var response = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body);
                            stream.Write(response, 0, response.Length);
                            stream.Flush();
                            if (request != "/upgrade") continue;
                            client.Close();
                            listener.Stop();
                            if (config[1] == "restart") Restart(path);
                            Environment.Exit(0);
                        }
                    }
                }
            }
            """);
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        var start = new ProcessStartInfo(compiler) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/nologo", "/target:exe", "/out:" + executable, source }) start.ArgumentList.Add(arg);
        using var compile = Process.Start(start)!;
        var output = compile.StandardOutput.ReadToEndAsync();
        var error = compile.StandardError.ReadToEndAsync();
        await compile.WaitForExitAsync();
        Assert.True(compile.ExitCode == 0, await output + await error);
        var store = new SettingsStore(TestRoot);
        await store.UpdateProfileAsync(CoreKind.Mihomo, 0, new() { ExePath = executable, ConfigPath = config, ApiUrl = apiOverride ?? $"http://127.0.0.1:{port}" });
        return store;
    }
}
