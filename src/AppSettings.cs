using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dashboard;

public sealed class AppSettingsLoadException : Exception
{
    public AppSettingsLoadException(string settingsPath, Exception innerException)
        : base($"The existing settings file '{settingsPath}' could not be read. The original file was retained.", innerException)
    {
        SettingsPath = settingsPath;
    }

    public string SettingsPath { get; }
}

public sealed class AppSettingsMigrationException : Exception
{
    public AppSettingsMigrationException(string settingsPath, Exception innerException)
        : base($"Settings were loaded, but credential migration could not be persisted to '{settingsPath}'. The original file was retained.", innerException)
    {
        SettingsPath = settingsPath;
    }

    public string SettingsPath { get; }
}

public sealed class AppSettings
{
    private static readonly object FileSaveLock = new();
    private readonly object _saveLock = new();
    private ISecretProtector _secretProtector = DpapiSecretProtector.Instance;
    private string _settingsPath = SettingsPath;
    private string _secret = "";
    private string _singBoxSecret = "";
    private bool _secretDecryptionFailed;
    private bool _singBoxSecretDecryptionFailed;
    public const string CoreTypeMihomo = "mihomo";
    public const string CoreTypeSingBox = "sing-box";
    private const string AppDirectoryName = "Dashboard";
    private const string LegacyAppDirectoryName = "MihomoDashboard";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string CoreType { get; set; } = CoreTypeMihomo;
    public string CorePath { get; set; } = DefaultMihomoCorePath;
    public string ConfigPath { get; set; } = DefaultConfigPath;
    public string DashboardApiUrl { get; set; } = "http://127.0.0.1:9090";
    [JsonIgnore]
    public string Secret
    {
        get => _secret;
        set => _secret = value ?? "";
    }

    public string? ProtectedSecret { get; set; }
    public string SingBoxCorePath { get; set; } = DefaultSingBoxCorePath;
    public string SingBoxConfigPath { get; set; } = DefaultSingBoxConfigPath;
    public string SingBoxApiUrl { get; set; } = "http://127.0.0.1:9090";
    [JsonIgnore]
    public string SingBoxSecret
    {
        get => _singBoxSecret;
        set => _singBoxSecret = value ?? "";
    }

    public string? ProtectedSingBoxSecret { get; set; }
    public bool SetupCompleted { get; set; }
    public bool StartCoreOnLaunch { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool LightweightMode { get; set; } = true;
    public bool Autostart { get; set; }
    public Dictionary<string, string>? DashboardSettings { get; set; }

    [JsonIgnore]
    public bool SecretDecryptionFailed => _secretDecryptionFailed;

    [JsonIgnore]
    public bool SingBoxSecretDecryptionFailed => _singBoxSecretDecryptionFailed;

    [JsonIgnore]
    public bool ActiveSecretDecryptionFailed => IsSingBox
        ? SingBoxSecretDecryptionFailed
        : SecretDecryptionFailed;

    [JsonIgnore]
    public bool IsSingBox => string.Equals(NormalizeCoreType(CoreType), CoreTypeSingBox, StringComparison.Ordinal);

    [JsonIgnore]
    public string CoreDisplayName => IsSingBox ? "sing-box" : "mihomo";

    [JsonIgnore]
    public string CoreTitle => CoreTitleFor(CoreType);

    public static string CoreTitleFor(string? coreType)
    {
        return string.Equals(NormalizeCoreType(coreType), CoreTypeSingBox, StringComparison.Ordinal)
            ? "sing-box"
            : "Mihomo Core";
    }

    [JsonIgnore]
    public string ActiveCorePath
    {
        get => IsSingBox ? SingBoxCorePath : CorePath;
        set
        {
            if (IsSingBox)
            {
                SingBoxCorePath = value;
            }
            else
            {
                CorePath = value;
            }
        }
    }

    [JsonIgnore]
    public string ActiveConfigPath
    {
        get => IsSingBox ? SingBoxConfigPath : ConfigPath;
        set
        {
            if (IsSingBox)
            {
                SingBoxConfigPath = value;
            }
            else
            {
                ConfigPath = value;
            }
        }
    }

    [JsonIgnore]
    public string ActiveDashboardApiUrl
    {
        get => IsSingBox ? SingBoxApiUrl : DashboardApiUrl;
        set
        {
            if (IsSingBox)
            {
                SingBoxApiUrl = value;
            }
            else
            {
                DashboardApiUrl = value;
            }
        }
    }

    [JsonIgnore]
    public string ActiveSecret
    {
        get => IsSingBox ? SingBoxSecret : Secret;
        set
        {
            if (IsSingBox)
            {
                SingBoxSecret = value;
            }
            else
            {
                Secret = value;
            }
        }
    }

    public static string AppDirectory => ResolveAppDirectory();

    public static string SettingsDirectory => AppDirectory;

    public static string ResourceDirectory => Path.Combine(AppDirectory, "resources");

    public static string LogDirectory => Path.Combine(ResourceDirectory, "logs");

    public static string WebViewUserDataDirectory => Path.Combine(ResourceDirectory, "EBWebView");

    private static string LegacyDashboardSettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDirectoryName);

