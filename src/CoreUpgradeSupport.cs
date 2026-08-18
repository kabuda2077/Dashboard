using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dashboard;

internal static class CoreUpgradeSupport
{
    private const int MaxCoreBackups = 3;
    private const int MaxReleaseRequestAttempts = 3;

    public static HttpClient CreateHttpClient()
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
        CancellationToken cancellationToken)
    {
        HostOperationLogger.Info("upgrade", $"Downloading release asset: {downloadUrl}");
        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
        HostOperationLogger.Info("upgrade", $"Downloaded release asset to {destinationPath} ({destination.Length} bytes).");
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
    {
        try
        {
            File.Copy(candidatePath, corePath, overwrite: true);
            HostOperationLogger.Info("upgrade", $"Replaced core binary: {corePath}");
        }
        catch (Exception replaceException)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                {
                    File.Copy(backupPath, corePath, overwrite: true);
                    HostOperationLogger.Error("upgrade", $"Core replacement failed and backup was restored: {backupPath}", replaceException);
                }
            }
            catch (Exception rollbackException)
            {
                throw new IOException(
                    $"内核替换失败，且从备份恢复失败。备份路径：{backupPath}",
                    new AggregateException(replaceException, rollbackException));
            }

            throw new IOException($"内核替换失败，已从备份恢复：{backupPath}", replaceException);
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
