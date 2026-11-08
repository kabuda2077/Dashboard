using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard;

internal sealed record CoreUpdateCheckResult(string CurrentVersion, string LatestVersion, bool UpdateAvailable);

internal static class CoreUpdateChecker
{
    internal static async Task<CoreUpdateCheckResult> CheckReleaseAsync(HttpClient client, string versionOutput,
        bool isSingBox, CancellationToken cancellationToken = default)
    {
        var kind = isSingBox ? CoreKind.SingBox : CoreKind.Mihomo;
        var source = CoreUpdateSource.Resolve(versionOutput, kind);
        if (source.Mode != "automatic")
            throw new InvalidOperationException(source.Mode == "manual" ? "manualCoreUpdate" : "unknownCoreSource");
        var version = CoreUpdateSource.VersionField(versionOutput, kind);
        var prerelease = !version.StartsWith('v') && !version.StartsWith('V');
        var installed = prerelease ? version[(version.IndexOf('-') + 1)..] : version[1..];
        var url = prerelease
            ? "https://api.github.com/repos/MetaCubeX/mihomo/releases/tags/Prerelease-Alpha"
            : "https://api.github.com/repos/MetaCubeX/mihomo/releases/latest";
        using var document = await CoreUpgradeSupport.GetReleaseJsonAsync(client, url, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var target = root.GetProperty("tag_name").GetString() ?? "";
        var available = prerelease ? !ReleaseContainsBuild(root, installed)
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

}