    private static string LegacyMihomoSettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyAppDirectoryName);

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    private static string LegacyDashboardSettingsPath => Path.Combine(LegacyDashboardSettingsDirectory, "settings.json");

    private static string LegacyMihomoSettingsPath => Path.Combine(LegacyMihomoSettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        Directory.CreateDirectory(SettingsDirectory);
        MigrateLegacySettingsFile();
        return Load(SettingsPath, DpapiSecretProtector.Instance);
    }

    internal static AppSettings Load(string settingsPath, ISecretProtector secretProtector)
    {
        if (!File.Exists(settingsPath))
        {
            var defaults = CreateForPath(settingsPath, secretProtector);
            defaults.Save();
            return defaults;
        }

        string json;
        AppSettings settings;
        try
        {
            json = File.ReadAllText(settingsPath);
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new JsonException("The settings document root cannot be null.");
        }
        catch (Exception exception)
        {
            throw new AppSettingsLoadException(settingsPath, exception);
        }

        settings._settingsPath = settingsPath;
        settings._secretProtector = secretProtector;
        var shouldSave = settings.RestoreSecrets(json);
        if (!TryReadBoolProperty(json, nameof(SetupCompleted), out _))
        {
            settings.SetupCompleted = true;
            shouldSave = true;
        }
        settings.CoreType = NormalizeCoreType(settings.CoreType);
        shouldSave |= settings.MigrateDefaultConfigPath();
        if (shouldSave)
        {
            try
            {
                settings.Save();
            }
            catch (Exception exception)
            {
                throw new AppSettingsMigrationException(settingsPath, exception);
            }
        }
        return settings;
    }

    private static AppSettings CreateForPath(string settingsPath, ISecretProtector secretProtector) => new()
    {
        _settingsPath = settingsPath,
        _secretProtector = secretProtector
    };

    public void Save()
    {
        lock (_saveLock)
        {
            lock (FileSaveLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? ".");
                var previousProtectedSecret = ProtectedSecret;
                var previousProtectedSingBoxSecret = ProtectedSingBoxSecret;
                var previousCoreType = CoreType;
                try
                {
                    if (!SecretDecryptionFailed)
                    {
                        ProtectedSecret = ProtectAndVerify(Secret);
                    }
                    if (!SingBoxSecretDecryptionFailed)
                    {
                        ProtectedSingBoxSecret = ProtectAndVerify(SingBoxSecret);
                    }
                    CoreType = NormalizeCoreType(CoreType);
                    var json = JsonSerializer.Serialize(this, JsonOptions);
                    WriteSettingsAtomically(_settingsPath, json);
                }
                catch
                {
                    ProtectedSecret = previousProtectedSecret;
                    ProtectedSingBoxSecret = previousProtectedSingBoxSecret;
                    CoreType = previousCoreType;
                    throw;
                }
            }
        }
    }

    private string ProtectAndVerify(string secret)
    {
        var protectedValue = _secretProtector.Protect(secret);
        var roundTripValue = _secretProtector.Unprotect(protectedValue);
        if (!string.Equals(secret, roundTripValue, StringComparison.Ordinal))
        {
            throw new CryptographicException("Protected secret verification failed.");
        }

        return protectedValue;
    }

    public void ReplaceSecret(bool isSingBox, string value)
    {
        if (isSingBox)
        {
            _singBoxSecret = value ?? "";
            _singBoxSecretDecryptionFailed = false;
            return;
        }

        _secret = value ?? "";
        _secretDecryptionFailed = false;
    }

    internal void RestoreSecretPersistenceState(
        bool isSingBox,
        string value,
        string? protectedValue,
        bool decryptionFailed)
    {
        if (isSingBox)
        {
            _singBoxSecret = value;
            ProtectedSingBoxSecret = protectedValue;
            _singBoxSecretDecryptionFailed = decryptionFailed;
            return;
        }

        _secret = value;
        ProtectedSecret = protectedValue;
        _secretDecryptionFailed = decryptionFailed;
    }

    internal void ExecuteSynchronized(Action action)
    {
        lock (_saveLock) action();
    }

    internal static void WriteSettingsAtomically(string path, string json)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string DefaultMihomoCorePath => Path.Combine(AppDirectory, "mihomo", "mihomo.exe");

    private static string DefaultConfigPath => Path.Combine(AppDirectory, "mihomo", "config.yaml");

    private static string DefaultSingBoxCorePath => Path.Combine(AppDirectory, "sing-box", "sing-box.exe");

    private static string DefaultSingBoxConfigPath => Path.Combine(AppDirectory, "sing-box", "config.json");

    private static string LegacyDefaultConfigPath => Path.Combine(AppDirectory, "config", "config.yaml");

    private static string ResolveAppDirectory()
    {
        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var trimmedBaseDirectory = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetFileName(trimmedBaseDirectory).Equals("EBWebView", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(trimmedBaseDirectory);
            var grandParent = parent?.Parent;
            if (parent is not null
                && Path.GetFileName(parent.FullName).Equals("resources", StringComparison.OrdinalIgnoreCase))
            {
                return parent.Parent?.FullName ?? baseDirectory;
            }

            if (parent is not null
                && grandParent is not null
                && Path.GetFileName(parent.FullName).Equals("runtime", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(grandParent.FullName).Equals("resources", StringComparison.OrdinalIgnoreCase))
            {
                return grandParent.Parent?.FullName ?? baseDirectory;
            }

            return parent?.FullName ?? baseDirectory;
        }

        return baseDirectory;
    }

    private static void MigrateLegacySettingsFile()
    {
        if (File.Exists(SettingsPath))
        {
            return;
        }

        var legacyPath = new[] { LegacyDashboardSettingsPath, LegacyMihomoSettingsPath }
            .FirstOrDefault(File.Exists);
        if (legacyPath is null)
        {
            return;
        }

        try
        {
            File.Copy(legacyPath, SettingsPath, overwrite: false);
        }
        catch
        {
        }
    }

    public static void MigrateLegacyDataDirectory(string directoryName, string targetDirectory)
    {
        var legacyDirectory = new[] { LegacyDashboardSettingsDirectory, LegacyMihomoSettingsDirectory }
            .Select(directory => Path.Combine(directory, directoryName))
            .FirstOrDefault(Directory.Exists);
        if (legacyDirectory is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
            foreach (var sourcePath in Directory.EnumerateFiles(legacyDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(legacyDirectory, sourcePath);
                var targetPath = Path.Combine(targetDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetDirectory);
                if (!File.Exists(targetPath))
                {
                    File.Copy(sourcePath, targetPath);
                }
            }
        }
        catch
        {
        }
    }

    public static void MigratePortableDataDirectory(string directoryName, string targetDirectory)
    {
        MigrateDataDirectory(Path.Combine(AppDirectory, directoryName), targetDirectory);
    }

    public static void MigrateResourceDataDirectory(string containerName, string directoryName, string targetDirectory)
    {
        MigrateDataDirectory(Path.Combine(ResourceDirectory, containerName, directoryName), targetDirectory);
    }

    private static void MigrateDataDirectory(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory) || IsSamePath(sourceDirectory, targetDirectory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
            foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
                var targetPath = Path.Combine(targetDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetDirectory);
                if (!File.Exists(targetPath))
                {
                    File.Move(sourcePath, targetPath);
                }
                else
                {
                    File.Delete(sourcePath);
                }
            }

            TryDeleteEmptyDirectory(sourceDirectory);
        }
        catch
        {
        }
    }

    private static void TryDeleteEmptyDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)
                && !Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any())
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private bool MigrateDefaultConfigPath()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath)
            || !IsSamePath(ConfigPath, LegacyDefaultConfigPath)
            || File.Exists(ConfigPath))
        {
            return false;
        }

        ConfigPath = DefaultConfigPath;
        return true;
    }

    public static string NormalizeCoreType(string? coreType)
    {
        return string.Equals(coreType, CoreTypeSingBox, StringComparison.OrdinalIgnoreCase)
            ? CoreTypeSingBox
            : CoreTypeMihomo;
    }

    private bool RestoreSecrets(string json)
    {
        var shouldSave = RestoreSecret(json, nameof(Secret), ProtectedSecret, isSingBox: false);
        shouldSave |= RestoreSecret(json, nameof(SingBoxSecret), ProtectedSingBoxSecret, isSingBox: true);
        return shouldSave;
    }

    private bool RestoreSecret(
        string json,
        string portablePropertyName,
        string? protectedValue,
        bool isSingBox)
    {
        var hasPortableSecret = TryReadStringProperty(json, portablePropertyName, out var portableSecret);

        if (!string.IsNullOrEmpty(protectedValue))
        {
            try
            {
                RestoreSecretPersistenceState(
                    isSingBox,
                    _secretProtector.Unprotect(protectedValue),
                    protectedValue,
                    decryptionFailed: false);
            }
            catch
            {
                if (hasPortableSecret)
                {
                    // The legacy field is the recoverable migration source. Load does not
                    // return until Save has protected and verified it successfully.
                    RestoreSecretPersistenceState(
                        isSingBox, portableSecret, protectedValue, decryptionFailed: false);
                }
                else
                {
                    RestoreSecretPersistenceState(isSingBox, "", protectedValue, decryptionFailed: true);
                }
            }

            return hasPortableSecret;
        }

        if (!hasPortableSecret)
        {
            return false;
        }

        RestoreSecretPersistenceState(isSingBox, portableSecret, protectedValue, decryptionFailed: false);
        return true;
    }

    private static bool TryReadStringProperty(string json, string propertyName, out string value)
    {
        value = "";
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return true;
    }

    private static bool TryReadBoolProperty(string json, string propertyName, out bool value)
    {
        value = false;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool IsSamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }
}
