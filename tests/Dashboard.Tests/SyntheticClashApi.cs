using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dashboard.Tests;

// Deterministic, loopback-only UI workload. Never a replacement for real core/API tests.
internal sealed class SyntheticClashApi : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentBag<Task> _clients = new();
    private readonly Task _accept;
    private int _sockets, _messages, _rows = 100;
    public int ActiveSockets => Volatile.Read(ref _sockets);
    public int Messages => Volatile.Read(ref _messages);
    public int Rows { get => Volatile.Read(ref _rows); set => Volatile.Write(ref _rows, value); }
    public string Origin { get; }
    public readonly ConcurrentDictionary<string, int> Requests = new();
    public readonly ConcurrentQueue<string> Errors = new();

    public SyntheticClashApi()
    {
        _listener.Start();
        Origin = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _accept = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _clients.Add(ServeAsync(client));
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var line = await reader.ReadLineAsync(_stop.Token) ?? "";
            var parts = line.Split(' ');
            if (parts.Length < 2) return;
            var path = parts[1].Split('?')[0];
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token)))
            { var split = line.IndexOf(':'); if (split > 0) headers[line[..split]] = line[(split + 1)..].Trim(); }
            Requests.AddOrUpdate(path, 1, (_, count) => count + 1);
            if (headers.TryGetValue("Sec-WebSocket-Key", out var key))
            {
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), _stop.Token);
                using var socket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(10));
                Interlocked.Increment(ref _sockets);
                try
                {
                    for (var tick = 1; !_stop.IsCancellationRequested; tick++)
                    {
                        var data = JsonSerializer.SerializeToUtf8Bytes(Response(path, tick));
                        await socket.SendAsync(data, WebSocketMessageType.Text, true, _stop.Token);
                        Interlocked.Increment(ref _messages);
                        await Task.Delay(1000, _stop.Token);
                    }
                }
                finally { Interlocked.Decrement(ref _sockets); }
            }
            else
            {
                var body = JsonSerializer.SerializeToUtf8Bytes(Response(path, 1));
                var cors = "Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Headers: Authorization, Content-Type\r\nAccess-Control-Allow-Methods: GET, PATCH, POST, PUT, DELETE, OPTIONS\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\n{cors}Content-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), _stop.Token);
                await stream.WriteAsync(body, _stop.Token);
            }
        }
        catch (Exception error) when (_stop.IsCancellationRequested || error is IOException or WebSocketException or SocketException) { }
        catch (Exception error) { Errors.Enqueue(error.ToString()); }
    }
    private object Response(string path, int tick) => path switch
    {
        "/version" => new { version = "1.19.31", meta = true },
        "/configs" => new Dictionary<string, object> { ["mode"] = "rule", ["allow-lan"] = false, ["mixed-port"] = 0, ["tun"] = new { enable = false } },
        "/providers/proxies" or "/providers/rules" => new { providers = new { } },
        "/proxies" => new { proxies = Proxies() },
        "/rules" => new { rules = Array.Empty<object>() },
        "/traffic" => new { up = tick * 1024L, down = tick * 2048L },
        "/memory" => new { inuse = 32 * 1024 * 1024 },
        "/logs" => new { type = "info", payload = "isolated workload " + tick },
        "/connections" => new { uploadTotal = tick * 1024L * Rows, downloadTotal = tick * 2048L * Rows, memory = 32 * 1024 * 1024, connections = Connections(tick) },
        _ => new { }
    };
    private Dictionary<string, object> Proxies()
    {
        var names = Enumerable.Range(0, 128).Select(i => "Node-" + i).ToArray();
        var proxies = names.ToDictionary(name => name, name => (object)new { name, type = "Shadowsocks", udp = true, history = new[] { new { time = "2026-09-01T00:00:00Z", delay = 42 } } });
        for (var i = 0; i < 4; i++) proxies["Group-" + i] = new { name = "Group-" + i, type = "Selector", all = names, now = names[i], history = Array.Empty<object>() };
        return proxies;
    }
    private object[] Connections(int tick) => Enumerable.Range(0, Rows).Select(i => (object)new
    {
        id = "fixture-" + i, upload = (long)tick * (i + 1), download = (long)tick * (i + 1) * 2,
        start = "2026-09-01T00:00:00Z", chains = new[] { "Node-0", "Group-0" }, rule = "MATCH", rulePayload = "",
        metadata = new { network = "tcp", type = "HTTP", sourceIP = "192.0.2.1", sourcePort = "10000", destinationIP = "198.51.100.1", destinationPort = "443", host = "fixture.example", process = "fixture.exe" }
    }).ToArray();
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop();
        await _accept;
        await Task.WhenAll(_clients.ToArray()).WaitAsync(TimeSpan.FromSeconds(10));
        _stop.Dispose();
    }
}
