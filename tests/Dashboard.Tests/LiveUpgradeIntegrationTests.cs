using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dashboard.Tests;

// Explicit opt-in network acceptance, excluded from normal Check/Release. Only
// dedicated temporary copies of validated cores can be changed by this suite.
public sealed class LiveUpgradeIntegrationTests
{
    [Theory]
    [InlineData(CoreKind.Mihomo)]
    [Trait("Category", "OnlineUpgradeIntegration")]
    public async Task CurrentReleaseEndpointIsExercisedWithoutTouchingUserCores(CoreKind kind)
    {
        await using var fixture = await IsolatedCore.CreateAsync(kind);
        var before = await CoreVersionReader.ReadAsync(fixture.Executable, kind);
        var beforeHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture.Executable)));
        string? after = null, afterHash = null;
        CommandResult? result = null;
        int? beforePid = null, upgradedPid = null;
        var automaticRestartConfirmed = false;
        string? error = null;
        try
        {
            Assert.Equal("completed", (await fixture.Controller.ExecuteAsync(new() { Type = "start", CoreType = kind })).Status);
            await fixture.ReadyAsync();
            beforePid = fixture.Process.ProcessId;
            result = await fixture.Controller.ExecuteAsync(new() { Type = "upgradeCore", CoreType = kind });
            Assert.Equal("completed", result.Status);
            upgradedPid = fixture.Process.ProcessId;
            if (kind == CoreKind.Mihomo && result.Code == "upgraded")
            {
                Assert.NotEqual(beforePid, upgradedPid);
                Assert.Equal("ready", fixture.Controller.ApiStatus);
                Assert.True(fixture.Controller.ApiIsManaged);
                automaticRestartConfirmed = true;
            }
            using var upgraded = Process.GetProcessById(upgradedPid!.Value);
            Assert.Equal("completed", (await fixture.Controller.ExecuteAsync(new() { Type = "restart", CoreType = kind })).Status);
            await fixture.ReadyAsync();
            Assert.True(upgraded.HasExited);
            after = await CoreVersionReader.ReadAsync(fixture.Executable, kind);
            afterHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture.Executable)));
            Assert.False(string.IsNullOrWhiteSpace(after));
            Assert.True(fixture.Process.IsRunning);
            Assert.True(fixture.Controller.ApiIsManaged);
            if (result.Code == "upgraded") Assert.NotEqual(beforeHash, afterHash);
            Assert.Equal("completed", (await fixture.Controller.ExecuteAsync(new() { Type = "stop", CoreType = kind })).Status);
            Assert.False(fixture.Process.IsRunning);
        }
        catch (Exception exception) { error = exception.Message; throw; }
        finally
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Dashboard.csproj"))) repository = repository.Parent;
            var directory = TestDirectory.ReportDirectory("online-upgrade"); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, AppSettings.WireKind(kind) + ".json"), JsonSerializer.Serialize(new {
                checkedAt = DateTimeOffset.UtcNow, kind = AppSettings.WireKind(kind), before, after, beforeHash, afterHash,
                result, beforePid, upgradedPid, automaticRestartConfirmed, error, isolated = true, noTunOrProxyListeners = true,
                scope = "Live release endpoint; mihomo replacement ownership before manual restart; API readiness, restart and stop. An alreadyLatest result is not evidence that a new release was downloaded."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
