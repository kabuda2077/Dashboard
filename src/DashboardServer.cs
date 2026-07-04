using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Dashboard;

public sealed class DashboardServer : IDisposable
{
    private const int PreferredPort = 33291;
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(5);

    private readonly string _rootFullPath;
    private readonly string _iconCacheRootFullPath;
    private readonly ConcurrentDictionary<string, CachedFile> _fileCache = new();
    private readonly ConcurrentDictionary<string, CachedFile> _iconFileCache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _firstRequestLogged;

    private sealed class CachedFile
    {
        public required byte[] Content { get; init; }
        public required string ContentType { get; init; }
        public required DateTime CachedAt { get; init; }
        public required DateTime LastWriteTimeUtc { get; init; }
        public required long Length { get; init; }
        public required string ETag { get; init; }
    }

    public DashboardServer(string root, string iconCacheRoot)
    {
        _rootFullPath = Path.GetFullPath(root);
        _iconCacheRootFullPath = Path.GetFullPath(iconCacheRoot);
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
                HostOperationLogger.Error("dashboard-server", "Dashboard server accept failed.", ex);
            }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        var requestStartedAt = Stopwatch.GetTimestamp();
        var requestPathForLog = "";
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

            string? ifNoneMatch = null;
            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await ReadLineWithTimeoutAsync(reader, token)))
            {
                if (headerLine.StartsWith("If-None-Match:", StringComparison.OrdinalIgnoreCase))
                {
                    ifNoneMatch = headerLine["If-None-Match:".Length..].Trim();
                }
            }

            var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var rawPath = parts.Length > 1 ? parts[1] : "/";
            requestPathForLog = rawPath;
            var requestPath = Uri.UnescapeDataString(rawPath.Split('?', 2)[0].TrimStart('/'));
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                requestPath = "index.html";
            }

            if (IsIconCacheRequest(requestPath))
            {
                if (TryGetIconCacheFile(requestPath, out var iconCacheFile))
                {
                    var cachedIcon = await ReadCachedFileAsync(_iconFileCache, iconCacheFile, token);
                    const string iconCacheControl = "public, max-age=604800, immutable";
                    if (ETagMatches(ifNoneMatch, cachedIcon.ETag))
                    {
                        await WriteNotModifiedAsync(stream, iconCacheControl, cachedIcon.ETag);
                    }
                    else
                    {
                        await WriteResponseAsync(stream, "200 OK", cachedIcon.ContentType, cachedIcon.Content, iconCacheControl, cachedIcon.ETag);
                    }
                }
                else
                {
                    await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"));
                }
                return;
            }

            var candidate = Path.GetFullPath(Path.Combine(_rootFullPath, requestPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsPathUnderRoot(candidate, _rootFullPath) || !File.Exists(candidate))
            {
                candidate = Path.Combine(_rootFullPath, "index.html");
            }

            var staticFile = await ReadStaticFileAsync(candidate, token);
            var cacheControl = GetStaticCacheControl(candidate);
            if (ETagMatches(ifNoneMatch, staticFile.ETag))
            {
                await WriteNotModifiedAsync(stream, cacheControl, staticFile.ETag);
                return;
            }

            await WriteResponseAsync(stream, "200 OK", staticFile.ContentType, staticFile.Content, cacheControl, staticFile.ETag);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("dashboard-server", "Dashboard server request handling failed.", ex);
            try
            {
                await WriteResponseAsync(stream, "500 Internal Server Error", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Internal Server Error"));
            }
            catch
            {
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(requestPathForLog)
                && Interlocked.Exchange(ref _firstRequestLogged, 1) == 0)
            {
                HostOperationLogger.Info(
                    "performance",
                    $"dashboard-server:firstRequest path={requestPathForLog} durationMs={Stopwatch.GetElapsedTime(requestStartedAt).TotalMilliseconds:0}");
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

        var candidate = Path.GetFullPath(Path.Combine(_iconCacheRootFullPath, fileName));
        if (!IsPathUnderRoot(candidate, _iconCacheRootFullPath) || !File.Exists(candidate))
        {
            return false;
        }

        iconCacheFile = candidate;
        return true;
    }

    private const string IconCacheRequestPrefix = "__mihomo/icon-cache/";

    private async Task<CachedFile> ReadStaticFileAsync(string path, CancellationToken token)
    {
        var fileInfo = new FileInfo(path);
        var etag = BuildETag(fileInfo);
        if (_fileCache.TryGetValue(path, out var cached))
        {
            var cacheAge = DateTime.UtcNow - cached.CachedAt;
            if (cacheAge < _cacheExpiration
                && cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc
                && cached.Length == fileInfo.Length)
            {
                return cached;
            }

            _fileCache.TryRemove(path, out _);
        }

        var file = await ReadFileAsync(path, fileInfo, etag, token);
        if (ShouldCacheStaticFile(path, file.Content.Length))
        {
            _fileCache[path] = file;
        }

        return file;
    }

    private static async Task<CachedFile> ReadCachedFileAsync(
        ConcurrentDictionary<string, CachedFile> cache,
        string path,
        CancellationToken token)
    {
        var fileInfo = new FileInfo(path);
        if (cache.TryGetValue(path, out var cached)
            && cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc
            && cached.Length == fileInfo.Length)
        {
            return cached;
        }

        var file = await ReadFileAsync(path, fileInfo, BuildETag(fileInfo), token);
        cache[path] = file;
        return file;
    }

    private static async Task<CachedFile> ReadFileAsync(
        string path,
        FileInfo fileInfo,
        string etag,
        CancellationToken token)
    {
        return new CachedFile
        {
            Content = await File.ReadAllBytesAsync(path, token),
            ContentType = GetContentType(fileInfo.Extension),
            CachedAt = DateTime.UtcNow,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            Length = fileInfo.Length,
            ETag = etag
        };
    }

    private static string BuildETag(FileInfo fileInfo)
    {
        return $"\"{fileInfo.Length:x}-{fileInfo.LastWriteTimeUtc.Ticks:x}\"";
    }

    private static bool ETagMatches(string? ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        return ifNoneMatch
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(value => string.Equals(value, etag, StringComparison.Ordinal));
    }

    private static bool ShouldCacheStaticFile(string path, int byteLength)
    {
        if (byteLength >= 1024 * 1024)
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        return !fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("sw.js", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("registerSW.js", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIconCacheRequest(string requestPath)
    {
        return requestPath.StartsWith(IconCacheRequestPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStaticCacheControl(string path)
    {
        if (IsHashedAsset(path))
        {
            return "public, max-age=31536000, immutable";
        }

        return "no-cache";
    }

    private static bool IsHashedAsset(string path)
    {
        var directory = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
        var fileName = Path.GetFileNameWithoutExtension(path);
        return directory.Equals("assets", StringComparison.OrdinalIgnoreCase)
            && fileName.Contains('-', StringComparison.Ordinal);
    }

    private static async Task WriteNotModifiedAsync(Stream stream, string cacheControl, string etag)
    {
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 304 Not Modified\r\nContent-Length: 0\r\nCache-Control: {cacheControl}\r\nETag: {etag}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
    }

    private static async Task WriteResponseAsync(Stream stream, string status, string contentType, byte[] body, string cacheControl = "no-cache", string? etag = null)
    {
        var etagHeader = string.IsNullOrWhiteSpace(etag) ? "" : $"ETag: {etag}\r\n";
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: {cacheControl}\r\n{etagHeader}Connection: close\r\n\r\n");
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
        _fileCache.Clear();
        _iconFileCache.Clear();
    }
}
