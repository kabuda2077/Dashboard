namespace Dashboard;

internal sealed record NetworkDiagnosticsSnapshot(string PathKey,
    IReadOnlyList<DiagnosticLatencySnapshot> Latency, IReadOnlyList<DiagnosticRoutingSnapshot> Routing);
internal sealed record DiagnosticLatencySnapshot(string Id, IReadOnlyList<double> Samples, double TestedAt, bool Invalidated = false);
internal sealed record DiagnosticRoutingSnapshot(string Id, RoutingProbeResult Result, double TestedAt, bool Invalidated = false);

// Untrusted display cache only. Nothing here supplies routing evidence or network endpoints.
internal static class NetworkDiagnosticsSnapshots
{
    private static readonly HashSet<string> LatencyIds =
        ["bilibili", "tencent", "taobao", "github", "cloudflare", "google", "telegram", "youtube"];
    internal static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(5);

    internal static bool IsValid(NetworkDiagnosticsSnapshot? snapshot, double earliest = 0)
    {
        if (snapshot is null || string.IsNullOrEmpty(snapshot.PathKey) || snapshot.PathKey.Length > 65536
            || snapshot.Latency is null || snapshot.Routing is null || snapshot.Latency.Count > 8 || snapshot.Routing.Count > 8)
            return false;
        var now = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oldest = Math.Max(earliest - ClockSkew.TotalMilliseconds, now - TimeSpan.FromDays(30).TotalMilliseconds);
        bool ValidTime(double time) => double.IsFinite(time) && time > 0 && time >= oldest && time <= now + ClockSkew.TotalMilliseconds;
        var latencyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in snapshot.Latency)
            if (row is null || row.Id is null || !LatencyIds.Contains(row.Id) || !latencyIds.Add(row.Id)
                || row.Samples is null || row.Samples.Count != 5 || row.Samples.Any(sample => !double.IsFinite(sample) || sample < 0 || sample > 60000)
                || !ValidTime(row.TestedAt)) return false;
        var routingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in snapshot.Routing)
            if (row is null || !RoutingProbe.IsTargetId(row.Id) || !routingIds.Add(row.Id)
                || row.Result is null || row.Id != row.Result.Id || !ValidResult(row.Result) || !ValidTime(row.TestedAt)) return false;
        return true;
    }

    private static bool ValidText(string? text, int limit) => text is null
        || (text.Length <= limit && !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl));

    private static bool ValidResult(RoutingProbeResult result)
    {
        if (!ValidText(result.Node, 256) || !ValidText(result.Reason, 128)) return false;
        return result.Status switch
        {
            "matched" => result.Reason is null && (result.Direct == true ? result.Node is null : result.Direct == false && result.Node is not null),
            "unconfirmed" => result.Node is null && result.Direct is null && result.Reason is null or "connectionNotConfirmed",
            "unavailable" => result.Node is null && result.Direct is null && result.Reason is null or "apiNotLocal" or "apiNotManaged"
                or "noProxyInbound" or "proxyAuthenticationRequired" or "timeout",
            "failed" => result.Node is null && result.Direct is null && result.Reason is null or "tlsValidationFailed" or "timeout" or "probeFailed",
            _ => false
        };
    }

    internal static NetworkDiagnosticsSnapshot Clone(NetworkDiagnosticsSnapshot snapshot) => new(snapshot.PathKey,
        snapshot.Latency.Select(row => row with { Samples = row.Samples.ToArray() }).ToArray(),
        snapshot.Routing.Select(row => row with { Result = row.Result with { } }).ToArray());
}
