using System.Net.Http.Headers;

namespace Dashboard;

internal static class CoreApiProbe
{
    internal static async Task<bool> WaitAsync(HttpClient client, string apiUrl, string secret,
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
                if (!string.IsNullOrWhiteSpace(secret)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                attempt.CancelAfter(requestTimeout);
                try
                {
                    using var response = await client.SendAsync(request, attempt.Token);
                    if (response.IsSuccessStatusCode) return true;
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!budget.IsCancellationRequested) { }
                await Task.Delay(retryDelay, budget.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }
}
