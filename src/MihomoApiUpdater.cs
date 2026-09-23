using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard;

internal sealed record MihomoApiUpgradeResult(bool IsAlreadyLatest, string Version = "");

internal sealed class MihomoApiUpgradeException(string userMessage, string diagnosticMessage)
    : InvalidOperationException(diagnosticMessage)
{
    public string UserMessage { get; } = userMessage;
}

internal static class MihomoApiUpdater
{
    private static readonly TimeSpan UpgradeTimeout = TimeSpan.FromMinutes(10);
    // Auth is set per request, so one shared client serves every core/secret.
    private static readonly HttpClient SharedClient = new() { Timeout = UpgradeTimeout };
    private static readonly Regex VersionPattern = new(
        @"(?:version\s+)?(?<version>v?\d+\.\d+\.\d+(?:[-+.][A-Za-z0-9.-]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<MihomoApiUpgradeResult> UpgradeAsync(
        string apiUrl,
        string secret,
        CancellationToken cancellationToken = default)
    {
        return await UpgradeAsync(SharedClient, apiUrl, secret, cancellationToken);
    }

    internal static async Task<MihomoApiUpgradeResult> UpgradeAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUpgradeUri(apiUrl));
        if (!string.IsNullOrWhiteSpace(secret))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret.Trim());
        }

        HostOperationLogger.Info("upgrade", $"Requesting mihomo self-upgrade through {request.RequestUri}.");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            HostOperationLogger.Info("upgrade", $"mihomo self-upgrade request completed with HTTP {(int)response.StatusCode}.");
            return new MihomoApiUpgradeResult(IsAlreadyLatest: false);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = ExtractErrorMessage(responseBody);
        if (IsAlreadyLatestMessage(detail))
        {
            var version = ExtractVersion(detail);
            HostOperationLogger.Info("upgrade", $"mihomo is already latest: {version}.");
            return new MihomoApiUpgradeResult(IsAlreadyLatest: true, version);
        }

        var diagnosticDetail = string.IsNullOrWhiteSpace(responseBody)
            ? response.ReasonPhrase ?? "unknown error"
            : responseBody.Trim();
        throw new MihomoApiUpgradeException(
            GetUserFriendlyError((int)response.StatusCode, detail),
            $"mihomo upgrade API returned HTTP {(int)response.StatusCode}: {diagnosticDetail}");
    }

    internal static Uri BuildUpgradeUri(string apiUrl)
    {
        if (!Uri.TryCreate(apiUrl?.Trim(), UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("mihomo API 地址无效，请检查面板设置中的 API 地址。");
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/upgrade",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    internal static string ExtractErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString()?.Trim() ?? "";
            }
        }
        catch (JsonException)
        {
        }

        return responseBody.Trim();
    }

    private static bool IsAlreadyLatestMessage(string message)
    {
        return message.Contains("already using latest version", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("already the latest version", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractVersion(string message)
    {
        var match = VersionPattern.Match(message);
        return match.Success ? match.Groups["version"].Value : "";
    }

    private static string GetUserFriendlyError(int statusCode, string detail)
    {
        return statusCode switch
        {
            401 => "升级失败：API 密钥不正确，请检查面板设置。",
            403 => "升级失败：当前核心拒绝了升级请求。",
            404 => "升级失败：当前 mihomo 版本不支持在线升级接口。",
            _ when !string.IsNullOrWhiteSpace(detail) => $"升级失败：{detail}",
            _ => $"升级失败：核心返回 HTTP {statusCode}，详情请查看升级日志。"
        };
    }
}
