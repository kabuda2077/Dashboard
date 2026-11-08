using System.Net.Http.Headers;
using System.Text.Json;

namespace Dashboard;

// Release metadata only. Core binary downloads and replacement belong to the core itself.
internal static class CoreUpgradeSupport
{
    private const int MaxReleaseRequestAttempts = 3;

    // Shared across release metadata checks; callers must not dispose it.
    public static HttpClient SharedClient { get; } = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Dashboard", "1.0"));
        return client;
    }

    public static async Task<JsonDocument> GetReleaseJsonAsync(
        HttpClient client, string requestUrl, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        cancellationToken = budget.Token;
        for (var attempt = 1; attempt <= MaxReleaseRequestAttempts; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(requestUrl,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (attempt < MaxReleaseRequestAttempts
                && !cancellationToken.IsCancellationRequested && IsTransientReleaseRequestFailure(ex))
            {
                HostOperationLogger.Info("update",
                    $"Release metadata request failed on attempt {attempt}/{MaxReleaseRequestAttempts}; retrying: {ex.Message}");
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
            }
        }
        throw new InvalidOperationException("发布信息请求未返回结果。");
    }

    private static bool IsTransientReleaseRequestFailure(Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: { } statusCode })
        {
            var status = (int)statusCode;
            return status is 408 or 429 || status >= 500;
        }
        return exception is HttpRequestException or IOException or JsonException;
    }
}
