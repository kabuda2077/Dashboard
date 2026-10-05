using System.Text.Json;

namespace Dashboard.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Dashboard.Tests", Guid.NewGuid().ToString("N"));
    private sealed class Protector : ISecretProtector
    {
        public int Writes;
        public bool FailRead;
        public string Protect(string value) { Writes++; return value.Length == 0 ? "" : "test:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value)); }
        public string Unprotect(string value) => value.Length == 0 ? "" : FailRead ? throw new InvalidOperationException("unavailable")
            : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value[5..]));
    }
    private static CoreProfileEdit Edit(CoreProfile profile, SecretEdit? secret = null) => new()
    { ExePath = profile.ExePath, ConfigPath = profile.ConfigPath, ApiUrl = profile.ApiUrl, Secret = secret ?? new() };

    [Fact]
    public void NewDocumentHasTwoProfilesAndExplicitEmptyPreferences()
    {
        var store = new SettingsStore(_directory, new Protector());
        Assert.Equal(2, store.Current.SchemaVersion);
        Assert.Empty(store.Current.DashboardPreferences);
        Assert.NotEqual(store.Current.Profiles.Mihomo.ExePath, store.Current.Profiles.SingBox.ExePath);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "settings.json")));
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("dashboardPreferences").ValueKind);
    }

    [Theory]
    [InlineData("{\"CoreType\":\"mihomo\"}")]
    [InlineData("{\"schemaVersion\":999}")]
    [InlineData("not-json")]
    public void UnsupportedOrDamagedDocumentIsNeverOverwritten(string original)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, original);
        Assert.Throws<AppSettingsLoadException>(() => new SettingsStore(_directory, new Protector()));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public async Task FailedCommitLeavesBothMemoryAndDiskUnchanged()
    {
        var fail = false;
        var store = new SettingsStore(_directory, new Protector(), (path, json) =>
        { if (fail) throw new IOException("disk failed"); SettingsStore.WriteAtomically(path, json); });
        var before = store.Current;
        var disk = File.ReadAllText(Path.Combine(_directory, "settings.json"));
        fail = true;
        await Assert.ThrowsAsync<IOException>(() => store.SetOptionAsync("minimizeToTray", false));
        Assert.Same(before, store.Current);
        Assert.Equal(disk, File.ReadAllText(Path.Combine(_directory, "settings.json")));
    }

    [Fact]
    public async Task ConcurrentIndependentCommitsDoNotClobberEachOther()
    {
        var store = new SettingsStore(_directory, new Protector());
        var draft = Edit(store.Current.Profiles.Mihomo) with { ApiUrl = "http://localhost:9191" };
        await Task.WhenAll(store.UpdateProfileAsync(CoreKind.Mihomo, 0, draft),
            store.SavePreferencesAsync(new Dictionary<string, string> { ["config/theme"] = "dark" }),
            store.SetOptionAsync("lightweightMode", false));
        Assert.Equal("http://localhost:9191", store.Current.Profiles.Mihomo.ApiUrl);
        Assert.Equal("dark", store.Current.DashboardPreferences["config/theme"]);
        Assert.False(store.Current.DesktopOptions.LightweightMode);
        Assert.Equal(1, store.Current.Profiles.Mihomo.Revision);
        var loaded = new SettingsStore(_directory, new Protector());
        Assert.Equal(store.Current.Profiles.Mihomo, loaded.Current.Profiles.Mihomo);
    }

    [Fact]
    public async Task SecretIsProtectedOnceAndPreferencesReuseCiphertext()
    {
        var protector = new Protector();
        var store = new SettingsStore(_directory, protector);
        await store.UpdateProfileAsync(CoreKind.Mihomo, 0, Edit(store.Current.Profiles.Mihomo,
            new() { Action = "replace", Value = "private-credential" }));
        var cipher = store.Current.Profiles.Mihomo.ProtectedSecret;
        await store.SavePreferencesAsync(new Dictionary<string, string> { ["config/theme"] = "dark" });
        await store.SetOptionAsync("lightweightMode", false);
        Assert.Equal(1, protector.Writes);
        Assert.Equal(cipher, store.Current.Profiles.Mihomo.ProtectedSecret);
        Assert.DoesNotContain("private-credential", File.ReadAllText(Path.Combine(_directory, "settings.json")));
        Assert.Equal("private-credential", new SettingsStore(_directory, protector).Current.Profiles.Mihomo.Secret);
    }

    [Fact]
    public async Task CurrentFormatCredentialFailureRequiresExplicitReplacement()
    {
        var protector = new Protector();
        var first = new SettingsStore(_directory, protector);
        await first.UpdateProfileAsync(CoreKind.Mihomo, 0, Edit(first.Current.Profiles.Mihomo, new() { Action = "replace", Value = "credential" }));
        protector.FailRead = true;
        var store = new SettingsStore(_directory, protector);
        var protectedValue = store.Current.Profiles.Mihomo.ProtectedSecret;
        Assert.True(store.Current.Profiles.Mihomo.SecretDecryptionFailed);
        await store.UpdateProfileAsync(CoreKind.Mihomo, 1, Edit(store.Current.Profiles.Mihomo));
        Assert.Equal(protectedValue, store.Current.Profiles.Mihomo.ProtectedSecret);
        Assert.True(store.Current.Profiles.Mihomo.SecretDecryptionFailed);
        await store.UpdateProfileAsync(CoreKind.Mihomo, 2, Edit(store.Current.Profiles.Mihomo, new() { Action = "replace", Value = "" }));
        Assert.False(store.Current.Profiles.Mihomo.SecretDecryptionFailed);
        Assert.Empty(store.Current.Profiles.Mihomo.ProtectedSecret);
    }

    [Fact]
    public async Task OldDraftCannotOverwriteACommittedProfile()
    {
        var store = new SettingsStore(_directory, new Protector());
        var draft = Edit(store.Current.Profiles.Mihomo);
        await store.UpdateProfileAsync(CoreKind.Mihomo, 0, draft);
        await Assert.ThrowsAsync<SettingsConflictException>(() => store.UpdateProfileAsync(CoreKind.Mihomo, 0, draft));
    }

    [Theory]
    [InlineData("file:///C:/test")]
    [InlineData("http://user:password@localhost")]
    [InlineData("http://localhost?secret=private")]
    public void ApiUrlsCannotContainCredentialsOrNonWebProtocols(string url) =>
        Assert.Throws<ArgumentException>(() => SettingsStore.NormalizeApiUrl(url));

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
