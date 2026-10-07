namespace Dashboard.Tests;

public sealed class UpgradeConfirmationTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData(null, null, "confirmationExpired")]
    [InlineData(1L, 0L, "staleRevision")]
    [InlineData(0L, 1L, "staleRuntime")]
    public async Task StaleOrUnboundConfirmationCannotReachAnUpgrade(long? revision, long? epoch, string code)
    {
        var root = TestRoot;
        var store = new SettingsStore(root);
        using var process = new CoreProcessManager();
        using var controller = new CoreLifecycleController(store, process, () => true);
        try
        {
            var result = await controller.ExecuteAsync(new() { Type = "upgradeCore", CoreType = CoreKind.SingBox,
                ConfirmUnverified = true, ExpectedRevision = revision, ExpectedRuntimeEpoch = epoch });
            Assert.Equal("rejected", result.Status);
            Assert.Equal(code, result.Code);
            Assert.False(process.IsRunning);
            Assert.Equal(0, store.Current.Profiles.SingBox.Revision);
        }
        finally { await controller.ShutdownAsync(TimeSpan.FromSeconds(2)); CleanupTestDirectory(); }
    }
}
