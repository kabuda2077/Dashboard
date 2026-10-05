using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dashboard;

internal static class CoreUpgradeSupport
{
    private const int MaxCoreBackups = 3;
    private const int MaxReleaseRequestAttempts = 3;

    // Shared across upgrade/update checks. HttpClient is thread-safe; creating one
    // per call leaks connection pools. Callers must not dispose it.
    public static HttpClient SharedClient { get; } = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Dashboard", "1.0"));
        return client;
    }

    public static async Task<JsonDocument> GetReleaseJsonAsync(
        HttpClient client,
        string requestUrl,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        cancellationToken = budget.Token;
        for (var attempt = 1; attempt <= MaxReleaseRequestAttempts; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(
                    requestUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (
                attempt < MaxReleaseRequestAttempts
                && !cancellationToken.IsCancellationRequested
                && IsTransientReleaseRequestFailure(ex))
            {
                HostOperationLogger.Info(
                    "update",
                    $"Release metadata request failed on attempt {attempt}/{MaxReleaseRequestAttempts}; retrying: {ex.Message}");
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("发布信息请求未返回结果。");
    }

    public static string CreateTempRoot(string operationName)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Dashboard", operationName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    public static async Task DownloadFileAsync(
        HttpClient client,
        string downloadUrl,
        string destinationPath,
        CancellationToken cancellationToken,
        TimeSpan? overallTimeout = null,
        TimeSpan? idleTimeout = null)
    {
        const long maximumBytes = 256L * 1024 * 1024;
        var idleLimit = idleTimeout ?? TimeSpan.FromSeconds(30);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(overallTimeout ?? TimeSpan.FromMinutes(5));
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
        idle.CancelAfter(idleLimit);
        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes) throw new IOException("Release archive exceeds the download limit.");
        await using var source = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        var buffer = new byte[65536];
        long total = 0;
        while (true)
        {
            idle.CancelAfter(idleLimit);
            var read = await source.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes) throw new IOException("Release archive exceeds the download limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), idle.Token).ConfigureAwait(false);
        }
        await destination.FlushAsync(budget.Token).ConfigureAwait(false);
        HostOperationLogger.Info("upgrade", $"Release asset downloaded ({total} bytes).");
    }

    public static string BackupCore(string corePath)
    {
        var backupDirectory = Path.Combine(Path.GetDirectoryName(corePath) ?? AppContext.BaseDirectory, "backups");
        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(
            backupDirectory,
            $"{Path.GetFileNameWithoutExtension(corePath)}-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(corePath)}.bak");

        File.Copy(corePath, backupPath, overwrite: false);
        HostOperationLogger.Info("upgrade", $"Created core backup: {backupPath}");
        PruneOldBackups(backupDirectory, corePath);
        return backupPath;
    }

    public static void ReplaceCoreWithRollback(string candidatePath, string corePath, string backupPath)
        => ReplaceCoreWithRollback(candidatePath, corePath, backupPath,
            (source, destination, backup) => File.Replace(source, destination, backup));

    // Stage on the destination volume: cancellation or process exit during the
    // copy must never truncate the installed executable. The commit/rollback
    // section deliberately does not accept cancellation.
    internal static void ReplaceCoreWithRollback(
        string candidatePath, string corePath, string backupPath,
        Action<string, string, string> replaceFile)
    {
        var stagedPath = corePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var rollbackPath = stagedPath + ".rollback";
        var retainRollback = false;
        try
        {
            using (var source = File.OpenRead(candidatePath))
            using (var staged = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(staged);
                staged.Flush(flushToDisk: true);
            }

            try
            {
                replaceFile(stagedPath, corePath, rollbackPath);
            }
            catch (Exception replaceException)
            {
                try
                {
                    // File.Replace can have moved the original to its backup
                    // before reporting an error. Restore that exact original,
                    // not a potentially older retained upgrade backup.
                    if (File.Exists(rollbackPath))
                    {
                        retainRollback = true;
                        File.Move(rollbackPath, corePath, overwrite: true);
                        retainRollback = false;
                    }
                    else if (!File.Exists(corePath))
                    {
                        // Recover a missing destination from the retained backup
                        // using the same staged, same-volume commit strategy.
                        File.Copy(backupPath, stagedPath, overwrite: true);
                        File.Move(stagedPath, corePath);
                    }
                }
                catch (Exception rollbackException)
                {
                    throw new IOException(
                        $"内核替换失败，且恢复失败。备份路径：{backupPath}；事务备份：{rollbackPath}",
                        new AggregateException(replaceException, rollbackException));
                }

                throw new IOException($"内核替换失败，原文件已保留或恢复。备份路径：{backupPath}", replaceException);
            }

            HostOperationLogger.Info("upgrade", $"Replaced core binary: {corePath}");
        }
        finally
        {
            DeleteUpgradeTempFile(stagedPath);
            if (!retainRollback) DeleteUpgradeTempFile(rollbackPath);
        }
    }

    private static void DeleteUpgradeTempFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex)
        {
            HostOperationLogger.Error("upgrade", $"Failed to remove upgrade staging file: {path}", ex);
        }
    }

    public static async Task<bool> HasSameFileHashAsync(
        string currentPath,
        string candidatePath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(currentPath).Length != new FileInfo(candidatePath).Length)
            {
                return false;
            }

            var currentHash = await ComputeFileSha256Async(currentPath, cancellationToken);
            var candidateHash = await ComputeFileSha256Async(candidatePath, cancellationToken);
            return string.Equals(currentHash, candidateHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }

    public static async Task VerifyArchiveSha256Async(
        string archivePath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            throw new InvalidOperationException("Release asset is missing a SHA256 digest; core upgrade was cancelled.");
        }

        var actualSha256 = await ComputeFileSha256Async(archivePath, cancellationToken);
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded core archive SHA256 verification failed; core upgrade was cancelled.");
        }

        HostOperationLogger.Info("upgrade", $"Verified SHA256 for {archivePath}: {actualSha256}");
    }

    public static string NormalizeSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return "";
        }

        var value = digest.Trim();
        const string prefix = "sha256:";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[prefix.Length..];
        }

        return value.Length == 64 && value.All(Uri.IsHexDigit)
            ? value.ToLowerInvariant()
            : "";
    }

    public static void DeleteDirectoryQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
                HostOperationLogger.Info("upgrade", $"Removed temporary upgrade directory: {directory}");
            }
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("upgrade", $"Failed to remove temporary upgrade directory: {directory}", ex);
        }
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void PruneOldBackups(string backupDirectory, string corePath)
    {
        var prefix = $"{Path.GetFileNameWithoutExtension(corePath)}-";
        var suffix = $"{Path.GetExtension(corePath)}.bak";
        var backups = Directory
            .EnumerateFiles(backupDirectory, $"{prefix}*{suffix}")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(MaxCoreBackups);

        foreach (var backup in backups)
        {
            try
            {
                File.Delete(backup);
                HostOperationLogger.Info("upgrade", $"Pruned old core backup: {backup}");
            }
            catch (Exception ex)
            {
                HostOperationLogger.Error("upgrade", $"Failed to prune old core backup: {backup}", ex);
            }
        }
    }

    private static bool IsTransientReleaseRequestFailure(Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: { } statusCode })
        {
            var status = (int)statusCode;
            return status is 408 or 429 || status >= 500;
        }

        return exception is HttpRequestException or IOException or JsonException;
    }
}
