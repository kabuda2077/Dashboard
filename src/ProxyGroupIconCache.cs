using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Dashboard;

public sealed class ProxyGroupIconCache
{
    private const int MaxIconBytes = 2 * 1024 * 1024;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly Dictionary<string, string> _cachedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _sync = new();

    public ProxyGroupIconCache()
    {
        Directory.CreateDirectory(CacheDirectory);
    }

    public event EventHandler? CacheChanged;

    public string CacheDirectory { get; } = Path.Combine(AppSettings.SettingsDirectory, "icon-cache");

    public IReadOnlyDictionary<string, string> GetDashboardMap(Uri dashboardUri)
    {
        lock (_sync)
        {
            return _cachedFiles.ToDictionary(
                item => item.Key,
                item => new Uri(dashboardUri, $"__mihomo/icon-cache/{Uri.EscapeDataString(item.Value)}").ToString(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void LoadExisting(string configPath)
    {
        // 异步加载图标缓存，不阻塞主线程
        _ = Task.Run(() => LoadExistingAsync(configPath));
    }

    private async Task LoadExistingAsync(string configPath)
    {
        var iconUrls = await Task.Run(() =>
            ExtractProxyGroupIconUrls(configPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        );

        if (iconUrls.Length == 0)
        {
            return;
        }

        // 并行检查文件存在性
        var tasks = iconUrls.Select(async iconUrl =>
        {
            if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                return null;
            }

            var fileName = GetCacheFileName(uri);
            var exists = await Task.Run(() =>
                File.Exists(Path.Combine(CacheDirectory, fileName)));

            return exists ? (iconUrl, uri, fileName) : ((string, Uri, string)?)null;
        });

        var results = await Task.WhenAll(tasks);

        lock (_sync)
        {
            foreach (var result in results)
            {
                if (result == null)
                {
                    continue;
                }

                var (iconUrl, uri, fileName) = result.Value;
                foreach (var key in GetCacheKeys(iconUrl, uri))
                {
                    _cachedFiles[key] = fileName;
                }
            }
        }
    }

    public async Task RefreshAsync(string configPath, CancellationToken cancellationToken = default)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var iconUrls = ExtractProxyGroupIconUrls(configPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var changed = false;

            foreach (var iconUrl in iconUrls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = await EnsureCachedAsync(iconUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                lock (_sync)
                {
                    var iconUri = new Uri(iconUrl);
                    foreach (var key in GetCacheKeys(iconUrl, iconUri))
                    {
                        if (!_cachedFiles.TryGetValue(key, out var existingFileName)
                            || !string.Equals(existingFileName, fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            _cachedFiles[key] = fileName;
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                CacheChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Dashboard", "1.0"));
        return client;
    }

    private async Task<string?> EnsureCachedAsync(string iconUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var fileName = GetCacheFileName(uri);
        var cachePath = Path.Combine(CacheDirectory, fileName);
        if (File.Exists(cachePath))
        {
            return fileName;
        }

        using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxIconBytes)
        {
            return null;
        }

        var tempPath = cachePath + ".tmp";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(tempPath))
        {
            await CopyWithLimitAsync(source, target, MaxIconBytes, cancellationToken);
        }

        File.Move(tempPath, cachePath, overwrite: true);
        return fileName;
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream target, int maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException("Icon file is too large.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string GetCacheFileName(Uri iconUri)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(iconUri.AbsoluteUri))).ToLowerInvariant();
        return hash + GetSafeExtension(iconUri);
    }

    private static IEnumerable<string> GetCacheKeys(string iconUrl, Uri iconUri)
    {
        yield return iconUrl;

        var absoluteUri = iconUri.AbsoluteUri;
        if (!string.Equals(iconUrl, absoluteUri, StringComparison.OrdinalIgnoreCase))
        {
            yield return absoluteUri;
        }

        var originalString = iconUri.OriginalString;
        if (!string.Equals(iconUrl, originalString, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(absoluteUri, originalString, StringComparison.OrdinalIgnoreCase))
        {
            yield return originalString;
        }
    }

    private static string GetSafeExtension(Uri iconUri)
    {
        var extension = Path.GetExtension(iconUri.AbsolutePath).ToLowerInvariant();
        return extension is ".svg" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".ico" or ".avif"
            ? extension
            : ".img";
    }

    private static IEnumerable<string> ExtractProxyGroupIconUrls(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            yield break;
        }

        var inProxyGroups = false;
        var proxyGroupsIndent = 0;

        foreach (var rawLine in File.ReadLines(configPath))
        {
            var withoutComment = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(withoutComment))
            {
                continue;
            }

            var indent = CountIndent(withoutComment);
            var line = withoutComment.Trim();

            if (inProxyGroups && indent <= proxyGroupsIndent)
            {
                inProxyGroups = false;
            }

            if (!inProxyGroups && line.Equals("proxy-groups:", StringComparison.OrdinalIgnoreCase))
            {
                inProxyGroups = true;
                proxyGroupsIndent = indent;
                continue;
            }

            if (!inProxyGroups || !TryReadYamlValue(line, "icon", out var iconValue))
            {
                continue;
            }

            if (Uri.TryCreate(iconValue, UriKind.Absolute, out var iconUri)
                && iconUri.Scheme is "http" or "https")
            {
                yield return iconUri.AbsoluteUri;
            }
        }
    }

    private static bool TryReadYamlValue(string line, string key, out string value)
    {
        value = "";
        var prefix = key + ":";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = line[prefix.Length..].Trim().Trim('"', '\'');
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string StripComment(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (current == '#' && !inSingleQuote && !inDoubleQuote)
            {
                return line[..i];
            }
        }

        return line;
    }

    private static int CountIndent(string line)
    {
        var count = 0;
        while (count < line.Length && char.IsWhiteSpace(line[count]))
        {
            count++;
        }

        return count;
    }
}
