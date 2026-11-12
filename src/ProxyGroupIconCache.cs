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
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly HttpClient SharedClient = CreateHttpClient();
    private readonly HttpClient _client;
    private readonly TimeSpan _downloadTimeout;
    private readonly TimeSpan _refreshTimeout;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, DateTimeOffset> _retryAfter = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeUrls = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _cachedFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _fileVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _sync = new();

    public ProxyGroupIconCache()
        : this(Path.Combine(AppSettings.ResourceDirectory, "icon-cache"))
    {
    }

    internal ProxyGroupIconCache(string cacheDirectory, HttpClient? client = null,
        TimeSpan? downloadTimeout = null, TimeSpan? refreshTimeout = null, TimeProvider? timeProvider = null)
    {
        _client = client ?? SharedClient;
        _downloadTimeout = downloadTimeout ?? TimeSpan.FromSeconds(12);
        _refreshTimeout = refreshTimeout ?? TimeSpan.FromSeconds(90);
        _timeProvider = timeProvider ?? TimeProvider.System;
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
            _retryAfter.Clear();
            _activeUrls.Clear();
        }
        if (changed) CacheChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task LoadExistingAsync(
        IEnumerable<string> iconUrls,
        CancellationToken cancellationToken = default,
        Func<bool>? isCurrent = null)
    {
        // Serialize scanning/pruning with downloads so a new list cannot remove an active temporary file.
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() => ScanExistingCacheFiles(iconUrls, cancellationToken, isCurrent), cancellationToken).ConfigureAwait(false);
        }
        finally { _refreshLock.Release(); }
    }

    private void ScanExistingCacheFiles(
        IEnumerable<string> iconUrls,
        CancellationToken cancellationToken,
        Func<bool>? isCurrent)
    {
        var urls = NormalizeIconUrls(iconUrls);
        cancellationToken.ThrowIfCancellationRequested();
        if (isCurrent?.Invoke() == false) return;
        PublishIfChanged(SetActiveUrls(urls, isCurrent), cancellationToken, isCurrent);
        foreach (var iconUrl in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isCurrent?.Invoke() == false) return;
            RecordExistingFile(iconUrl, cancellationToken, isCurrent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (isCurrent?.Invoke() == false) return;
        PublishIfChanged(PruneCache(cancellationToken, isCurrent), cancellationToken, isCurrent);
    }

    private static string[] NormalizeIconUrls(IEnumerable<string> iconUrls) => iconUrls
        .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri : null)
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .Take(MaxCacheFiles)
        .ToArray();

    private bool SetActiveUrls(string[] iconUrls, Func<bool>? isCurrent)
    {
        var active = iconUrls.ToHashSet(StringComparer.Ordinal);
        lock (_sync)
        {
            if (isCurrent?.Invoke() == false) return false;
            _activeUrls.Clear();
            _activeUrls.UnionWith(active);
            var removed = _cachedFiles.Keys.Where(key => !active.Contains(key)).ToArray();
            foreach (var key in removed) _cachedFiles.Remove(key);
            foreach (var key in _retryAfter.Keys.Where(key => !active.Contains(key)).ToArray()) _retryAfter.Remove(key);
            return removed.Length > 0;
        }
    }

    private void RecordExistingFile(string iconUrl, CancellationToken cancellationToken, Func<bool>? isCurrent)
    {
        var fileName = GetCacheFileName(new Uri(iconUrl));
        if (File.Exists(Path.Combine(CacheDirectory, fileName)))
            PublishIfChanged(TryRecordCacheFile(iconUrl, fileName, isCurrent), cancellationToken, isCurrent);
    }

    private void PublishIfChanged(bool changed, CancellationToken cancellationToken, Func<bool>? isCurrent)
    {
        if (changed && !cancellationToken.IsCancellationRequested && isCurrent?.Invoke() != false)
            CacheChanged?.Invoke(this, EventArgs.Empty);
    }

    // URL keys are canonical on both cache input and ProxyIcon lookup.
    private bool TryRecordCacheFile(string iconUrl, string fileName, Func<bool>? isCurrent)
    {
        lock (_sync)
        {
            if (isCurrent?.Invoke() == false || !_activeUrls.Contains(iconUrl)) return false;
            var stamp = File.GetLastWriteTimeUtc(Path.Combine(CacheDirectory, fileName)).Ticks;
            var versionChanged = !_fileVersions.TryGetValue(fileName, out var previousStamp) || previousStamp != stamp;
            _fileVersions[fileName] = stamp;
            return RecordKey(iconUrl, fileName) || versionChanged;
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
        IEnumerable<string> iconUrls,
        CancellationToken cancellationToken = default,
        Func<bool>? isCurrent = null)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(_refreshTimeout);
            var urls = NormalizeIconUrls(iconUrls);
            budget.Token.ThrowIfCancellationRequested();
            if (isCurrent?.Invoke() == false) return;
            PublishIfChanged(SetActiveUrls(urls, isCurrent), cancellationToken, isCurrent);

            foreach (var iconUrl in urls)
            {
                budget.Token.ThrowIfCancellationRequested();
                if (isCurrent?.Invoke() == false) return;
                // Publish a stale file before trying the network, and retain it on failure.
                RecordExistingFile(iconUrl, cancellationToken, isCurrent);
                lock (_sync)
                {
                    if (_retryAfter.TryGetValue(iconUrl, out var retryAfter) && _timeProvider.GetUtcNow() < retryAfter)
                        continue;
                }

                string? fileName;
                try { fileName = await EnsureCachedAsync(iconUrl, budget.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    RecordFailure(iconUrl, isCurrent);
                    budget.Token.ThrowIfCancellationRequested();
                    continue;
                }
                catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException)
                {
                    budget.Token.ThrowIfCancellationRequested();
                    RecordFailure(iconUrl, isCurrent);
                    HostOperationLogger.Info("icon-cache", "An icon could not be refreshed; any cached copy remains available.");
                    continue;
                }
                budget.Token.ThrowIfCancellationRequested();
                if (isCurrent?.Invoke() == false) return;
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    RecordFailure(iconUrl, isCurrent);
                    continue;
                }

                lock (_sync) _retryAfter.Remove(iconUrl);
                PublishIfChanged(TryRecordCacheFile(iconUrl, fileName, isCurrent), cancellationToken, isCurrent);
            }
        }
        finally
        {
            var changed = false;
            try { changed = PruneCache(cancellationToken, isCurrent); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { HostOperationLogger.Info("icon-cache", "Cache pruning was unavailable."); }
            finally { _refreshLock.Release(); }
            PublishIfChanged(changed, cancellationToken, isCurrent);
        }
    }

    private void RecordFailure(string iconUrl, Func<bool>? isCurrent)
    {
        lock (_sync)
        {
            if (isCurrent?.Invoke() == false || !_activeUrls.Contains(iconUrl)) return;
            _retryAfter[iconUrl] = _timeProvider.GetUtcNow() + FailureRetryDelay;
        }
    }

    internal bool PruneCache(CancellationToken cancellationToken = default, Func<bool>? isCurrent = null)
    {
        long retainedBytes = 0;
        HashSet<string> active;
        lock (_sync)
        {
            // ClearPublishedMap shares this lock: either cancellation wins, or pruning
            // retains the old session's fallback files in this snapshot before reset.
            if (cancellationToken.IsCancellationRequested || isCurrent?.Invoke() == false) return false;
            active = _cachedFiles.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new DirectoryInfo(CacheDirectory).EnumerateFiles()
            .OrderByDescending(file => active.Contains(file.Name))
            .ThenByDescending(file => file.LastWriteTimeUtc))
        {
            if (file.Extension != ".tmp" && retained.Count < MaxCacheFiles && retainedBytes + file.Length <= MaxCacheBytes
                && (active.Contains(file.Name) || _timeProvider.GetUtcNow().UtcDateTime - file.LastWriteTimeUtc < CacheLifetime))
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
        if (File.Exists(cachePath) && _timeProvider.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(cachePath) < CacheLifetime)
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
            cancellationToken.ThrowIfCancellationRequested();
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

}
