using System.Text.Json;

namespace Dashboard;

internal sealed class HostMessageRouter(HostMessageHandlers handlers)
{
    private static readonly HashSet<string> CoreCommands = ["saveProfile", "start", "restart", "switchCore", "stop", "upgradeCore", "completeSetup"];
    private static readonly HashSet<string> WindowCommands = ["windowDrag", "windowResize", "windowToggleMaximize", "windowMinimize", "windowClose", "requestWindowState"];
    private static readonly HashSet<string> Edges = ["left", "right", "top", "bottom", "topLeft", "topRight", "bottomLeft", "bottomRight"];
    private static readonly HashSet<string> Options = ["startCoreOnLaunch", "minimizeToTray", "lightweightMode", "autostart"];
    internal const int MaximumMessageCharacters = 4 * 1024 * 1024;

    public async Task RouteAsync(string json, Action<object> reply)
    {
        var request = Parse(json);
        if (request.Type == "performance")
        {
            HostOperationLogger.Diagnostic("performance", $"frontend:{request.Name} durationMs={request.DurationMs:0}");
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
            reply(new BootstrapReply(id, handlers.BuildState(), handlers.Preferences()));
            return;
        }
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
                default: result = await handlers.ExecuteAsync(request); break;
            }
        }
        catch (Exception error)
        {
            HostOperationLogger.Error("host-bridge", "Host command failed.", error);
            result = CommandResult.Failed("operationFailed", error.Message);
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
        if (request.ConfirmUnverified && (request.Type != "upgradeCore" || request.ExpectedRevision is null || request.ExpectedRuntimeEpoch is null))
            throw new ArgumentException("An upgrade confirmation requires its original profile revision and runtime epoch.");
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
            case "bootstrap": case "requestState": case "refreshCoreMetadata": case "checkAppUpdate": case "openAppRelease": case "openCoreRepository": break;
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
    public Action<string, bool> PreferencesFlushed { get; init; } = (_, _) => { };
    public Action RequestElevation { get; init; } = () => { };
}
