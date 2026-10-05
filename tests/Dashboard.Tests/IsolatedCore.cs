using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Dashboard.Tests;

internal sealed class IsolatedCore : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "Dashboard.IsolatedCore", Guid.NewGuid().ToString("N"));
    public SettingsStore Store { get; }
    public CoreProcessManager Process { get; } = new();
    public CoreLifecycleController Controller { get; }
    public CoreKind Kind { get; }
    public string Executable => Store.Current.Profile(Kind).ExePath;

    private IsolatedCore(CoreKind kind, HttpClient? upgradeClient)
    {
        Kind = kind;
        Directory.CreateDirectory(Root);
        Store = new SettingsStore(Root);
        Controller = new CoreLifecycleController(Store, Process, () => true, upgradeClient);
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<IsolatedCore> CreateAsync(CoreKind kind, HttpClient? upgradeClient = null, string? apiOverride = null)
    {
        var source = Environment.GetEnvironmentVariable("DASHBOARD_TEST_CORES_DIR")
            ?? throw new InvalidOperationException("Set DASHBOARD_TEST_CORES_DIR to the explicitly prepared validation cores.");
        var fixture = new IsolatedCore(kind, upgradeClient);
        var name = AppSettings.WireKind(kind);
        var executable = Path.Combine(fixture.Root, name + ".exe");
        File.Copy(Path.Combine(source, name, name + ".exe"), executable);
        var port = FreePort();
        var config = Path.Combine(fixture.Root, kind == CoreKind.Mihomo ? "config.yaml" : "config.json");
        await File.WriteAllTextAsync(config, kind == CoreKind.Mihomo
            ? $"mixed-port: 0\nexternal-controller: 127.0.0.1:{port}\nmode: rule\nlog-level: info\nproxies: []\nproxy-groups: []\nrules: ['MATCH,DIRECT']\n"
            : JsonSerializer.Serialize(new
            {
                log = new { level = "info" }, inbounds = Array.Empty<object>(),
                outbounds = new[] { new { type = "direct", tag = "direct" } }, route = new { final = "direct" },
                experimental = new { clash_api = new { external_controller = $"127.0.0.1:{port}", default_mode = "rule" } }
            }));
        await fixture.Store.UpdateProfileAsync(kind, 0, new() { ExePath = executable, ConfigPath = config, ApiUrl = apiOverride ?? $"http://127.0.0.1:{port}" });
        await fixture.Store.SetActiveCoreAsync(kind);
        return fixture;
    }

    public async Task ReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (Controller.ApiStatus != "ready")
        {
            if (!Process.IsRunning || DateTime.UtcNow >= deadline)
                throw new InvalidOperationException($"Core not ready: {Controller.ApiStatus}\n{Process.GetLogTail(8000)}");
            await Task.Delay(50);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Controller.ShutdownAsync(TimeSpan.FromSeconds(10));
        Controller.Dispose(); Process.Dispose();
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}
