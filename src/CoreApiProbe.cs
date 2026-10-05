using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Dashboard;

internal sealed record CoreApiStatus(string Status, string Version);

internal static class CoreApiProbe
{
    internal static async Task<CoreApiStatus> ProbeAsync(HttpClient client, string apiUrl, string secret,
        TimeSpan requestTimeout, TimeSpan totalTimeout, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(totalTimeout);
        try
        {
            while (true)
            {
                budget.Token.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl.TrimEnd('/')}/version");
                if (!string.IsNullOrEmpty(secret))
                {
                    if (!AuthenticationHeaderValue.TryParse("Bearer " + secret, out var authorization)) return new("unauthorized", "");
                    request.Headers.Authorization = authorization;
                }
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                attempt.CancelAfter(requestTimeout);
                try
                {
                    using var response = await client.SendAsync(request, attempt.Token).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        return new("unauthorized", "");
                    if (response.IsSuccessStatusCode)
                    {
                        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(attempt.Token).ConfigureAwait(false));
                        if (json.RootElement.ValueKind == JsonValueKind.Object
                            && json.RootElement.TryGetProperty("version", out var value)
                            && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                            return new("ready", value.GetString()!);
                    }
                }
                catch (HttpRequestException) { }
                catch (JsonException) { }
                catch (OperationCanceledException) when (!budget.IsCancellationRequested) { }
                await Task.Delay(retryDelay, budget.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new("unreachable", ""); }
    }
}
