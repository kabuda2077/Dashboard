using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard;

internal sealed record AppUpdateResult(
    string CurrentVersion,
    string LatestVersion,
    bool UpdateAvailable);

internal static partial class AppUpdateChecker
{
    internal const string LatestReleaseApiUrl = "https://api.github.com/repos/kabuda2077/Dashboard/releases/latest";
    internal const string ReleasesPageUrl = "https://github.com/kabuda2077/Dashboard/releases/latest";

    // One shared client per process; HttpClient is thread-safe and must not be
    // created per call (each instance holds its own connection pool).
    private static readonly HttpClient SharedClient = CreateHttpClient();

    public static async Task<AppUpdateResult> CheckAsync(
        string currentVersion,
        HttpClient? client = null,
        CancellationToken cancellationToken = default)
    {
        client ??= SharedClient;
        using var response = await client.GetAsync(LatestReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var tagName = document.RootElement.TryGetProperty("tag_name", out var tagProperty)
            ? tagProperty.GetString() ?? ""
            : "";
        var latestVersion = NormalizeVersion(tagName);
        var normalizedCurrent = NormalizeVersion(currentVersion);
        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            throw new InvalidDataException("GitHub Release 没有有效的版本号。");
        }

        if (string.IsNullOrWhiteSpace(normalizedCurrent))
        {
            throw new InvalidDataException("当前 Dashboard 版本号无效。");
        }

        return new AppUpdateResult(
            normalizedCurrent,
            latestVersion,
            CompareVersions(latestVersion, normalizedCurrent) > 0);
    }

    internal static string NormalizeVersion(string value)
    {
        var match = VersionPattern().Match(value.Trim());
        if (!match.Success)
        {
            return "";
        }

        return $"{int.Parse(match.Groups[1].Value)}.{int.Parse(match.Groups[2].Value)}.{int.Parse(match.Groups[3].Value)}";
    }

    internal static int CompareVersions(string left, string right)
    {
        var leftVersion = ParseVersion(left);
        var rightVersion = ParseVersion(right);
        return leftVersion.CompareTo(rightVersion);
    }

    private static Version ParseVersion(string value)
    {
        var normalized = NormalizeVersion(value);
        return Version.TryParse(normalized, out var version)
            ? version
            : throw new FormatException($"无效版本号：{value}");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Dashboard", DashboardVersion.Current));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    [GeneratedRegex(@"^[vV]?(\d+)\.(\d+)\.(\d+)(?:[-+].*)?$")]
    private static partial Regex VersionPattern();
}
