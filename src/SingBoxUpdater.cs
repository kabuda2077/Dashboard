using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard;

internal sealed class UnverifiedReleaseException() : Exception("发布文件未提供有效的 SHA256 摘要。请明确确认是否信任该来源，再执行候选程序或替换内核。");

public static class SingBoxUpdater
{
    private const string ReleasesApi = "https://api.github.com/repos/reF1nd/sing-box-releases/releases?per_page=50";
    private const long MaxExtractedBytes = 512L * 1024 * 1024;

    public static Task<CoreUpgradeResult> UpgradeLatestAsync(string corePath, Action? beforeReplace = null,
        CancellationToken cancellationToken = default, bool confirmUnverified = false) =>
        UpgradeAsync(corePath, beforeReplace, cancellationToken, confirmUnverified, CoreUpgradeSupport.SharedClient, CoreVersionReader.ReadAsync);

    internal static async Task<CoreUpgradeResult> UpgradeAsync(string corePath, Action? beforeReplace,
        CancellationToken cancellationToken, bool confirmUnverified, HttpClient client,
        Func<string, CoreKind, CancellationToken, Task<string>> readVersion)
    {
        if (!File.Exists(corePath)) throw new FileNotFoundException("找不到 sing-box 内核。", corePath);
        var installed = CoreUpdateChecker.ExtractVersion(await readVersion(corePath, CoreKind.SingBox, cancellationToken).ConfigureAwait(false));
        if (installed.Length == 0) throw new InvalidOperationException("无法识别当前 sing-box 内核版本。");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMinutes(10));
        var token = budget.Token;
        using var document = await CoreUpgradeSupport.GetReleaseJsonAsync(client, ReleasesApi, token).ConfigureAwait(false);
        var release = FindMatchingRelease(document.RootElement, installed);
        var version = release.GetProperty("tag_name").GetString() ?? "";
        var asset = FindAsset(release.GetProperty("assets"));
        if (IsSameVersion(installed, version)) return new(version, asset.Name, "", IsAlreadyLatest: true);
        if (asset.Digest.Length == 0 && !confirmUnverified) throw new UnverifiedReleaseException();
        var temporary = CoreUpgradeSupport.CreateTempRoot("sing-box-upgrade");
        try
        {
            // The release name is never used as a filesystem path.
            var archive = Path.Combine(temporary, "release.zip");
            await CoreUpgradeSupport.DownloadFileAsync(client, asset.Url, archive, token).ConfigureAwait(false);
            if (asset.Digest.Length > 0)
                await CoreUpgradeSupport.VerifyArchiveSha256Async(archive, asset.Digest, token).ConfigureAwait(false);
            var candidate = ExtractCoreExecutable(archive, temporary);
            var candidateVersion = CoreUpdateChecker.ExtractVersion(
                await readVersion(candidate, CoreKind.SingBox, token).ConfigureAwait(false));
            if (!IsSameVersion(candidateVersion, version)) throw new InvalidOperationException("候选内核版本与所选发布不一致。");
            if (await CoreUpgradeSupport.HasSameFileHashAsync(corePath, candidate, token).ConfigureAwait(false))
                return new(version, asset.Name, "", IsAlreadyLatest: true);
            token.ThrowIfCancellationRequested();
            beforeReplace?.Invoke();
            var backup = CoreUpgradeSupport.BackupCore(corePath);
            CoreUpgradeSupport.ReplaceCoreWithRollback(candidate, corePath, backup);
            return new(version, asset.Name, backup, IsAlreadyLatest: false);
        }
        finally { CoreUpgradeSupport.DeleteDirectoryQuietly(temporary); }
    }

    internal static JsonElement FindMatchingRelease(JsonElement releases, string installedVersion)
    {
        var wantsPrerelease = IsPrereleaseVersion(installedVersion);
        return releases.EnumerateArray()
            .Where(release => !(release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                && (release.TryGetProperty("prerelease", out var value) && value.ValueKind == JsonValueKind.True) == wantsPrerelease)
            .OrderByDescending(release => release.TryGetProperty("published_at", out var date)
                && DateTimeOffset.TryParse(date.GetString(), out var parsed) ? parsed : DateTimeOffset.MinValue)
            .Cast<JsonElement?>().FirstOrDefault()
            ?? throw new InvalidOperationException("没有找到匹配当前 sing-box 分支的 reF1nd 发布版本。");
    }

    private static bool IsPrereleaseVersion(string version) => version.Contains("alpha", StringComparison.OrdinalIgnoreCase)
        || version.Contains("beta", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(version, @"(?:^|[-.])rc(?:[-.\d]|$)", RegexOptions.IgnoreCase);

    internal static (string Name, string Url, string Digest) FindAsset(JsonElement assets)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!name.Contains("sing-box", StringComparison.OrdinalIgnoreCase)
                || !name.Contains("windows-amd64v3", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != "https" || address.UserInfo.Length > 0)
                throw new InvalidOperationException("发布文件地址无效。");
            var rawDigest = asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String ? digest.GetString() : null;
            var normalized = CoreUpgradeSupport.NormalizeSha256Digest(rawDigest);
            if (!string.IsNullOrWhiteSpace(rawDigest) && normalized.Length == 0)
                throw new InvalidOperationException("发布摘要格式无效。");
            return (name, url, normalized);
        }
        throw new InvalidOperationException("没有找到 sing-box windows-amd64v3 发布文件。");
    }

    internal static bool IsSameVersion(string installedVersion, string latestVersion)
    {
        static string Normalize(string value) => value.Trim().TrimStart('v', 'V').ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(installedVersion) && !string.IsNullOrWhiteSpace(latestVersion)
            && Normalize(installedVersion) == Normalize(latestVersion);
    }

    internal static string ExtractCoreExecutable(string archivePath, string temporary)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 4096 || archive.Entries.Sum(entry => entry.Length) > MaxExtractedBytes)
            throw new InvalidOperationException("内核归档超过解压限制。");
        var entries = archive.Entries.Where(entry => entry.Name.Equals("sing-box.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length != 1) throw new InvalidOperationException("归档必须包含唯一的 sing-box.exe。");
        // Extract exactly one validated entry to our own filename, never an archive-controlled path.
        var destination = Path.Combine(temporary, "candidate-sing-box.exe");
        entries[0].ExtractToFile(destination);
        return destination;
    }
}
