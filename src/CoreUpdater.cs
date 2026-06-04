using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace MihomoDashboard;

public sealed record CoreUpgradeResult(string Version, string AssetName, string BackupPath);

public static class CoreUpdater
{
    private const string LatestReleaseApi = "https://api.github.com/repos/MetaCubeX/mihomo/releases/latest";
    private const int MaxCoreBackups = 3;

    public static async Task<CoreUpgradeResult> UpgradeLatestAsync(string corePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(corePath))
        {
            throw new InvalidOperationException("请先设置 mihomo 内核路径。");
        }

        if (!File.Exists(corePath))
        {
            throw new FileNotFoundException("找不到 mihomo 内核，请检查路径。", corePath);
        }

        using var client = CreateHttpClient();
        using var releaseResponse = await client.GetAsync(LatestReleaseApi, cancellationToken);
        releaseResponse.EnsureSuccessStatusCode();

        await using var releaseStream = await releaseResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(releaseStream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var version = root.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() ?? "latest" : "latest";
        var asset = FindWindowsX64Asset(root.GetProperty("assets"));

        var tempRoot = Path.Combine(Path.GetTempPath(), "MihomoDashboard", "core-upgrade", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var archivePath = Path.Combine(tempRoot, asset.Name);
            using (var assetResponse = await client.GetAsync(asset.DownloadUrl, cancellationToken))
            {
                assetResponse.EnsureSuccessStatusCode();
                await using var assetStream = await assetResponse.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = File.Create(archivePath);
                await assetStream.CopyToAsync(fileStream, cancellationToken);
            }

            await VerifyArchiveSha256Async(archivePath, asset.Sha256Digest, cancellationToken);
            var extractedCore = ExtractCoreExecutable(archivePath, tempRoot);
            var backupPath = BackupCore(corePath);
            File.Copy(extractedCore, corePath, overwrite: true);

            return new CoreUpgradeResult(version, asset.Name, backupPath);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MihomoDashboard", "1.0"));
        return client;
    }

    private static CoreAsset FindWindowsX64Asset(JsonElement assets)
    {
        var candidates = new List<CoreAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
            var sha256Digest = NormalizeSha256Digest(
                asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null);
            var normalizedName = name.ToLowerInvariant();

            if (!normalizedName.Contains("windows") ||
                !normalizedName.Contains("amd64") ||
                normalizedName.Contains("arm") ||
                string.IsNullOrWhiteSpace(downloadUrl))
            {
                continue;
            }

            if (!normalizedName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !normalizedName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            candidates.Add(new CoreAsset(name, downloadUrl, sha256Digest));
        }

        var selected = candidates
            .OrderBy(GetAssetPreference)
            .ThenByDescending(GetGoCompilerVersion)
            .ThenBy(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return selected ?? throw new InvalidOperationException("没有找到适用于 Windows x64 的 mihomo 发布文件。");
    }

    private static int GetAssetPreference(CoreAsset asset)
    {
        var name = asset.Name.ToLowerInvariant();
        if (name.Contains("amd64-v3-go125"))
        {
            return 0;
        }

        if (name.Contains("amd64-v3-go"))
        {
            return 1;
        }

        if (name.Contains("amd64-v3-"))
        {
            return 2;
        }

        if (name.Contains("compatible"))
        {
            return 8;
        }

        if (!name.Contains("amd64-v1-") &&
            !name.Contains("amd64-v2-") &&
            !name.Contains("amd64-v3-"))
        {
            return 3;
        }

        if (name.Contains("amd64-v2-go"))
        {
            return 4;
        }

        if (name.Contains("amd64-v2-"))
        {
            return 5;
        }

        if (name.Contains("amd64-v1-go"))
        {
            return 6;
        }

        if (name.Contains("amd64-v1-"))
        {
            return 7;
        }

        if (name.Contains("-go"))
        {
            return 9;
        }

        return 10;
    }

    private static int GetGoCompilerVersion(CoreAsset asset)
    {
        var name = asset.Name;
        var markerIndex = name.IndexOf("-go", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return 0;
        }

        var start = markerIndex + 3;
        var end = start;
        while (end < name.Length && char.IsDigit(name[end]))
        {
            end++;
        }

        return int.TryParse(name[start..end], out var version) ? version : 0;
    }

    private static string ExtractCoreExecutable(string archivePath, string tempRoot)
    {
        var extractRoot = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractRoot);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, extractRoot);
        }
        else if (archivePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            var outputPath = Path.Combine(extractRoot, "mihomo.exe");
            using var source = File.OpenRead(archivePath);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var output = File.Create(outputPath);
            gzip.CopyTo(output);
        }
        else
        {
            throw new InvalidOperationException("不支持的内核压缩包格式。");
        }

        var executable = Directory
            .EnumerateFiles(extractRoot, "*.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(path).Contains("mihomo", StringComparison.OrdinalIgnoreCase));

        return executable ?? throw new InvalidOperationException("压缩包中没有找到 mihomo.exe。");
    }

    private static string BackupCore(string corePath)
    {
        var backupDirectory = Path.Combine(Path.GetDirectoryName(corePath) ?? AppContext.BaseDirectory, "backups");
        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(
            backupDirectory,
            $"{Path.GetFileNameWithoutExtension(corePath)}-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(corePath)}.bak");

        File.Copy(corePath, backupPath, overwrite: false);
        PruneOldBackups(backupDirectory, corePath);
        return backupPath;
    }

    private static async Task VerifyArchiveSha256Async(string archivePath, string expectedSha256, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            throw new InvalidOperationException("Release asset is missing a SHA256 digest; core upgrade was cancelled.");
        }

        await using var stream = File.OpenRead(archivePath);
        var actualBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var actualSha256 = Convert.ToHexString(actualBytes).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded core archive SHA256 verification failed; core upgrade was cancelled.");
        }
    }

    private static string NormalizeSha256Digest(string? digest)
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
            }
            catch
            {
            }
        }
    }

    private sealed record CoreAsset(string Name, string DownloadUrl, string Sha256Digest);
}
