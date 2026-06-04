using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Dashboard;

public sealed class DashboardServer : IDisposable
{
    private const int PreferredPort = 33291;
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(5);

    private readonly string _root;
    private readonly string _iconCacheRoot;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public DashboardServer(string root, string iconCacheRoot)
    {
        _root = root;
        _iconCacheRoot = iconCacheRoot;
    }

    public Uri Start()
    {
        _listener = StartListener(PreferredPort);
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenAsync(_cts.Token));
        return new Uri($"http://127.0.0.1:{port}/");
    }

    private static TcpListener StartListener(int preferredPort)
    {
        var listener = new TcpListener(IPAddress.Loopback, preferredPort);
        try
        {
            listener.Start();
            return listener;
        }
        catch
        {
            listener.Stop();
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return listener;
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        if (_listener is null)
        {
            return;
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleAsync(client, token), token);
            }
            catch when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dashboard server accept failed: {ex.Message}");
            }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using var ownedClient = client;
        ownedClient.ReceiveTimeout = (int)RequestReadTimeout.TotalMilliseconds;
        await using var stream = ownedClient.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        try
        {
            var requestLine = await ReadLineWithTimeoutAsync(reader, token);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            while (!string.IsNullOrEmpty(await ReadLineWithTimeoutAsync(reader, token)))
            {
            }

            var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var rawPath = parts.Length > 1 ? parts[1] : "/";
            var requestPath = Uri.UnescapeDataString(rawPath.Split('?', 2)[0].TrimStart('/'));
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                requestPath = "index.html";
            }

            if (IsIconCacheRequest(requestPath))
            {
                if (TryGetIconCacheFile(requestPath, out var iconCacheFile))
                {
                    var iconBytes = await File.ReadAllBytesAsync(iconCacheFile, token);
                    await WriteResponseAsync(stream, "200 OK", GetContentType(Path.GetExtension(iconCacheFile)), iconBytes, "public, max-age=604800");
                }
                else
                {
                    await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"));
                }
                return;
            }

            var candidate = Path.GetFullPath(Path.Combine(_root, requestPath.Replace('/', Path.DirectorySeparatorChar)));
            var rootFullPath = Path.GetFullPath(_root);
            if (!IsPathUnderRoot(candidate, rootFullPath) || !File.Exists(candidate))
            {
                candidate = Path.Combine(rootFullPath, "index.html");
            }

            var bytes = await File.ReadAllBytesAsync(candidate, token);
            await WriteResponseAsync(stream, "200 OK", GetContentType(Path.GetExtension(candidate)), bytes);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch
        {
            try
            {
                await WriteResponseAsync(stream, "500 Internal Server Error", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Internal Server Error"));
            }
            catch
            {
            }
        }
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(RequestReadTimeout);

        try
        {
            return await reader.ReadLineAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return null;
        }
    }

    private bool TryGetIconCacheFile(string requestPath, out string iconCacheFile)
    {
        iconCacheFile = "";
        if (!IsIconCacheRequest(requestPath))
        {
            return false;
        }

        var fileName = Path.GetFileName(Uri.UnescapeDataString(requestPath[IconCacheRequestPrefix.Length..]));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_iconCacheRoot, fileName));
        var rootFullPath = Path.GetFullPath(_iconCacheRoot);
        if (!IsPathUnderRoot(candidate, rootFullPath) || !File.Exists(candidate))
        {
            return false;
        }

        iconCacheFile = candidate;
        return true;
    }

    private const string IconCacheRequestPrefix = "__mihomo/icon-cache/";

    private static bool IsIconCacheRequest(string requestPath)
    {
        return requestPath.StartsWith(IconCacheRequestPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteResponseAsync(Stream stream, string status, string contentType, byte[] body, string cacheControl = "no-cache")
    {
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: {cacheControl}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(body);
    }

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
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

    private static bool IsPathUnderRoot(string candidate, string rootFullPath)
    {
        var normalizedRoot = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;

        return candidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }
}
