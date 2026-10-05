using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Dashboard;

internal sealed class DashboardOriginUnavailableException(int port, Exception error)
    : InvalidOperationException($"Dashboard 所需的本地端口 {port} 不可用。请关闭占用它的程序后重试。", error);

public sealed class DashboardServer : IDisposable
{
    internal const int DashboardPort = 33291;
    internal static readonly Uri DashboardOrigin = new($"http://127.0.0.1:{DashboardPort}/");
    private readonly string _root;
    private readonly string _icons;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _clients = new(32, 32);
    private readonly ConcurrentDictionary<long, Task> _requests = new();
    private TcpListener? _listener;
    private Task? _listenTask;
    private long _requestId;
    private int _disposed;
    private int _firstRequest;

    public DashboardServer(string root, string iconCacheRoot)
    {
        _root = Path.GetFullPath(root);
        _icons = Path.GetFullPath(iconCacheRoot);
    }
    public Uri Start() => StartCore(false);
    internal Uri StartForTests(int testPort = 0) => StartCore(true, testPort);
    private Uri StartCore(bool ephemeral, int testPort = 0)
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(DashboardServer));
        TcpListener Bind(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try { listener.Start(); return listener; }
            catch { listener.Stop(); throw; }
        }
        _listener = ephemeral ? Bind(testPort) : StartListener(Bind);
        _listenTask = ListenAsync(_lifetime.Token);
        return new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
    }
    internal static TcpListener StartListener(Func<int, TcpListener> bind)
    {
        try { return bind(DashboardPort); }
        catch (SocketException error) { throw new DashboardOriginUnavailableException(DashboardPort, error); }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                if (!_clients.Wait(0)) { client.Dispose(); continue; }
                var id = Interlocked.Increment(ref _requestId);
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _requests[id] = completion.Task;
                _ = ServeAndCompleteAsync(client, id, completion, token);
            }
            catch (Exception) when (token.IsCancellationRequested) { break; }
            catch (Exception error) { HostOperationLogger.Error("dashboard-server", "Local request accept failed.", error); }
        }
    }

    private async Task ServeAndCompleteAsync(TcpClient client, long id, TaskCompletionSource completion, CancellationToken token)
    {
        try { await ServeAsync(client, token).ConfigureAwait(false); }
        catch (Exception error) { HostOperationLogger.Error("dashboard-server", "Local resource request failed.", error); }
        finally
        {
            client.Dispose();
            _clients.Release();
            _requests.TryRemove(id, out _);
            completion.TrySetResult();
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var requestToken = deadline.Token;
        var started = Stopwatch.GetTimestamp();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
        try
        {
            var line = await ReadLineAsync(reader, 8192, requestToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) return;
            var parts = line.Split(' ', 3);
            if (parts.Length != 3 || !parts[1].StartsWith('/') || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            { await ErrorAsync(stream, "400 Bad Request", requestToken); return; }
            if (parts[0] is not ("GET" or "HEAD")) { await ErrorAsync(stream, "405 Method Not Allowed", requestToken); return; }
            string? etag = null;
            var headerBytes = 0;
            var headerCount = 0;
            while (true)
            {
                var header = await ReadLineAsync(reader, 8192, requestToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(header)) break;
                headerBytes += header.Length;
                if (++headerCount > 64 || headerBytes > 32768) { await ErrorAsync(stream, "431 Request Header Fields Too Large", requestToken); return; }
                if (header.StartsWith("If-None-Match:", StringComparison.OrdinalIgnoreCase)) etag = header[14..].Trim();
            }
            var relative = Uri.UnescapeDataString(parts[1].Split('?', 2)[0]).TrimStart('/');
            if (relative.Length == 0) relative = "index.html";
            const string iconPrefix = "__mihomo/icon-cache/";
            var icon = relative.StartsWith(iconPrefix, StringComparison.Ordinal);
            var root = icon ? _icons : _root;
            if (icon) relative = relative[iconPrefix.Length..];
            var file = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)
                || HasResourceLink(root, file))
            { await ErrorAsync(stream, "404 Not Found", requestToken); return; }
            var info = new FileInfo(file);
            var currentEtag = $"\"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\"";
            var cache = icon ? "public, max-age=604800, immutable" : IsHashedAsset(file) ? "public, max-age=31536000, immutable" : "no-cache";
            if (etag?.Split(',', StringSplitOptions.TrimEntries).Contains(currentEtag) == true)
            { await HeaderAsync(stream, "304 Not Modified", "application/octet-stream", 0, cache, currentEtag, requestToken); return; }
            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            await HeaderAsync(stream, "200 OK", ContentType(info.Extension), source.Length, cache, currentEtag, requestToken);
            if (parts[0] != "HEAD") await source.CopyToAsync(stream, requestToken).ConfigureAwait(false);
            // Browser HTTP cache and the OS file cache own caching; do not duplicate all assets in host RAM.
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested) { }
        catch (IOException) { }
        catch (Exception error) when (error is ArgumentException or UriFormatException)
        { try { await ErrorAsync(stream, "400 Bad Request", requestToken); } catch { } }
        finally
        {
            if (Interlocked.Exchange(ref _firstRequest, 1) == 0)
                HostOperationLogger.Diagnostic("performance", $"dashboard-server:firstRequest durationMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0}");
        }
    }

    private static bool HasResourceLink(string root, string path)
    {
        var boundary = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Lexical '..' checks alone do not contain Windows junctions or symlinks.
        // Packaged resources do not need links below their configured root.
        for (var current = path; !string.Equals(current, boundary, StringComparison.OrdinalIgnoreCase);
            current = Path.GetDirectoryName(current)!)
        {
            if (string.IsNullOrEmpty(current) || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;
        }
        return false;
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, int maximum, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) != 0)
        {
            if (buffer[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length >= maximum) throw new IOException("Local request line exceeds its limit.");
            text.Append(buffer[0]);
        }
        return text.Length == 0 ? null : text.ToString();
    }
    private static bool IsHashedAsset(string path) => Path.GetFileName(Path.GetDirectoryName(path)) == "assets"
        && Path.GetFileNameWithoutExtension(path).Contains('-');
    private static Task ErrorAsync(Stream stream, string status, CancellationToken token) =>
        HeaderAsync(stream, status, "text/plain; charset=utf-8", 0, "no-store", null, token);
    private static async Task HeaderAsync(Stream stream, string status, string type, long length, string cache, string? etag, CancellationToken token)
    {
        var header = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {length}\r\nCache-Control: {cache}\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n";
        if (etag is not null) header += $"ETag: {etag}\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header + "\r\n"), token).ConfigureAwait(false);
    }
    private static string ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" or ".webmanifest" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".avif" => "image/avif",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream"
    };

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel(); _listener?.Stop();
        try
        {
            if (_listenTask is not null) await _listenTask.ConfigureAwait(false);
            await Task.WhenAll(_requests.Values).WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false);
            _lifetime.Dispose(); _clients.Dispose();
        }
        catch (Exception error) { HostOperationLogger.Error("dashboard-server", "Resource requests did not drain.", error); }
    }
    public void Dispose() { _ = StopAsync(); }
}
