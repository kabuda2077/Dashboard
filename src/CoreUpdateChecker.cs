using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard;

internal sealed record CoreUpdateCheckResult(
    string CurrentVersion,
    string LatestVersion,
    bool UpdateAvailable);

internal static partial class CoreUpdateChecker
{
    private const string MihomoStableApi = "https://api.github.com/repos/MetaCubeX/mihomo/releases/latest";
    private const string MihomoAlphaApi = "https://api.github.com/repos/MetaCubeX/mihomo/releases/tags/Prerelease-Alpha";
    private const string MihomoSmartApi = "https://api.github.com/repos/vernesong/mihomo/releases/tags/Prerelease-Alpha";
    private const string SingBoxReleasesApi = "https://api.github.com/repos/reF1nd/sing-box-releases/releases?per_page=50";

    public static async Task<CoreUpdateCheckResult> CheckAsync(
        string corePath,
        bool isSingBox,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(corePath) || !File.Exists(corePath))
        {
            return new CoreUpdateCheckResult("", "", false);
        }

        var versionOutput = await ReadVersionOutputAsync(corePath, isSingBox, cancellationToken);
        if (string.IsNullOrWhiteSpace(versionOutput))
        {
            throw new InvalidOperationException("无法读取当前内核版本。");
        }

        using var client = CoreUpgradeSupport.CreateHttpClient();
        return await CheckReleaseAsync(client, versionOutput, isSingBox, cancellationToken);
    }

    internal static async Task<CoreUpdateCheckResult> CheckReleaseAsync(
        HttpClient client,
        string versionOutput,
        bool isSingBox,
        CancellationToken cancellationToken = default)
    {
        if (isSingBox)
        {
            var singBoxCurrentVersion = ExtractVersion(versionOutput);
            if (string.IsNullOrWhiteSpace(singBoxCurrentVersion))
            {
                throw new InvalidOperationException("无法识别当前内核版本。");
            }

            using var response = await client.GetAsync(SingBoxReleasesApi, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var release = SingBoxUpdater.FindMatchingRelease(document.RootElement, singBoxCurrentVersion);
            var latestVersion = release.GetProperty("tag_name").GetString() ?? "";
            return new CoreUpdateCheckResult(
                singBoxCurrentVersion,
                latestVersion,
                !SingBoxUpdater.IsSameVersion(singBoxCurrentVersion, latestVersion));
        }

        var channel = DetectMihomoChannel(versionOutput);
        var currentVersion = channel == MihomoChannel.Stable
            ? ExtractVersion(versionOutput)
            : ExtractAlphaBuild(versionOutput) ?? ExtractVersion(versionOutput);
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            throw new InvalidOperationException("无法识别当前内核版本。");
        }

        var releaseApi = channel switch
        {
            MihomoChannel.Alpha => MihomoAlphaApi,
            MihomoChannel.Smart => MihomoSmartApi,
            _ => MihomoStableApi
        };
        using var releaseResponse = await client.GetAsync(releaseApi, cancellationToken);
        releaseResponse.EnsureSuccessStatusCode();
        await using var releaseStream = await releaseResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var releaseDocument = await JsonDocument.ParseAsync(
            releaseStream,
            cancellationToken: cancellationToken);
        var root = releaseDocument.RootElement;
        var latest = root.GetProperty("tag_name").GetString() ?? "";

        var updateAvailable = channel == MihomoChannel.Stable
            ? AppUpdateChecker.CompareVersions(latest, currentVersion) > 0
            : !ReleaseContainsCurrentBuild(root, versionOutput, currentVersion);
        return new CoreUpdateCheckResult(currentVersion, latest, updateAvailable);
    }

    private static bool ReleaseContainsCurrentBuild(
        JsonElement release,
        string versionOutput,
        string currentVersion)
    {
        var marker = ExtractAlphaBuild(versionOutput) ?? currentVersion;
        if (string.IsNullOrWhiteSpace(marker)
            || !release.TryGetProperty("assets", out var assets)
            || assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return assets.EnumerateArray().Any(asset =>
            asset.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String
            && name.GetString()?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static MihomoChannel DetectMihomoChannel(string versionOutput)
    {
        if (versionOutput.Contains("alpha-smart", StringComparison.OrdinalIgnoreCase)
            || versionOutput.Contains("mihomo smart", StringComparison.OrdinalIgnoreCase))
        {
            return MihomoChannel.Smart;
        }

        return versionOutput.Contains("alpha", StringComparison.OrdinalIgnoreCase)
            ? MihomoChannel.Alpha
            : MihomoChannel.Stable;
    }

    private static string ExtractVersion(string value)
    {
        var match = VersionPattern().Match(value);
        return match.Success ? match.Value.TrimStart('v', 'V') : "";
    }

    private static string? ExtractAlphaBuild(string value)
    {
        var match = AlphaBuildPattern().Match(value);
        return match.Success ? match.Groups["build"].Value : null;
    }

    private static async Task<string> ReadVersionOutputAsync(
        string corePath,
        bool isSingBox,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(corePath, isSingBox ? "version" : "-v")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动内核版本探测进程。");
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
            throw new TimeoutException("读取内核版本超时。");
        }

        return $"{await outputTask} {await errorTask}".Trim();
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

    private enum MihomoChannel
    {
        Stable,
        Alpha,
        Smart
    }

    [GeneratedRegex(@"v?\d+\.\d+\.\d+(?:[-+.][A-Za-z0-9.-]+)?", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"(?:alpha-smart|alpha)[-\s]+(?<build>[A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AlphaBuildPattern();
}
