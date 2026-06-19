using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard;

public static class SingBoxUpdater
{
    private const string ReleasesApi = "https://api.github.com/repos/reF1nd/sing-box-releases/releases?per_page=50";
    private const int MaxCoreBackups = 3;

    public static async Task<CoreUpgradeResult> UpgradeLatestAsync(
        string corePath,
        Action? beforeReplace = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(corePath))
        {
            throw new InvalidOperationException("请先设置 sing-box 内核路径。");
        }

        if (!File.Exists(corePath))
        {
            throw new FileNotFoundException("找不到 sing-box 内核，请检查路径。", corePath);
        }

        var installedVersion = await GetInstalledVersionAsync(corePath, cancellationToken);
        using var client = CreateHttpClient();
        using var releaseResponse = await client.GetAsync(ReleasesApi, cancellationToken);
        releaseResponse.EnsureSuccessStatusCode();

        await using var releaseStream = await releaseResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(releaseStream, cancellationToken: cancellationToken);
        var release = FindMatchingRelease(document.RootElement, installedVersion);
        var version = release.GetProperty("tag_name").GetString() ?? "latest";
        var asset = FindWindowsAmd64V3Asset(release.GetProperty("assets"));

        if (IsSameVersion(installedVersion, version))
        {
            return new CoreUpgradeResult(version, asset.Name, "", IsAlreadyLatest: true);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "Dashboard", "sing-box-upgrade", Guid.NewGuid().ToString("N"));
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

            var extractedCore = ExtractCoreExecutable(archivePath, tempRoot);
            if (await HasSameFileHashAsync(corePath, extractedCore, cancellationToken))
            {
                return new CoreUpgradeResult(version, asset.Name, "", IsAlreadyLatest: true);
            }

            beforeReplace?.Invoke();
            var backupPath = BackupCore(corePath);
            File.Copy(extractedCore, corePath, overwrite: true);

            return new CoreUpgradeResult(version, asset.Name, backupPath, IsAlreadyLatest: false);
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

    private static JsonElement FindMatchingRelease(JsonElement releases, string installedVersion)
    {
        var wantsAlpha = installedVersion.Contains("alpha", StringComparison.OrdinalIgnoreCase);
        var candidates = releases
            .EnumerateArray()
            .Where(release =>
            {
                var tag = release.GetProperty("tag_name").GetString() ?? "";
                var prerelease = release.TryGetProperty("prerelease", out var prereleaseProperty)
                    && prereleaseProperty.ValueKind == JsonValueKind.True;
                return wantsAlpha
                    ? prerelease && tag.Contains("alpha", StringComparison.OrdinalIgnoreCase)
                    : !prerelease;
            })
            .OrderByDescending(release =>
                release.TryGetProperty("published_at", out var publishedAt)
                    ? DateTimeOffset.TryParse(publishedAt.GetString(), out var value) ? value : DateTimeOffset.MinValue
                    : DateTimeOffset.MinValue)
            .ToList();

        return candidates.FirstOrDefault().ValueKind == JsonValueKind.Undefined
            ? throw new InvalidOperationException("没有找到匹配当前 sing-box 分支的 reF1nd 发布版本。")
            : candidates[0];
    }

    private static CoreAsset FindWindowsAmd64V3Asset(JsonElement assets)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
            var normalizedName = name.ToLowerInvariant();
            if (normalizedName.Contains("sing-box")
                && normalizedName.Contains("windows-amd64v3")
                && normalizedName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(downloadUrl))
            {
                return new CoreAsset(name, downloadUrl);
            }
        }

        throw new InvalidOperationException("没有找到 sing-box windows-amd64v3 发布文件。");
    }

    private static async Task<string> GetInstalledVersionAsync(string corePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(corePath, "version")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "";
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return "";
            }

            var output = $"{await outputTask} {await errorTask}";
            return ExtractVersionToken(output);
        }
        catch
        {
            return "";
        }
    }

    private static string ExtractVersionToken(string value)
    {
        var match = Regex.Match(value, @"v?\d+\.\d+\.\d+(?:[-+.][A-Za-z0-9.-]+)?");
        return match.Success ? NormalizeVersion(match.Value) : "";
    }

    private static bool IsSameVersion(string installedVersion, string latestVersion)
    {
        installedVersion = NormalizeVersion(installedVersion);
        latestVersion = NormalizeVersion(latestVersion);
        return !string.IsNullOrWhiteSpace(installedVersion)
            && !string.IsNullOrWhiteSpace(latestVersion)
            && string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string value)
    {
        value = value.Trim().TrimStart('v', 'V').ToLowerInvariant();
        return value.StartsWith("sing-box ", StringComparison.OrdinalIgnoreCase)
            ? value["sing-box ".Length..]
            : value;
    }

    private static string ExtractCoreExecutable(string archivePath, string tempRoot)
    {
        var extractRoot = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(archivePath, extractRoot);

        var executable = Directory
            .EnumerateFiles(extractRoot, "*.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(path).Contains("sing-box", StringComparison.OrdinalIgnoreCase));

        return executable ?? throw new InvalidOperationException("压缩包中没有找到 sing-box.exe。");
    }

    private static async Task<bool> HasSameFileHashAsync(
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

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Dashboard", "1.0"));
        return client;
    }

    private sealed record CoreAsset(string Name, string DownloadUrl);
}
