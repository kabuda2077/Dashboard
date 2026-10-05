using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Dashboard;

public sealed class ProxyGroupIconCache
{
    private const int MaxIconBytes = 2 * 1024 * 1024;
    internal const int MaxCacheFiles = 256;
    internal const long MaxCacheBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);
    private static readonly HttpClient SharedClient = CreateHttpClient();
    private readonly HttpClient _client;
    private readonly TimeSpan _downloadTimeout;
    private readonly TimeSpan _refreshTimeout;

    private readonly Dictionary<string, string> _cachedFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _fileVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _sync = new();

    public ProxyGroupIconCache()
        : this(Path.Combine(AppSettings.ResourceDirectory, "icon-cache"))
    {
    }

    internal ProxyGroupIconCache(string cacheDirectory, HttpClient? client = null,
        TimeSpan? downloadTimeout = null, TimeSpan? refreshTimeout = null)
    {
        _client = client ?? SharedClient;
        _downloadTimeout = downloadTimeout ?? TimeSpan.FromSeconds(12);
        _refreshTimeout = refreshTimeout ?? TimeSpan.FromSeconds(90);
        CacheDirectory = cacheDirectory;
        Directory.CreateDirectory(CacheDirectory);
    }

    public event EventHandler? CacheChanged;

    public string CacheDirectory { get; }

    public IReadOnlyDictionary<string, string> GetDashboardMap(Uri dashboardUri)
    {
        lock (_sync)
        {
            return _cachedFiles.ToDictionary(
                item => item.Key,
                item => new Uri(dashboardUri, $"__mihomo/icon-cache/{Uri.EscapeDataString(item.Value)}?v={_fileVersions.GetValueOrDefault(item.Value):x}").ToString(),
                StringComparer.Ordinal);
        }
    }

    internal void ClearPublishedMap()
    {
        bool changed;
        lock (_sync)
        {
            changed = _cachedFiles.Count != 0;
            _cachedFiles.Clear();
            _fileVersions.Clear();
        }
        if (changed) CacheChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task LoadExistingAsync(
        string configPath,
        CancellationToken cancellationToken = default,
        Func<bool>? isCurrent = null)
    {
        // Keep disk scanning off the UI thread, but return ownership to the host.
        return Task.Run(() => ScanExistingCacheFiles(configPath, cancellationToken, isCurrent), cancellationToken);
    }

    private void ScanExistingCacheFiles(
        string configPath,
        CancellationToken cancellationToken,
        Func<bool>? isCurrent)
    {
        var iconUrls = ExtractProxyGroupIconUrls(configPath)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxCacheFiles)
            .ToArray();

        if (isCurrent?.Invoke() == false) return;
        cancellationToken.ThrowIfCancellationRequested();
        bool changed;
        lock (_sync)
        {
            if (isCurrent?.Invoke() == false) return;
            changed = _cachedFiles.Count > 0;
            _cachedFiles.Clear();
        }
        foreach (var iconUrl in iconUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                continue;
            }

            var fileName = GetCacheFileName(uri);
            if (!File.Exists(Path.Combine(CacheDirectory, fileName)))
            {
                continue;
            }

            if (isCurrent?.Invoke() == false) return;
            changed |= TryRecordCacheFile(iconUrl, fileName, isCurrent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (isCurrent?.Invoke() == false) return;
        PruneCache();
        if (changed)
        {
            CacheChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Records the icon under the URL as written in the config, and additionally
    // under its normalized absolute form when the two differ.
    //
    // Both keys are needed because ProxyIcon.vue looks up twice: first the icon
    // string verbatim, then `new URL(icon).href`. That second lookup is a plain
    // JavaScript property read, so it is case-sensitive and cannot fall back to
    // this dictionary's OrdinalIgnoreCase comparer. Storing only the raw key
    // would leave the normalized lookup unmatched whenever a config writes a
    // URL in non-normalized form.
    //
    // Returns true when the mapping changed.
    private bool TryRecordCacheFile(string iconUrl, string fileName, Func<bool>? isCurrent)
    {
        lock (_sync)
        {
            if (isCurrent?.Invoke() == false) return false;
            var stamp = File.GetLastWriteTimeUtc(Path.Combine(CacheDirectory, fileName)).Ticks;
            var versionChanged = !_fileVersions.TryGetValue(fileName, out var previousStamp) || previousStamp != stamp;
            _fileVersions[fileName] = stamp;
            var changed = RecordKey(iconUrl, fileName) || versionChanged;

            if (Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri)
                && !string.Equals(iconUrl, uri.AbsoluteUri, StringComparison.Ordinal))
            {
                changed |= RecordKey(uri.AbsoluteUri, fileName);
            }

            return changed;
        }
    }

    // Caller must hold _sync.
    private bool RecordKey(string key, string fileName)
    {
        if (_cachedFiles.TryGetValue(key, out var existingFileName)
            && string.Equals(existingFileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _cachedFiles[key] = fileName;
        return true;
    }

    public async Task RefreshAsync(
        string configPath,
        CancellationToken cancellationToken = default,
        Func<bool>? isCurrent = null)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        var changed = false;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(_refreshTimeout);
            var iconUrls = ExtractProxyGroupIconUrls(configPath).Distinct(StringComparer.Ordinal).Take(MaxCacheFiles).ToArray();
            var currentKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var iconUrl in iconUrls)
            {
                currentKeys.Add(iconUrl);
                if (Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri)) currentKeys.Add(uri.AbsoluteUri);
            }
            lock (_sync)
            {
                if (isCurrent?.Invoke() == false) return;
                foreach (var key in _cachedFiles.Keys.Where(key => !currentKeys.Contains(key)).ToArray())
                    changed |= _cachedFiles.Remove(key);
            }

            foreach (var iconUrl in iconUrls)
            {
                budget.Token.ThrowIfCancellationRequested();
                if (isCurrent?.Invoke() == false) return;
                string? fileName;
                try { fileName = await EnsureCachedAsync(iconUrl, budget.Token); }
                catch (OperationCanceledException) when (!budget.IsCancellationRequested) { continue; }
                catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException)
                { HostOperationLogger.Info("icon-cache", "An icon could not be cached; its remote fallback remains available."); continue; }
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                if (isCurrent?.Invoke() == false) return;
                changed |= TryRecordCacheFile(iconUrl, fileName, isCurrent);
            }

        }
        finally
        {
            try { changed |= PruneCache(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { HostOperationLogger.Info("icon-cache", "Cache pruning was unavailable."); }
            finally { _refreshLock.Release(); }
            if (changed && !cancellationToken.IsCancellationRequested && isCurrent?.Invoke() != false)
                CacheChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal bool PruneCache()
    {
        long retainedBytes = 0;
        HashSet<string> active;
        lock (_sync) active = _cachedFiles.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new DirectoryInfo(CacheDirectory).EnumerateFiles().OrderByDescending(file => file.LastWriteTimeUtc))
        {
            if (file.Extension != ".tmp" && retained.Count < MaxCacheFiles && retainedBytes + file.Length <= MaxCacheBytes
                && (active.Contains(file.Name) || DateTime.UtcNow - file.LastWriteTimeUtc < CacheLifetime))
            { retained.Add(file.Name); retainedBytes += file.Length; continue; }
            try { file.Delete(); } catch (IOException) { }
        }
        lock (_sync)
        {
            var removed = _cachedFiles.Where(item => !retained.Contains(item.Value)).Select(item => item.Key).ToArray();
            foreach (var key in removed) _cachedFiles.Remove(key);
            foreach (var key in _fileVersions.Keys.Where(key => !retained.Contains(key)).ToArray()) _fileVersions.Remove(key);
            return removed.Length > 0;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Dashboard", DashboardVersion.Current));
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
        if (File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < CacheLifetime)
        {
            return fileName;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_downloadTimeout);
        cancellationToken = budget.Token;
        using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxIconBytes)
        {
            return null;
        }

        var tempPath = cachePath + ".tmp";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(tempPath))
            {
                await CopyWithLimitAsync(source, target, MaxIconBytes, cancellationToken);
            }
            File.Move(tempPath, cachePath, overwrite: true);
            return fileName;
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
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
