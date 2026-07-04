using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard;

public sealed record CoreUpgradeResult(
    string Version,
    string AssetName,
    string BackupPath,
    bool IsAlreadyLatest,
    string Warning = "");

public static class CoreUpdater
{
    private const string LatestReleaseApi = "https://api.github.com/repos/MetaCubeX/mihomo/releases/latest";

    public static async Task<CoreUpgradeResult> UpgradeLatestAsync(string corePath, Action? beforeReplace = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(corePath))
        {
            throw new InvalidOperationException("请先设置 mihomo 内核路径。");
        }

        if (!File.Exists(corePath))
        {
            throw new FileNotFoundException("找不到 mihomo 内核，请检查路径。", corePath);
        }

        using var client = CoreUpgradeSupport.CreateHttpClient();
        using var releaseResponse = await client.GetAsync(LatestReleaseApi, cancellationToken);
        releaseResponse.EnsureSuccessStatusCode();

        await using var releaseStream = await releaseResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(releaseStream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var version = root.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() ?? "latest" : "latest";
        var asset = FindWindowsX64Asset(root.GetProperty("assets"));
        var installedVersion = await GetInstalledVersionAsync(corePath, cancellationToken);
        if (IsSameVersion(installedVersion, version))
        {
            return new CoreUpgradeResult(version, asset.Name, "", IsAlreadyLatest: true);
        }

        var tempRoot = CoreUpgradeSupport.CreateTempRoot("core-upgrade");
        try
        {
            var archivePath = Path.Combine(tempRoot, asset.Name);
            await CoreUpgradeSupport.DownloadFileAsync(client, asset.DownloadUrl, archivePath, cancellationToken);

            await CoreUpgradeSupport.VerifyArchiveSha256Async(archivePath, asset.Sha256Digest, cancellationToken);
            var extractedCore = ExtractCoreExecutable(archivePath, tempRoot);
            if (await CoreUpgradeSupport.HasSameFileHashAsync(corePath, extractedCore, cancellationToken))
            {
                return new CoreUpgradeResult(version, asset.Name, "", IsAlreadyLatest: true);
            }

            beforeReplace?.Invoke();
            var backupPath = CoreUpgradeSupport.BackupCore(corePath);
            CoreUpgradeSupport.ReplaceCoreWithRollback(extractedCore, corePath, backupPath);

            return new CoreUpgradeResult(version, asset.Name, backupPath, IsAlreadyLatest: false);
        }
        finally
        {
            CoreUpgradeSupport.DeleteDirectoryQuietly(tempRoot);
        }
    }

    private static async Task<string> GetInstalledVersionAsync(string corePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(corePath, "-v")
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
        return !string.IsNullOrWhiteSpace(installedVersion) &&
            !string.IsNullOrWhiteSpace(latestVersion) &&
            string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string value)
    {
        value = value.Trim().TrimStart('v', 'V').ToLowerInvariant();
        return value.StartsWith("release-", StringComparison.OrdinalIgnoreCase)
            ? value["release-".Length..]
            : value;
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

    private static CoreAsset FindWindowsX64Asset(JsonElement assets)
    {
        var candidates = new List<CoreAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
            var sha256Digest = CoreUpgradeSupport.NormalizeSha256Digest(
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

    private sealed record CoreAsset(string Name, string DownloadUrl, string Sha256Digest);
}
