using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MihomoDashboard;

public sealed class DashboardServer : IDisposable
{
    private readonly string _root;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public DashboardServer(string root)
    {
        _root = root;
    }

    public Uri Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenAsync(_cts.Token));
        return new Uri($"http://127.0.0.1:{port}/");
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
                _ = Task.Run(() => HandleAsync(client), token);
            }
            catch when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        try
        {
            var requestLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
            {
            }

            var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var rawPath = parts.Length > 1 ? parts[1] : "/";
            var requestPath = Uri.UnescapeDataString(rawPath.Split('?', 2)[0].TrimStart('/'));
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                requestPath = "index.html";
            }

            var candidate = Path.GetFullPath(Path.Combine(_root, requestPath.Replace('/', Path.DirectorySeparatorChar)));
            var rootFullPath = Path.GetFullPath(_root);
            if (!IsPathUnderRoot(candidate, rootFullPath) || !File.Exists(candidate))
            {
                candidate = Path.Combine(rootFullPath, "index.html");
            }

            var bytes = await File.ReadAllBytesAsync(candidate);
            await WriteResponseAsync(stream, "200 OK", GetContentType(Path.GetExtension(candidate)), bytes);
        }
        catch
        {
            await WriteResponseAsync(stream, "500 Internal Server Error", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Internal Server Error"));
        }
    }

    private static async Task WriteResponseAsync(Stream stream, string status, string contentType, byte[] body)
    {
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
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
