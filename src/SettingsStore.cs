using System.Collections.ObjectModel;
using System.Text.Json;

namespace Dashboard;

// The only writer. A candidate becomes visible only after its atomic disk commit.
internal sealed class SettingsStore
{
    private readonly string _path;
    private readonly ISecretProtector _protector;
    private readonly Action<string, string> _persist;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private AppSettings _current;
    public AppSettings Current => Volatile.Read(ref _current);
    internal string FilePath => _path;
    public event EventHandler? Changed;

    public SettingsStore(string? appDirectory = null, ISecretProtector? protector = null,
        Action<string, string>? persist = null)
    {
        var directory = Path.GetFullPath(appDirectory ?? AppSettings.AppDirectory);
        _path = Path.Combine(directory, "settings.json");
        _protector = protector ?? DpapiSecretProtector.Instance;
        _persist = persist ?? WriteAtomically;
        if (!File.Exists(_path))
        {
            _current = AppSettings.CreateDefault(directory);
            _persist(_path, JsonSerializer.Serialize(_current, AppSettings.JsonOptions));
            return;
        }
        try
        {
            var json = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schema)
                || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != AppSettings.CurrentSchemaVersion)
                throw new JsonException("Unsupported settings schema.");
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)
                ?? throw new JsonException("Settings cannot be null.");
            Validate(loaded);
            _current = loaded.WithProfile(CoreKind.Mihomo, RestoreSecret(loaded.Profiles.Mihomo))
                .WithProfile(CoreKind.SingBox, RestoreSecret(loaded.Profiles.SingBox)) with
            {
                DashboardPreferences = Freeze(loaded.DashboardPreferences)
            };
        }
        catch (Exception error) { throw new AppSettingsLoadException(_path, error); }
    }

    private CoreProfile RestoreSecret(CoreProfile profile)
    {
        try { return profile with { Secret = _protector.Unprotect(profile.ProtectedSecret), SecretDecryptionFailed = false }; }
        catch { return profile with { Secret = "", SecretDecryptionFailed = true }; }
    }

    public Task<AppSettings> UpdateProfileAsync(CoreKind kind, long expectedRevision, CoreProfileEdit edit, CancellationToken token = default) =>
        UpdateAsync(current =>
        {
            var previous = current.Profile(kind);
            if (previous.Revision != expectedRevision) throw new SettingsConflictException();
            ArgumentNullException.ThrowIfNull(edit);
            var profile = previous with
            {
                Revision = checked(previous.Revision + 1),
                ExePath = NormalizePath(edit.ExePath),
                ConfigPath = NormalizePath(edit.ConfigPath),
                ApiUrl = NormalizeApiUrl(edit.ApiUrl)
            };
            if (edit.Secret.Action == "replace")
            {
                if (edit.Secret.Value is null) throw new ArgumentException("Replacing a Secret requires an explicit value.");
                var protectedValue = _protector.Protect(edit.Secret.Value);
                if (_protector.Unprotect(protectedValue) != edit.Secret.Value)
                    throw new InvalidOperationException("Secret protection verification failed.");
                profile = profile with { ProtectedSecret = protectedValue, Secret = edit.Secret.Value, SecretDecryptionFailed = false };
            }
            else if (edit.Secret.Action != "keep" || edit.Secret.Value is not null)
                throw new ArgumentException("Secret edit must be keep or an explicit replacement.");
            return current.WithProfile(kind, profile);
        }, token);

    public Task<AppSettings> SetActiveCoreAsync(CoreKind kind, CancellationToken token = default) =>
        UpdateAsync(current => { _ = current.Profile(kind); return current with { ActiveCoreKind = kind }; }, token);

    public Task<AppSettings> CompleteSetupAsync(CancellationToken token = default) =>
        UpdateAsync(current => current with { SetupCompleted = true }, token);

    public Task<AppSettings> SetOptionAsync(string option, bool value, CancellationToken token = default) =>
        UpdateAsync(current => current with
        {
            DesktopOptions = option switch
            {
                "startCoreOnLaunch" => current.DesktopOptions with { StartCoreOnLaunch = value },
                "minimizeToTray" => current.DesktopOptions with { MinimizeToTray = value },
                "lightweightMode" => current.DesktopOptions with { LightweightMode = value },
                "autostart" => current.DesktopOptions with { Autostart = value },
                _ => throw new ArgumentException("Unknown desktop option.")
            }
        }, token);

    public Task<AppSettings> SavePreferencesAsync(IReadOnlyDictionary<string, string> preferences, CancellationToken token = default)
    {
        var copy = Freeze(preferences);
        ValidatePreferences(copy);
        return UpdateAsync(current => current with { DashboardPreferences = copy, PreferencesRevision = checked(current.PreferencesRevision + 1) }, token);
    }

    private async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken token)
    {
        AppSettings next;
        await _writer.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            // DPAPI, serialization and flush must not block the window's message loop.
            next = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var candidate = update(Current);
                Validate(candidate);
                var json = JsonSerializer.Serialize(candidate, AppSettings.JsonOptions);
                token.ThrowIfCancellationRequested();
                // Once commit starts, do not abandon it midway or report cancellation as rollback.
                _persist(_path, json);
                Volatile.Write(ref _current, candidate);
                return candidate;
            }, token).ConfigureAwait(false);
        }
        finally { _writer.Release(); }
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception error) { HostOperationLogger.Error("settings", "Settings observer failed after a successful commit.", error); }
        return next;
    }

    private static IReadOnlyDictionary<string, string> Freeze(IReadOnlyDictionary<string, string> values) =>
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(values, StringComparer.Ordinal));

    internal static string NormalizeApiUrl(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("API 地址必须是无内嵌凭证的 HTTP(S) 根地址。");
        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static string NormalizePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Path.GetFullPath(value.Trim());
    }

    private static void Validate(AppSettings settings)
    {
        if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion || !Enum.IsDefined(settings.ActiveCoreKind) || settings.PreferencesRevision < 0)
            throw new JsonException("Unsupported settings schema/core kind.");
        if (settings.Profiles is null || settings.DesktopOptions is null || settings.DashboardPreferences is null)
            throw new JsonException("Missing settings sections.");
        foreach (var kind in Enum.GetValues<CoreKind>())
        {
            var profile = settings.Profile(kind) ?? throw new JsonException("Missing core profile.");
            if (profile.Revision < 0 || profile.ProtectedSecret is null) throw new JsonException("Invalid profile.");
            _ = NormalizePath(profile.ExePath); _ = NormalizePath(profile.ConfigPath); _ = NormalizeApiUrl(profile.ApiUrl);
        }
        ValidatePreferences(settings.DashboardPreferences);
    }

    private static void ValidatePreferences(IReadOnlyDictionary<string, string> values)
    {
        if (values.Any(item => !item.Key.StartsWith("config/", StringComparison.Ordinal) || item.Value is null))
            throw new ArgumentException("Only string config/* preferences may be persisted.");
    }

    internal static void WriteAtomically(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
