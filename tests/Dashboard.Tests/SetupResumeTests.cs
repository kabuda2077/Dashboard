namespace Dashboard.Tests;

public sealed class SetupResumeTests
{
    [Fact]
    public async Task RelaunchWaitsForApiAndDurableCompletionBeforeReturning()
    {
        var ready = false;
        var saved = new TaskCompletionSource<CommandResult>();
        var completing = new TaskCompletionSource();
        var flow = DashboardApplicationContext.ResumeSetupCoreAsync(
            () => Task.FromResult(CommandResult.Completed("processStarted")),
            () => true, () => ready ? "ready" : "checking",
            () => { completing.SetResult(); return saved.Task; }, CancellationToken.None);
        Assert.False(flow.IsCompleted);
        Assert.False(completing.Task.IsCompleted);
        ready = true;
        await completing.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(flow.IsCompleted);
        saved.SetResult(CommandResult.Completed("setupCompleted", saved: true));
        Assert.True((await flow).Saved);
    }

    [Theory]
    [InlineData("failed", true, "ready")]
    [InlineData("elevationRequired", false, "idle")]
    [InlineData("completed", false, "ready")]
    [InlineData("completed", true, "checking")]
    public async Task FailedStartExitOrUnavailableApiNeverCompletesSetup(string status, bool running, string api)
    {
        var writes = 0;
        var result = await DashboardApplicationContext.ResumeSetupCoreAsync(
            () => Task.FromResult(new CommandResult(status, "startResult")), () => running, () => api,
            () => { writes++; return Task.FromResult(CommandResult.Completed()); },
            CancellationToken.None, TimeSpan.Zero);
        Assert.Equal(0, writes);
        Assert.NotEqual("completed", result.Status);
    }

    [Fact]
    public async Task FailedCompletionIsReturnedForVisibleRecovery()
    {
        var result = await DashboardApplicationContext.ResumeSetupCoreAsync(
            () => Task.FromResult(CommandResult.Completed()), () => true, () => "ready",
            () => Task.FromResult(CommandResult.Failed("saveFailed")), CancellationToken.None);
        Assert.Equal("saveFailed", result.Code);
    }
}
