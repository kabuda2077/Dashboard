using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;

namespace Dashboard;

internal sealed record RoutingProbeResult(string Id, string Status, string? Node = null, bool? Direct = null, string? Reason = null);
internal sealed record RoutingProbeSession(CoreKind Kind, CoreProfile Connection, CoreProfile Running, long Epoch,
    int ProcessId, RoutingProbeInbound? Inbound);
internal sealed record RoutingProbeInbound(IPAddress Address, int Port, bool Socks, bool Authentication);
internal sealed record RoutingProbeTarget(string Id, string Host, string Path, int Port = 443);

// Independent HTTPS streams: the socket's source tuple belongs to this process,
// not to a browser request or a predicted rule/group selection.
internal sealed class RoutingProbe
{
    private static readonly RoutingProbeTarget[] Targets =
    [
        new("alibaba", "www.aliyun.com", "/robots.txt"),
        new("youtube", "www.youtube.com", "/robots.txt"),
        new("google", "www.google.com", "/robots.txt"),
        new("telegram", "telegram.org", "/robots.txt"),
        new("cloudflare", "www.cloudflare.com", "/cdn-cgi/trace"),
        new("chatgpt", "chatgpt.com", "/robots.txt"),
        new("bilibili", "www.bilibili.com", "/robots.txt"),
        new("github", "github.com", "/robots.txt")
    ];
    private readonly IReadOnlyList<RoutingProbeTarget> _targets;
    private readonly Func<string, SslClientAuthenticationOptions> _tls;
    public RoutingProbe() : this(Targets, host => new() { TargetHost = host }) { }
    // Fixture-only seam for an owned TLS origin and custom-root trust, never bridge input.
    internal RoutingProbe(IReadOnlyList<RoutingProbeTarget> targets, Func<string, SslClientAuthenticationOptions> tls)
    { _targets = targets; _tls = tls; }

    public async Task<IReadOnlyList<RoutingProbeResult>> RunAsync(RoutingProbeSession session, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(14));
        try { return await RunOwnedAsync(session, budget.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Unavailable("timeout"); }
    }

