using System.Diagnostics;
using System.Text.Json;

namespace Dashboard;

internal sealed class HostMessageRouter(HostMessageHandlers handlers)
{
    private static readonly HashSet<string> CoreCommands = ["saveProfile", "start", "restart", "switchCore", "stop", "upgradeCore", "completeSetup"];
    private static readonly HashSet<string> WindowCommands = ["windowDrag", "windowResize", "windowToggleMaximize", "windowMinimize", "windowClose", "requestWindowState"];
    private static readonly HashSet<string> Edges = ["left", "right", "top", "bottom", "topLeft", "topRight", "bottomLeft", "bottomRight"];
    private static readonly HashSet<string> Options = ["startCoreOnLaunch", "minimizeToTray", "lightweightMode", "autostart"];
    internal const int MaximumMessageCharacters = 4 * 1024 * 1024;
    private readonly object _preferenceSavesLock = new();
    private readonly HashSet<Task> _pendingPreferenceSaves = [];

    public async Task RouteAsync(string json, Action<object> reply, Action<string, double>? performance = null)
    {
        var request = Parse(json);
        if (request.Type == "performance")
        {
            HostOperationLogger.Diagnostic("performance", $"frontend:{request.Name} durationMs={request.DurationMs:0}");
            performance?.Invoke($"frontend:{request.Name}", request.DurationMs!.Value);
            return;
        }
        if (WindowCommands.Contains(request.Type))
        {
            handlers.WindowCommand(request.Type, request.Edge);
            return;
        }
        var id = request.RequestId!;
        if (request.Type == "preferencesFlushed")
        {
            handlers.PreferencesFlushed(id, request.Value == true);
            return;
        }
        if (request.Type == "bootstrap")
        {
            var startedAt = Stopwatch.GetTimestamp();
            performance?.Invoke("bootstrap:received", 0);
            Task priorSaves;
            lock (_preferenceSavesLock) priorSaves = Task.WhenAll(_pendingPreferenceSaves);
            // Snapshot accepted writes only. Later saves must not prolong this bootstrap,
            // and failed writes still release the barrier with the last committed snapshot.
            await priorSaves;
            performance?.Invoke("bootstrap:preferenceBarrier", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            var snapshotStartedAt = Stopwatch.GetTimestamp();
            var snapshot = new BootstrapReply(id, handlers.BuildState(), handlers.Preferences());
            performance?.Invoke("bootstrap:snapshot", Stopwatch.GetElapsedTime(snapshotStartedAt).TotalMilliseconds);
            reply(snapshot);
            performance?.Invoke("bootstrap:replied", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return;
        }
        var preferenceSave = request.Type == "saveDashboardPreferences"
            ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        if (preferenceSave is not null)
            lock (_preferenceSavesLock) _pendingPreferenceSaves.Add(preferenceSave.Task);
        CommandResult result;
        try
        {
            switch (request.Type)
            {
                case "requestState": result = CommandResult.Completed(); break;
                case "chooseCoreFile":
                case "chooseConfigFile":
                    var path = handlers.ChooseFile(request.CoreType!.Value, request.Type == "chooseConfigFile");
                    result = path is null ? new("cancelled", "cancelled") : new("completed", "fileSelected", Path: path);
                    break;
                case "openCoreLocation":
                case "openConfigLocation":
                    handlers.OpenLocation(request.CoreType!.Value, request.Type == "openConfigLocation");
                    result = CommandResult.Completed(); break;
                case "checkAppUpdate":
                    await handlers.CheckAppUpdateAsync(); result = CommandResult.Completed(); break;
                case "openAppRelease": handlers.OpenAppRelease(); result = CommandResult.Completed(); break;
                case "openCoreRepository": handlers.OpenCoreRepository(); result = CommandResult.Completed(); break;
                case "openCoreRelease": handlers.OpenCoreRelease(); result = CommandResult.Completed(); break;
                default: result = await handlers.ExecuteAsync(request); break;
            }
        }
        catch (Exception error)
        {
            HostOperationLogger.Error("host-bridge", "Host command failed.", error);
            result = CommandResult.Failed("operationFailed", error.Message);
        }
        finally
        {
            if (preferenceSave is not null)
            {
                lock (_preferenceSavesLock) _pendingPreferenceSaves.Remove(preferenceSave.Task);
                preferenceSave.TrySetResult();
            }
        }
        reply(new HostReply(id, result, handlers.BuildState()));
        // The browser must receive its saved-profile acknowledgement before the
        // native owner asks it to flush drafts and decide whether it can exit.
        if (result.Status == "elevationRequired") handlers.RequestElevation();
    }

    internal static HostRequest Parse(string json)
    {
        if (json.Length > MaximumMessageCharacters) throw new ArgumentException("Host message exceeds size limit.");
        using var document = JsonDocument.Parse(json, new() { MaxDepth = 32 });
        ValidateUniqueFields(document.RootElement);
        var request = JsonSerializer.Deserialize<HostRequest>(json, HostBridgeJson.JsonOptions)
            ?? throw new ArgumentException("Missing command.");
        if (request.ProtocolVersion != HostBridgeJson.ProtocolVersion) throw new ArgumentException("Unsupported host protocol version.");
        if (request.CoreType.HasValue && !Enum.IsDefined(request.CoreType.Value)) throw new ArgumentException("Unknown core kind.");
        if (request.ExpectedRevision is < 0 || request.ExpectedRuntimeEpoch is < 0) throw new ArgumentException("Invalid operation revision.");
        if (request.Type == "performance")
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100 || request.Name.Any(char.IsControl)
                || request.DurationMs is null || !double.IsFinite(request.DurationMs.Value) || request.DurationMs < 0)
                throw new ArgumentException("Invalid performance event.");
            return request;
        }
        if (WindowCommands.Contains(request.Type))
        {
            if (request.Type == "windowResize" && (request.Edge is null || !Edges.Contains(request.Edge))) throw new ArgumentException("Invalid resize edge.");
            return request;
        }
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128) throw new ArgumentException("A requestId is required.");
        if (request.Type == "cacheProxyIcons")
        {
            if (request.CoreType is null || request.ExpectedRuntimeEpoch is null || request.IconUrls is null
                || request.IconUrls.Count > ProxyGroupIconCache.MaxCacheFiles
                || request.IconUrls.Any(url => url is null || url.Length > 8192
                    || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                || document.RootElement.EnumerateObject().Any(p => p.Name is not
                    ("protocolVersion" or "type" or "requestId" or "coreType" or "expectedRuntimeEpoch" or "iconUrls")))
                throw new ArgumentException("Icon caching requires its core session and HTTP(S) URLs only.");
            return request;
        }
        if (document.RootElement.TryGetProperty("iconUrls", out _))
            throw new ArgumentException("Unexpected icon cache payload.");
        if (request.Type is "testRouting" or "getNetworkDiagnostics" or "setNetworkDiagnostics")
        {
            if (request.CoreType is null || request.ExpectedRuntimeEpoch is null
                || document.RootElement.EnumerateObject().Any(p => p.Name is not ("protocolVersion" or "type" or "requestId" or "coreType" or "expectedRuntimeEpoch")
                    && !(request.Type == "testRouting" && p.Name == "targetIds")
                    && !(request.Type == "setNetworkDiagnostics" && p.Name == "networkDiagnostics")))
                throw new ArgumentException("Diagnostics require their core type, runtime epoch and command payload only.");
            if (request.Type == "testRouting"
                && (!RoutingProbe.ValidTargetIds(request.TargetIds)
                    || (document.RootElement.TryGetProperty("targetIds", out var ids) && ids.ValueKind != JsonValueKind.Array)))
                throw new ArgumentException("Invalid routing targets.");
            if (request.Type == "setNetworkDiagnostics" && !NetworkDiagnosticsSnapshots.IsValid(request.NetworkDiagnostics))
                throw new ArgumentException("Invalid network diagnostics snapshot.");
            return request;
        }
        if (document.RootElement.TryGetProperty("targetIds", out _) || document.RootElement.TryGetProperty("networkDiagnostics", out _))
            throw new ArgumentException("Unexpected diagnostics payload.");
        if (CoreCommands.Contains(request.Type))
        {
            if (request.CoreType is null) throw new ArgumentException("A core type is required.");
            if (request.Draft is not null && request.ExpectedRevision is null) throw new ArgumentException("A draft requires its expected revision.");
            if (request.Type == "saveProfile" && request.Draft is null) throw new ArgumentException("A profile draft is required.");
            if (request.Type == "upgradeCore" && request.Draft is not null) throw new ArgumentException("An upgrade cannot change its target profile draft.");
            return request;
        }
        switch (request.Type)
        {
            case "chooseCoreFile":
            case "chooseConfigFile":
            case "openCoreLocation":
            case "openConfigLocation":
                if (request.CoreType is null) throw new ArgumentException("A core type is required.");
                break;
            case "setDesktopOption":
                if (request.Option is null || !Options.Contains(request.Option) || request.Value is null) throw new ArgumentException("Invalid desktop option.");
                break;
            case "preferencesFlushed":
                if (request.Value is null) throw new ArgumentException("Missing flush result.");
                break;
            case "saveDashboardPreferences":
                if (request.Preferences is null || request.Preferences.Any(item => !item.Key.StartsWith("config/", StringComparison.Ordinal) || item.Value is null))
                    throw new ArgumentException("Invalid preferences.");
                break;
            case "bootstrap": case "requestState": case "refreshCoreMetadata": case "checkAppUpdate": case "openAppRelease": case "openCoreRepository": case "openCoreRelease": break;
            default: throw new ArgumentException("Unknown host command.");
        }
        return request;
    }

    private static void ValidateUniqueFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("Duplicate command field.");
                ValidateUniqueFields(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ValidateUniqueFields(child);
    }
}

internal sealed class HostMessageHandlers
{
    public required Func<HostRequest, Task<CommandResult>> ExecuteAsync { get; init; }
    public required Func<DashboardState> BuildState { get; init; }
    public required Func<IReadOnlyDictionary<string, string>> Preferences { get; init; }
    public required Func<CoreKind, bool, string?> ChooseFile { get; init; }
    public required Action<CoreKind, bool> OpenLocation { get; init; }
    public required Action<string, string?> WindowCommand { get; init; }
    public required Func<Task> CheckAppUpdateAsync { get; init; }
    public required Action OpenAppRelease { get; init; }
    public required Action OpenCoreRepository { get; init; }
    public required Action OpenCoreRelease { get; init; }
    public Action<string, bool> PreferencesFlushed { get; init; } = (_, _) => { };
    public Action RequestElevation { get; init; } = () => { };
}
