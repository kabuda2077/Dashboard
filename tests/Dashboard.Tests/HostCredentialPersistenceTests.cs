using System.Text.Json;
using Dashboard;

namespace Dashboard.Tests;

public sealed class HostCredentialPersistenceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "Dashboard.HostCredentialPersistenceTests",
        Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_temporaryDirectory, "settings.json");

    [Fact]
    public void LegacyPlaintextSecretsMigrateToProtectedOnlyStorage()
    {
        WriteJson(new
        {
            SetupCompleted = true,
            Secret = "mihomo-legacy",
            SingBoxSecret = "sing-box-legacy"
        });
        var protector = new FakeSecretProtector();

        var settings = AppSettings.Load(SettingsPath, protector);

        Assert.Equal("mihomo-legacy", settings.Secret);
        Assert.Equal("sing-box-legacy", settings.SingBoxSecret);
        Assert.False(settings.SecretDecryptionFailed);
        Assert.False(settings.SingBoxSecretDecryptionFailed);
        Assert.Equal("dpapi:mihomo-legacy", ReadString(nameof(AppSettings.ProtectedSecret)));
        Assert.Equal("dpapi:sing-box-legacy", ReadString(nameof(AppSettings.ProtectedSingBoxSecret)));
        AssertPropertyAbsent(nameof(AppSettings.Secret));
        AssertPropertyAbsent(nameof(AppSettings.SingBoxSecret));
    }

    [Fact]
    public void UnreadableProtectedValueUsesVerifiedLegacyPlaintextMigrationSource()
    {
        WriteJson(new
        {
            SetupCompleted = true,
            Secret = "recoverable-legacy-value",
            ProtectedSecret = "dpapi:unreadable"
        });
        var settings = AppSettings.Load(
            SettingsPath,
            new FakeSecretProtector("dpapi:unreadable"));

        Assert.Equal("recoverable-legacy-value", settings.Secret);
        Assert.False(settings.SecretDecryptionFailed);
        Assert.Equal("dpapi:recoverable-legacy-value", ReadString(nameof(AppSettings.ProtectedSecret)));
        AssertPropertyAbsent(nameof(AppSettings.Secret));
    }

    [Fact]
    public void FailedRoundTripLeavesRecoverableLegacyFileUntouched()
    {
        const string originalJson = "{\"SetupCompleted\":true,\"Secret\":\"only-recoverable-value\",\"ProtectedSecret\":\"dpapi:unreadable\"}";
        Directory.CreateDirectory(_temporaryDirectory);
        File.WriteAllText(SettingsPath, originalJson);

        var exception = Assert.Throws<AppSettingsMigrationException>(() =>
            AppSettings.Load(SettingsPath, new MismatchingSecretProtector()));

        Assert.IsType<System.Security.Cryptography.CryptographicException>(exception.InnerException);
        Assert.Equal(originalJson, File.ReadAllText(SettingsPath));
        Assert.Empty(Directory.EnumerateFiles(_temporaryDirectory, "*.tmp"));
    }

    [Fact]
    public void CorruptExistingJsonThrowsAndRemainsUntouched()
    {
        const string corruptJson = "{ this is not valid JSON";
        Directory.CreateDirectory(_temporaryDirectory);
        File.WriteAllText(SettingsPath, corruptJson);

        var exception = Assert.Throws<AppSettingsLoadException>(() =>
            AppSettings.Load(SettingsPath, new FakeSecretProtector()));

        Assert.IsType<JsonException>(exception.InnerException);
        Assert.Equal(corruptJson, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void NullExistingJsonThrowsAndRemainsUntouched()
    {
        const string nullJson = "null";
        Directory.CreateDirectory(_temporaryDirectory);
        File.WriteAllText(SettingsPath, nullJson);

        var exception = Assert.Throws<AppSettingsLoadException>(() =>
            AppSettings.Load(SettingsPath, new FakeSecretProtector()));

        Assert.IsType<JsonException>(exception.InnerException);
        Assert.Equal(nullJson, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void UndecryptableSecretIsExplicitAndUnrelatedSavePreservesOriginalValue()
    {
        WriteJson(new
        {
            SetupCompleted = true,
            CoreType = AppSettings.CoreTypeMihomo,
            ProtectedSecret = "dpapi:unreadable",
            ProtectedSingBoxSecret = "dpapi:sing-box-ok"
        });
        var protector = new FakeSecretProtector("dpapi:unreadable");

        var settings = AppSettings.Load(SettingsPath, protector);

        Assert.True(settings.SecretDecryptionFailed);
        Assert.True(settings.ActiveSecretDecryptionFailed);
        Assert.False(settings.SingBoxSecretDecryptionFailed);
        Assert.Equal("", settings.Secret);
        Assert.Equal("sing-box-ok", settings.SingBoxSecret);

        // Core.collect may assign every field during an unrelated save. This must not
        // turn an unavailable credential into a deliberate empty or replacement value.
        settings.Secret = "value-from-automatic-collection";
        settings.SetupCompleted = false;
        settings.Save();

        Assert.True(settings.SecretDecryptionFailed);
        Assert.Equal("dpapi:unreadable", settings.ProtectedSecret);
        Assert.Equal("dpapi:unreadable", ReadString(nameof(AppSettings.ProtectedSecret)));
        AssertPropertyAbsent(nameof(AppSettings.Secret));
    }

    [Fact]
    public void ExplicitEmptyReplacementClearsOnlySelectedCoreFailure()
    {
        WriteJson(new
        {
            SetupCompleted = true,
            ProtectedSecret = "dpapi:mihomo-unreadable",
            ProtectedSingBoxSecret = "dpapi:sing-box-unreadable"
        });
        var protector = new FakeSecretProtector(
            "dpapi:mihomo-unreadable",
            "dpapi:sing-box-unreadable");
        var settings = AppSettings.Load(SettingsPath, protector);

        settings.ReplaceSecret(isSingBox: false, "");
        settings.Save();

        Assert.False(settings.SecretDecryptionFailed);
        Assert.True(settings.SingBoxSecretDecryptionFailed);
        Assert.Equal("", settings.Secret);
        Assert.Equal("dpapi:", settings.ProtectedSecret);
        Assert.Equal("dpapi:sing-box-unreadable", settings.ProtectedSingBoxSecret);
        AssertPropertyAbsent(nameof(AppSettings.Secret));
        AssertPropertyAbsent(nameof(AppSettings.SingBoxSecret));
    }

    [Fact]
    public void FailedHostTransactionRestoresFailureAuthorizationAndProtectedOriginal()
    {
        WriteJson(new
        {
            SetupCompleted = true,
            ProtectedSecret = "dpapi:mihomo-unreadable",
            ProtectedSingBoxSecret = "dpapi:sing-box-ok"
        });
        var settings = AppSettings.Load(
            SettingsPath,
            new FakeSecretProtector("dpapi:mihomo-unreadable"));

        Assert.Throws<IOException>(() => HostSettingsTransaction.Execute(
            settings,
            () => settings.ReplaceSecret(isSingBox: false, "confirmed-replacement"),
            () => throw new IOException("Simulated persistence failure.")));

        Assert.True(settings.SecretDecryptionFailed);
        Assert.Equal("", settings.Secret);
        Assert.Equal("dpapi:mihomo-unreadable", settings.ProtectedSecret);
        Assert.False(settings.SingBoxSecretDecryptionFailed);
        Assert.Equal("sing-box-ok", settings.SingBoxSecret);
    }

    [Fact]
    public void ActiveFailureTracksSelectedCore()
    {
        WriteJson(new
        {
            SetupCompleted = true,
            CoreType = AppSettings.CoreTypeSingBox,
            ProtectedSecret = "dpapi:mihomo-ok",
            ProtectedSingBoxSecret = "dpapi:sing-box-unreadable"
        });
        var settings = AppSettings.Load(
            SettingsPath,
            new FakeSecretProtector("dpapi:sing-box-unreadable"));

        Assert.False(settings.SecretDecryptionFailed);
        Assert.True(settings.SingBoxSecretDecryptionFailed);
        Assert.True(settings.ActiveSecretDecryptionFailed);

        settings.CoreType = AppSettings.CoreTypeMihomo;

        Assert.False(settings.ActiveSecretDecryptionFailed);
    }

    [Fact]
    public void FailedSaveKeepsExistingFileAndProtectedValuesRecoverable()
    {
        var protector = new FakeSecretProtector();
        var settings = AppSettings.Load(SettingsPath, protector);
        settings.ReplaceSecret(isSingBox: false, "original");
        settings.ReplaceSecret(isSingBox: true, "other-original");
        settings.Save();
        var originalJson = File.ReadAllText(SettingsPath);
        var originalProtectedSecret = settings.ProtectedSecret;
        var originalProtectedSingBoxSecret = settings.ProtectedSingBoxSecret;

        settings.Secret = "replacement";
        settings.SingBoxSecret = "other-replacement";
        using (File.Open(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var exception = Record.Exception(() => settings.Save());
            Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        }

        Assert.Equal(originalProtectedSecret, settings.ProtectedSecret);
        Assert.Equal(originalProtectedSingBoxSecret, settings.ProtectedSingBoxSecret);
        Assert.Equal(originalJson, File.ReadAllText(SettingsPath));
        Assert.DoesNotContain("replacement", originalJson, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private void WriteJson(object value)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(value));
    }

    private string ReadString(string propertyName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        return document.RootElement.GetProperty(propertyName).GetString() ?? "";
    }

    private void AssertPropertyAbsent(string propertyName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.False(document.RootElement.TryGetProperty(propertyName, out _));
    }

    private sealed class MismatchingSecretProtector : ISecretProtector
    {
        public string Protect(string secret) => SecretProtector.ProtectedPrefix + secret;

        public string Unprotect(string protectedSecret)
        {
            if (protectedSecret == "dpapi:unreadable")
            {
                throw new InvalidOperationException("Simulated unreadable original.");
            }

            return "different-value";
        }
    }

    private sealed class FakeSecretProtector(params string[] unreadableValues) : ISecretProtector
    {
        private readonly HashSet<string> _unreadableValues = new(unreadableValues, StringComparer.Ordinal);

        public string Protect(string secret) => SecretProtector.ProtectedPrefix + secret;

        public string Unprotect(string protectedSecret)
        {
            if (_unreadableValues.Contains(protectedSecret))
            {
                throw new InvalidOperationException("Simulated credential belonging to another user or machine.");
            }

            if (!protectedSecret.StartsWith(SecretProtector.ProtectedPrefix, StringComparison.Ordinal))
            {
                throw new FormatException("Unsupported test credential.");
            }

            return protectedSecret[SecretProtector.ProtectedPrefix.Length..];
        }
    }
}