    private async Task<IReadOnlyList<RoutingProbeResult>> RunOwnedAsync(RoutingProbeSession session, CancellationToken token)
    {
        if (!Uri.TryCreate(session.Connection.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var api)
            || api.Scheme is not ("http" or "https") || api.UserInfo.Length != 0 || LocalAddress(api.Host) is null)
            return Unavailable("apiNotLocal");
        var apiAddress = LocalAddress(api.Host)!;
        if (!ManagedCoreListener.Owns(new(apiAddress, api.Port), session.ProcessId)) return Unavailable("apiNotManaged");
        using var client = new HttpClient(new SocketsHttpHandler {
            UseProxy = false, AllowAutoRedirect = false,
            ConnectCallback = async (_, cancellation) =>
            {
                var endpoint = new IPEndPoint(apiAddress, api.Port);
                if (!ManagedCoreListener.Owns(endpoint, session.ProcessId)) throw new IOException("API listener changed.");
                var socket = new Socket(apiAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(endpoint, cancellation).ConfigureAwait(false);
                    if (!ManagedCoreListener.Owns(endpoint, session.ProcessId)) throw new IOException("API listener changed.");
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        })
        { BaseAddress = api, Timeout = TimeSpan.FromSeconds(3) };
        // Credentials live on the API client only, never on the proxy or TLS streams.
        if (session.Connection.Secret.Length > 0) client.DefaultRequestHeaders.Authorization = new("Bearer", session.Connection.Secret);
        RoutingProbeInbound? inbound;
        try
        {
            inbound = await DiscoverAsync(session, client, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or JsonException or HttpRequestException or OperationCanceledException or UnauthorizedAccessException)
        { return Unavailable("noProxyInbound"); }
        if (inbound is null) return Unavailable("noProxyInbound");
        if (inbound.Authentication) return Unavailable("proxyAuthenticationRequired");
        if (!ManagedCoreListener.Owns(new(inbound.Address, inbound.Port), session.ProcessId)) return Unavailable("noProxyInbound");
        return await Task.WhenAll(_targets.Select(target => ProbeAsync(client, inbound, target, session.ProcessId, token))).ConfigureAwait(false);
    }

    private IReadOnlyList<RoutingProbeResult> Unavailable(string reason) => _targets.Select(t => new RoutingProbeResult(t.Id, "unavailable", Reason: reason)).ToArray();
    private static async Task<RoutingProbeInbound?> DiscoverAsync(RoutingProbeSession session, HttpClient api, CancellationToken token)
    {
        if (session.Kind == CoreKind.Mihomo)
        {
            using var config = await GetAsync(api, "configs", token).ConfigureAwait(false);
            var root = config.RootElement;
            var address = root.TryGetProperty("allow-lan", out var lan) && lan.ValueKind == JsonValueKind.True
                ? LocalAddress(Text(root, "bind-address") ?? "*") : IPAddress.Loopback;
            if (address is null) return null;
            var auth = root.TryGetProperty("authentication", out var users) && users.ValueKind == JsonValueKind.Array && users.GetArrayLength() > 0;
            // The default mixed/HTTP/SOCKS ports reported by the runtime are supported.
            // Select the first enabled transport deterministically; never guess a port.
            foreach (var (name, socks) in new[] { ("mixed-port", false), ("port", false), ("socks-port", true) })
                if (Port(root, name) is { } port) return new(address, port, socks, auth);
            return null;
        }
        return session.Inbound;
    }

    // Parse only the routing capability from the launch input. Never retain the
    // raw config (which can contain outbound passwords, private keys and users).
    internal static RoutingProbeInbound? CaptureSingBoxInbound(string json)
    {
        using var document = JsonDocument.Parse(json, new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!document.RootElement.TryGetProperty("inbounds", out var inbounds) || inbounds.ValueKind != JsonValueKind.Array) return null;
        var candidates = new List<RoutingProbeInbound>();
        var declaredListeners = 0;
        foreach (var entry in inbounds.EnumerateArray())
        {
            var type = Text(entry, "type");
            if (type is not ("mixed" or "http" or "socks")) continue;
            if (entry.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True) continue;
            if (++declaredListeners > 1) return null;
            var address = LocalAddress(Text(entry, "listen") ?? "127.0.0.1");
            if (address is null || Port(entry, "listen_port") is not { } port) continue;
            var auth = entry.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array && users.GetArrayLength() > 0;
            // TLS-wrapped HTTP inbounds and detoured transports aren't plain local proxy listeners.
            if (entry.TryGetProperty("tls", out var tls) && tls.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True
                || !string.IsNullOrEmpty(Text(entry, "detour"))) continue;
            candidates.Add(new(address, port, type == "socks", auth));
        }
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static IPAddress? LocalAddress(string value)
    {
        if (value is "*" or "0.0.0.0" or "localhost") return IPAddress.Loopback;
        if (value is "::" or "[::]") return IPAddress.IPv6Loopback;
        if (!IPAddress.TryParse(value.Trim('[', ']'), out var address)) return null;
        if (IPAddress.IsLoopback(address)) return address;
        return NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(a => a.Address.Equals(address)) ? address : null;
    }
    private static int? Port(JsonElement element, string name) => element.TryGetProperty(name, out var port)
        && port.ValueKind == JsonValueKind.Number && port.TryGetInt32(out var number) && number is > 0 and <= 65535 ? number : null;
    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.ToString() : null;

    internal async Task<RoutingProbeResult> ProbeAsync(HttpClient api, RoutingProbeInbound inbound, RoutingProbeTarget target, int processId, CancellationToken lifetime)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        budget.CancelAfter(TimeSpan.FromSeconds(12));
        var token = budget.Token;
        var httpsCompleted = false;
        try
        {
            using var baseline = await GetAsync(api, "connections", token).ConfigureAwait(false);
            var oldIds = Connections(baseline).Select(c => Text(c, "id")).Where(id => id is not null).ToHashSet();
            using var client = new TcpClient(inbound.Address.AddressFamily) { NoDelay = true };
            await client.ConnectAsync(inbound.Address, inbound.Port, token).ConfigureAwait(false);
            if (!ManagedCoreListener.Owns(new(inbound.Address, inbound.Port), processId)) return new(target.Id, "unavailable", Reason: "noProxyInbound");
            var source = (IPEndPoint)client.Client.LocalEndPoint!;
            var stream = client.GetStream();
            if (inbound.Socks) await SocksAsync(stream, target, token).ConfigureAwait(false);
            else
            {
                await SendAsync(stream, $"CONNECT {target.Host}:{target.Port} HTTP/1.1\r\nHost: {target.Host}:{target.Port}\r\n\r\n", token).ConfigureAwait(false);
                var header = await HeadersAsync(stream, token).ConfigureAwait(false);
                if (Status(header) == 407) return new(target.Id, "unavailable", Reason: "proxyAuthenticationRequired");
                if (Status(header) != 200) throw new IOException("CONNECT rejected.");
            }
            using var tls = new SslStream(stream, leaveInnerStreamOpen: true);
            await tls.AuthenticateAsClientAsync(_tls(target.Host), token).ConfigureAwait(false);
            await SendAsync(tls, $"GET {target.Path} HTTP/1.1\r\nHost: {target.Host}\r\nConnection: keep-alive\r\n\r\n", token).ConfigureAwait(false);
            var headers = await HeadersAsync(tls, token).ConfigureAwait(false);
            if (Status(headers) is < 200 or > 599) throw new IOException("Invalid HTTP response.");
            httpsCompleted = true;
            // Never follow redirects or consume a potentially large body. Keep the same
            // independent stream alive through both observations after TLS and HTTP.
            using var first = await GetAsync(api, "connections", token).ConfigureAwait(false);
            var a = await MatchAsync(api, first, source, target, oldIds, token).ConfigureAwait(false);
            await Task.Delay(100, token).ConfigureAwait(false);
            using var second = await GetAsync(api, "connections", token).ConfigureAwait(false);
            var b = await MatchAsync(api, second, source, target, oldIds, token).ConfigureAwait(false);
            if (a is null || b is null || a != b) return new(target.Id, "unconfirmed", Reason: "connectionNotConfirmed");
            return a.Direct
                ? new(target.Id, "matched", Direct: true) : new(target.Id, "matched", Node: a.Node, Direct: false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
        catch (ProxyAuthenticationException) { return new(target.Id, "unavailable", Reason: "proxyAuthenticationRequired"); }
        catch (AuthenticationException) { return new(target.Id, "failed", Reason: "tlsValidationFailed"); }
        catch (Exception error) when (error is IOException or SocketException or HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            // Failure to read routing evidence cannot undo a completed HTTPS
            // exchange. Lifecycle cancellation above must still propagate.
            return httpsCompleted
                ? new(target.Id, "unconfirmed", Reason: "connectionNotConfirmed")
                : new(target.Id, "failed", Reason: error is OperationCanceledException ? "timeout" : "probeFailed");
        }
    }

    private sealed class ProxyAuthenticationException : IOException;
    internal sealed record Observation(string Id, string Node, bool Direct, string Chain);
    internal static async Task<Observation?> MatchAsync(HttpClient api, JsonDocument snapshot, IPEndPoint source, RoutingProbeTarget target, HashSet<string?> oldIds, CancellationToken token)
    {
        var candidates = Connections(snapshot).Where(c => c.TryGetProperty("metadata", out var metadata)
            && IPAddress.TryParse(Text(metadata, "sourceIP"), out var address)
            && address.MapToIPv6().Equals(source.Address.MapToIPv6())
            && Text(metadata, "sourcePort") == source.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (candidates.Length != 1) return null;
        var candidate = candidates[0]; var metadata = candidate.GetProperty("metadata");
        var observedHost = Text(metadata, "host");
        var sniffHost = Text(metadata, "sniffHost");
        bool HostMatches(string? value) => string.IsNullOrEmpty(value) || string.Equals(value.TrimEnd('.'), target.Host, StringComparison.OrdinalIgnoreCase);
        if ((string.IsNullOrEmpty(observedHost) && string.IsNullOrEmpty(sniffHost)) || !HostMatches(observedHost) || !HostMatches(sniffHost)
            || Text(metadata, "destinationPort") != target.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)) return null;
        var id = Text(candidate, "id");
        if (string.IsNullOrWhiteSpace(id) || oldIds.Contains(id) || !candidate.TryGetProperty("chains", out var chains)
            || chains.ValueKind != JsonValueKind.Array || chains.GetArrayLength() == 0) return null;
        if (chains.EnumerateArray().Any(c => c.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(c.GetString()))) return null;
        var node = chains[0].GetString()!;
        string? provider = null;
        var providerChain = "";
        if (candidate.TryGetProperty("providerChains", out var providerChains))
        {
            if (providerChains.ValueKind != JsonValueKind.Array || providerChains.GetArrayLength() != chains.GetArrayLength()
                || providerChains.EnumerateArray().Any(p => p.ValueKind != JsonValueKind.String)) return null;
            provider = providerChains[0].GetString();
            providerChain = providerChains.GetRawText();
        }
        // The first chain member is not necessarily a leaf. Require API type evidence;
        // a group's current selection is never evidence for this connection.
        using var proxies = await GetAsync(api, "proxies", token).ConfigureAwait(false);
        if (!proxies.RootElement.TryGetProperty("proxies", out var map) || map.ValueKind != JsonValueKind.Object) return null;
        var hasProxy = map.TryGetProperty(node, out var proxy);
        var type = hasProxy ? LeafType(proxy, node) : null;
        if (hasProxy && type is null) return null;
        if (!hasProxy || !string.IsNullOrEmpty(provider))
        {
            // mihomo subscription nodes can exist only in /providers/proxies. Resolve
            // them only when needed, using the recorded provider when available.
            using var providers = await GetAsync(api, "providers/proxies", token).ConfigureAwait(false);
            var providerType = ProviderLeafType(providers.RootElement, node, provider);
            if (providerType is null || (type is not null && !type.Equals(providerType, StringComparison.OrdinalIgnoreCase))) return null;
            type = providerType;
        }
        var direct = type!.Equals("Direct", StringComparison.OrdinalIgnoreCase);
        // DIRECT elsewhere in the chain conflicts with a different observed leaf.
        foreach (var member in chains.EnumerateArray().Skip(1))
            if (map.TryGetProperty(member.GetString()!, out var other)
                && Text(other, "type")?.Equals("Direct", StringComparison.OrdinalIgnoreCase) == true && !direct) return null;
        return new(id, node, direct, chains.GetRawText() + "\n" + providerChain);
    }
    private static string? LeafType(JsonElement proxy, string name)
    {
        if (proxy.ValueKind != JsonValueKind.Object || proxy.TryGetProperty("all", out _)
            || (proxy.TryGetProperty("name", out var named) && (named.ValueKind != JsonValueKind.String || named.GetString() != name))) return null;
        var type = Text(proxy, "type");
        return type is not null && LeafTypes.Contains(type) ? type : null;
    }
    private static string? ProviderLeafType(JsonElement root, string node, string? provider)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Object) return null;
        string? type = null;
        var matches = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in providers.EnumerateObject())
        {
            if (!names.Add(entry.Name)) return null;
            if (!string.IsNullOrEmpty(provider) && entry.Name != provider) continue;
            if (entry.Value.ValueKind != JsonValueKind.Object || !entry.Value.TryGetProperty("proxies", out var nodes)
                || nodes.ValueKind != JsonValueKind.Array) return null;
            foreach (var leaf in nodes.EnumerateArray())
            {
                if (leaf.ValueKind != JsonValueKind.Object) return null;
                if (Text(leaf, "name") != node) continue;
                var leafType = LeafType(leaf, node);
                if (leafType is null || (type is not null && !type.Equals(leafType, StringComparison.OrdinalIgnoreCase))) return null;
                type = leafType;
                if (leaf.TryGetProperty("provider-name", out var owner)
                    && (owner.ValueKind != JsonValueKind.String || (!string.IsNullOrEmpty(owner.GetString()) && owner.GetString() != entry.Name))) return null;
                matches++;
            }
        }
        // Without a recorded provider, equally named nodes remain ambiguous even
        // when their protocols agree. With it, require exactly one in that provider.
        return matches == 1 ? type : null;
    }
    private static readonly HashSet<string> LeafTypes = new(StringComparer.OrdinalIgnoreCase)
    { "Direct", "Shadowsocks", "ShadowsocksR", "Socks5", "Http", "Vmess", "Vless", "Trojan", "Hysteria", "Hysteria2", "Tuic", "WireGuard", "SSH", "Snell", "Mieru", "AnyTLS" };
    private static IEnumerable<JsonElement> Connections(JsonDocument document) => document.RootElement.TryGetProperty("connections", out var rows)
        && rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray() : [];
    private static async Task<JsonDocument> GetAsync(HttpClient api, string path, CancellationToken token)
    {
        using var response = await api.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, token).ConfigureAwait(false);
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false));
    }
    private static Task SendAsync(Stream stream, string text, CancellationToken token) => stream.WriteAsync(Encoding.ASCII.GetBytes(text), token).AsTask();
    private static int Status(string header)
    {
        var parts = header.Split(' ', 3);
        return parts.Length >= 2 && parts[0] is "HTTP/1.1" or "HTTP/1.0" && int.TryParse(parts[1], out var code) ? code : 0;
    }
    private static async Task<string> HeadersAsync(Stream stream, CancellationToken token)
    {
        var bytes = new byte[16384]; var count = 0;
        // Byte-sized reads avoid eating any TLS bytes beyond a CONNECT header,
        // and bound origin consumption to headers only.
        while (count < bytes.Length)
        {
            await stream.ReadExactlyAsync(bytes.AsMemory(count++, 1), token).ConfigureAwait(false);
            if (count >= 4 && bytes[count - 4] == 13 && bytes[count - 3] == 10 && bytes[count - 2] == 13 && bytes[count - 1] == 10)
                return Encoding.ASCII.GetString(bytes, 0, count);
        }
        throw new IOException("HTTP headers too large.");
    }
    private static async Task SocksAsync(Stream stream, RoutingProbeTarget target, CancellationToken token)
    {
        await stream.WriteAsync(new byte[] { 5, 1, 0 }, token).ConfigureAwait(false);
        var greeting = new byte[2]; await stream.ReadExactlyAsync(greeting, token).ConfigureAwait(false);
        if (greeting[0] == 5 && greeting[1] != 0) throw new ProxyAuthenticationException();
        if (greeting[0] != 5) throw new IOException("Invalid SOCKS greeting.");
        var host = Encoding.ASCII.GetBytes(target.Host);
        await stream.WriteAsync(new byte[] { 5, 1, 0, 3, (byte)host.Length }.Concat(host).Concat(new byte[] { (byte)(target.Port >> 8), (byte)target.Port }).ToArray(), token).ConfigureAwait(false);
        var head = new byte[4]; await stream.ReadExactlyAsync(head, token).ConfigureAwait(false);
        if (head[0] != 5 || head[1] != 0) throw new IOException("SOCKS CONNECT rejected.");
        int length;
        if (head[3] == 1) length = 4;
        else if (head[3] == 4) length = 16;
        else if (head[3] == 3) { var size = new byte[1]; await stream.ReadExactlyAsync(size, token).ConfigureAwait(false); length = size[0]; }
        else throw new IOException("Invalid SOCKS address.");
        await stream.ReadExactlyAsync(new byte[length + 2], token).ConfigureAwait(false);
    }
}
