using System.Security.Cryptography;
using System.Text.Json;

namespace Dashboard.Tests;

// Explicit opt-in network acceptance, excluded from normal Check/Release. Only
// dedicated temporary copies of validated cores can be changed by this suite.
public sealed class LiveUpgradeIntegrationTests
{
    [Theory]
    [InlineData(CoreKind.Mihomo)]
    [InlineData(CoreKind.SingBox)]
    [Trait("Category", "OnlineUpgradeIntegration")]
    public async Task CurrentReleaseEndpointIsExercisedWithoutTouchingUserCores(CoreKind kind)
    {
        await using var fixture = await IsolatedCore.CreateAsync(kind);
        var before = await CoreVersionReader.ReadAsync(fixture.Executable, kind);
        var beforeHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture.Executable)));
        string? after = null, afterHash = null;
        CommandResult? result = null;
        string? error = null;
        try
        {
            Assert.Equal("completed", (await fixture.Controller.ExecuteAsync(new() { Type = "start", CoreType = kind })).Status);
            await fixture.ReadyAsync();
            result = await fixture.Controller.ExecuteAsync(new() { Type = "upgradeCore", CoreType = kind });
            Assert.Equal("completed", result.Status);
            Assert.Equal("completed", (await fixture.Controller.ExecuteAsync(new() { Type = "restart", CoreType = kind })).Status);
            await fixture.ReadyAsync();
            after = await CoreVersionReader.ReadAsync(fixture.Executable, kind);
            afterHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture.Executable)));
            Assert.False(string.IsNullOrWhiteSpace(after));
            Assert.True(fixture.Process.IsRunning);
        }
        catch (Exception exception) { error = exception.Message; throw; }
        finally
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj"))) repository = repository.Parent;
            var directory = Path.Combine(repository!.FullName, ".tmp", "online-upgrade"); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, AppSettings.WireKind(kind) + ".json"), JsonSerializer.Serialize(new {
                checkedAt = DateTimeOffset.UtcNow, kind = AppSettings.WireKind(kind), before, after, beforeHash, afterHash,
                result, error, isolated = true, noTunOrProxyListeners = true,
                scope = "Live configured release endpoint plus restart/API readiness. An alreadyLatest result is not evidence that a new release was downloaded. Digest confirmation is never bypassed."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
