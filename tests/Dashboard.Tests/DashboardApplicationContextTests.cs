namespace Dashboard.Tests;

public sealed class DashboardApplicationContextTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void AutostartMigrationDefersToElevatedCoreRelaunch(
        bool shouldStartCore,
        bool isAdministrator,
        bool expected)
    {
        Assert.Equal(
            expected,
            DashboardApplicationContext.ShouldDeferAutostartReconcile(shouldStartCore, isAdministrator));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void WindowCreationDefersUntilElevatedCoreRelaunch(
        bool shouldStartCore,
        bool isAdministrator,
        bool expected)
    {
        Assert.Equal(
            expected,
            DashboardApplicationContext.ShouldRelaunchBeforeShowingWindow(shouldStartCore, isAdministrator));
    }
}
