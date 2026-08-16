using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard;

public static class SingBoxUpdater
{
    private const string ReleasesApi = "https://api.github.com/repos/reF1nd/sing-box-releases/releases?per_page=50";

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
        using var client = CoreUpgradeSupport.CreateHttpClient();
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

        var tempRoot = CoreUpgradeSupport.CreateTempRoot("sing-box-upgrade");
        try
        {
            var archivePath = Path.Combine(tempRoot, asset.Name);
            await CoreUpgradeSupport.DownloadFileAsync(client, asset.DownloadUrl, archivePath, cancellationToken);

            var extractedCore = ExtractCoreExecutable(archivePath, tempRoot);
            if (await CoreUpgradeSupport.HasSameFileHashAsync(corePath, extractedCore, cancellationToken))
            {
                return new CoreUpgradeResult(version, asset.Name, "", IsAlreadyLatest: true);
            }

            beforeReplace?.Invoke();
            var backupPath = CoreUpgradeSupport.BackupCore(corePath);
            CoreUpgradeSupport.ReplaceCoreWithRollback(extractedCore, corePath, backupPath);

            return new CoreUpgradeResult(
                version,
                asset.Name,
                backupPath,
                IsAlreadyLatest: false,
                Warning: "sing-box 发布文件未提供 SHA256 digest，本次升级无法进行发布方校验。");
        }
        finally
        {
            CoreUpgradeSupport.DeleteDirectoryQuietly(tempRoot);
        }
    }

    internal static JsonElement FindMatchingRelease(JsonElement releases, string installedVersion)
    {
        var wantsPrerelease = IsPrereleaseVersion(installedVersion);
        var candidates = releases
            .EnumerateArray()
            .Where(release =>
            {
                var prerelease = release.TryGetProperty("prerelease", out var prereleaseProperty)
                    && prereleaseProperty.ValueKind == JsonValueKind.True;
                return prerelease == wantsPrerelease;
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

    private static bool IsPrereleaseVersion(string version)
    {
        return version.Contains("alpha", StringComparison.OrdinalIgnoreCase)
            || version.Contains("beta", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(version, @"(?:^|[-.])rc(?:[-.\d]|$)", RegexOptions.IgnoreCase);
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

    internal static bool IsSameVersion(string installedVersion, string latestVersion)
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

    private sealed record CoreAsset(string Name, string DownloadUrl);
}
