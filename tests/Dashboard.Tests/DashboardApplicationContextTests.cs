namespace Dashboard.Tests;

public sealed class DashboardApplicationContextTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void StartingCoreWithoutElevationDefersWindowAndAutostart(
        bool shouldStartCore,
        bool isAdministrator,
        bool expected)
    {
        Assert.Equal(
            expected,
            DashboardApplicationContext.WillRelaunchElevated(shouldStartCore, isAdministrator));
    }

    [Theory]
    [InlineData(true, false, false, true, true, false, true)]
    [InlineData(true, false, false, true, true, true, false)]
    [InlineData(true, false, false, true, false, false, false)]
    [InlineData(true, false, false, false, true, false, false)]
    [InlineData(true, true, false, true, true, false, false)]
    [InlineData(true, false, true, true, true, false, false)]
    [InlineData(false, false, false, true, true, false, false)]
    public void ResumeRecoveryOnlyRestartsAStaleMihomoTun(
        bool coreRunning,
        bool isSingBox,
        bool coreOperationInProgress,
        bool tunWasUpBeforeSuspend,
        bool physicalNetworkUp,
        bool tunUp,
        bool expected)
    {
        Assert.Equal(
            expected,
            DashboardApplicationContext.ShouldRestartCoreAfterResume(
                coreRunning,
                isSingBox,
                coreOperationInProgress,
                tunWasUpBeforeSuspend,
                physicalNetworkUp,
                tunUp));
    }
}
