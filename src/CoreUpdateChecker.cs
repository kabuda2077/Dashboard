using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard;

internal sealed record CoreUpdateCheckResult(string CurrentVersion, string LatestVersion, bool UpdateAvailable);

internal static class CoreUpdateChecker
{
    internal static async Task<CoreUpdateCheckResult> CheckReleaseAsync(HttpClient client, string versionOutput,
        bool isSingBox, CancellationToken cancellationToken = default)
    {
        if (isSingBox)
        {
            var current = ExtractVersion(versionOutput);
            if (current.Length == 0) throw new InvalidOperationException("无法识别当前内核版本。");
            using var releases = await CoreUpgradeSupport.GetReleaseJsonAsync(client,
                "https://api.github.com/repos/reF1nd/sing-box-releases/releases?per_page=50", cancellationToken).ConfigureAwait(false);
            var release = SingBoxUpdater.FindMatchingRelease(releases.RootElement, current);
            var latest = release.GetProperty("tag_name").GetString() ?? "";
            return new(current, latest, !SingBoxUpdater.IsSameVersion(current, latest));
        }
        var smart = versionOutput.Contains("alpha-smart", StringComparison.OrdinalIgnoreCase)
            || versionOutput.Contains("mihomo smart", StringComparison.OrdinalIgnoreCase);
        var alpha = smart || versionOutput.Contains("alpha", StringComparison.OrdinalIgnoreCase);
        var installed = alpha ? ExtractAlphaBuild(versionOutput) ?? ExtractVersion(versionOutput) : ExtractVersion(versionOutput);
        if (installed.Length == 0) throw new InvalidOperationException("无法识别当前内核版本。");
        var url = smart ? "https://api.github.com/repos/vernesong/mihomo/releases/tags/Prerelease-Alpha"
            : alpha ? "https://api.github.com/repos/MetaCubeX/mihomo/releases/tags/Prerelease-Alpha"
            : "https://api.github.com/repos/MetaCubeX/mihomo/releases/latest";
        using var document = await CoreUpgradeSupport.GetReleaseJsonAsync(client, url, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var target = root.GetProperty("tag_name").GetString() ?? "";
        var available = alpha ? !ReleaseContainsBuild(root, ExtractAlphaBuild(versionOutput) ?? installed)
            : AppUpdateChecker.CompareVersions(target, installed) > 0;
        return new(installed, target, available);
    }

    private static bool ReleaseContainsBuild(JsonElement release, string marker) => marker.Length > 0
        && release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array
        && assets.EnumerateArray().Any(asset => asset.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String && name.GetString()!.Contains(marker, StringComparison.OrdinalIgnoreCase));

    internal static string DisplayVersion(string output, CoreKind kind)
    {
        var match = Regex.Match(output, @"(?im)^(?:Mihomo (?:Meta |Smart )?|Clash(?:\.Meta)? |sing-box version )(?<version>\S+)");
        if (!match.Success) return "";
        var version = match.Groups["version"].Value;
        return kind == CoreKind.SingBox ? "sing-box " + version.TrimStart('v', 'V') : version;
    }

    internal static string ExtractVersion(string output)
    {
        var match = Regex.Match(output, @"v?\d+\.\d+\.\d+(?:[-+.][A-Za-z0-9.-]+)?", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.TrimStart('v', 'V') : "";
    }

    private static string? ExtractAlphaBuild(string output)
    {
        var match = Regex.Match(output, @"(?:alpha-smart|alpha)[-\s]+(?<build>[A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["build"].Value : null;
    }
}
