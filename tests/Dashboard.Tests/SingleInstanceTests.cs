namespace Dashboard.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void NamesAreIsolatedByUserSid()
    {
        var first = SingleInstance.GetNames("S-1-5-21-1000");
        var second = SingleInstance.GetNames("S-1-5-21-2000");

        Assert.NotEqual(first.MutexName, second.MutexName);
        Assert.NotEqual(first.PipeName, second.PipeName);
        Assert.StartsWith("Local\\Dashboard.SingleInstance.", first.MutexName);
        Assert.StartsWith("Dashboard.Activate.", first.PipeName);
    }

    [Fact]
    public void SecondInstanceReceivesActivationAcknowledgement()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var names = new SingleInstanceNames(
            $"Local\\Dashboard.Tests.{suffix}",
            $"Dashboard.Tests.{suffix}");
        using var activated = new ManualResetEventSlim();
        Assert.True(SingleInstance.TryCreate(
            activated.Set,
            waitForPreviousExit: false,
            names,
            out var primary));
        using (primary)
        {
            Assert.False(SingleInstance.TryCreate(
                () => { },
                waitForPreviousExit: false,
                names,
                out var secondary));
            Assert.Null(secondary);
            Assert.True(activated.Wait(TimeSpan.FromSeconds(2)));
        }
    }
}
