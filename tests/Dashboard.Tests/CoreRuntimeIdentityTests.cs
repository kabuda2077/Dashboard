namespace Dashboard.Tests;

public sealed class CoreRuntimeIdentityTests
{
    [Fact]
    public void RestartWithSamePathHasDifferentIdentity()
    {
        var before = DashboardHost.BuildCoreIdentity("mihomo", "core.exe", 100, 4);
        var after = DashboardHost.BuildCoreIdentity("mihomo", "core.exe", 101, 5);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void StopInvalidatesIdentityEvenWhenPathIsUnchanged()
    {
        var running = DashboardHost.BuildCoreIdentity("sing-box", "core.exe", 100, 4);
        var stopped = DashboardHost.BuildCoreIdentity("sing-box", "core.exe", null, 5);
        Assert.NotEqual(running, stopped);
    }

    [Fact]
    public void GenerationSeparatesPidReuse()
    {
        var oldRun = DashboardHost.BuildCoreIdentity("mihomo", "core.exe", 100, 4);
        var reusedPid = DashboardHost.BuildCoreIdentity("mihomo", "core.exe", 100, 6);
        Assert.NotEqual(oldRun, reusedPid);
    }
}
