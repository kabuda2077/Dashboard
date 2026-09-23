using System.Text.Json;

namespace Dashboard.Tests;

public sealed class HostCredentialRecoveryTests
{
    [Fact]
    public void CanonicalProfilePayloadUpdatesBothProfilesAndIgnoresActiveAliases()
    {
        var settings = new AppSettings
        {
            CoreType = AppSettings.CoreTypeMihomo,
            CorePath = "old-mihomo.exe",
            ConfigPath = "old-mihomo.yaml",
            DashboardApiUrl = "http://old-mihomo",
            Secret = "old-mihomo-secret",
            SingBoxCorePath = "old-sing-box.exe",
            SingBoxConfigPath = "old-sing-box.json",
            SingBoxApiUrl = "http://old-sing-box",
            SingBoxSecret = "old-sing-box-secret"
        };
        using var payload = JsonDocument.Parse("""
            {
              "coreType":"sing-box",
              "mihomoCorePath":" new-mihomo.exe ",
              "mihomoConfigPath":" new-mihomo.yaml ",
              "mihomoApiUrl":" http://new-mihomo ",
              "mihomoSecret":"new-mihomo-secret",
              "singBoxCorePath":" new-sing-box.exe ",
              "singBoxConfigPath":" new-sing-box.json ",
              "singBoxApiUrl":" http://new-sing-box ",
              "singBoxSecret":"new-sing-box-secret",
              "corePath":"ignored.exe",
              "configPath":"ignored.json",
              "apiUrl":"http://ignored",
              "secret":"ignored-secret"
            }
            """);

        DashboardHost.ApplyCoreProfileEdits(settings, payload.RootElement);

        Assert.Equal(AppSettings.CoreTypeSingBox, settings.CoreType);
        Assert.Equal("new-mihomo.exe", settings.CorePath);
        Assert.Equal("new-mihomo.yaml", settings.ConfigPath);
        Assert.Equal("http://new-mihomo", settings.DashboardApiUrl);
        Assert.Equal("new-mihomo-secret", settings.Secret);
        Assert.Equal("new-sing-box.exe", settings.SingBoxCorePath);
        Assert.Equal("new-sing-box.json", settings.SingBoxConfigPath);
        Assert.Equal("http://new-sing-box", settings.SingBoxApiUrl);
        Assert.Equal("new-sing-box-secret", settings.SingBoxSecret);
    }

    [Fact]
    public void OrdinarySavePayloadDoesNotReplaceUnreadableCredentials()
    {
        var settings = new AppSettings();
        settings.RestoreSecretPersistenceState(false, "", "dpapi:recoverable-original", true);
        using var payload = JsonDocument.Parse("""{"mihomoSecret":"","singBoxSecret":"new-box"}""");
        DashboardHost.ApplyCredentialEdit(settings, payload.RootElement, false);
        DashboardHost.ApplyCredentialEdit(settings, payload.RootElement, true);
        Assert.True(settings.SecretDecryptionFailed);
        Assert.Equal("dpapi:recoverable-original", settings.ProtectedSecret);
        Assert.Equal("new-box", settings.SingBoxSecret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("replacement")]
    public void ExplicitRecoverySupportsEmptyAndNonemptySecrets(string replacement)
    {
        var settings = new AppSettings();
        settings.RestoreSecretPersistenceState(false, "", "dpapi:original", true);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { mihomoSecret = replacement, replaceMihomoSecret = true }));
        DashboardHost.ApplyCredentialEdit(settings, payload.RootElement, false);
        Assert.False(settings.SecretDecryptionFailed);
        Assert.Equal(replacement, settings.Secret);
    }

    [Fact]
    public void FailedSaveRestoresRecoveryFlagAndOriginalCiphertext()
    {
        var settings = new AppSettings();
        settings.RestoreSecretPersistenceState(false, "", "dpapi:original", true);
        Assert.Throws<IOException>(() => HostSettingsTransaction.Execute(settings,
            () => settings.ReplaceSecret(false, "replacement"),
            () => throw new IOException("save failed")));
        Assert.True(settings.SecretDecryptionFailed);
        Assert.Equal("dpapi:original", settings.ProtectedSecret);
        Assert.Equal("", settings.Secret);
    }
}
