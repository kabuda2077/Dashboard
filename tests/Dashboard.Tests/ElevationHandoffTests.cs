using Dashboard;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace Dashboard.Tests;

public sealed class ElevationHandoffTests
{
    [Fact]
    public async Task SameAccountReplacementAcknowledgesBeforeOldOwnerExits()
    {
        using var handoff = new ElevationHandoff();
        var wait = handoff.WaitForReadyAsync(new TaskCompletionSource().Task);
        var replacement = Task.Run(() => ElevationHandoff.ConfirmReplacement(handoff.UserSid, handoff.PipeName));
        await Task.WhenAll(wait, replacement).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ParentDecisionAfterReadyOutlivesConnectionBudget(bool accepted)
    {
        var pipeName = "Dashboard.Test.Elevation." + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var identity = WindowsIdentity.GetCurrent();
        var replacement = Task.Run(() => ElevationHandoff.ConfirmReplacement(identity.User!.Value, pipeName, TimeSpan.FromSeconds(1)));
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.WaitForConnectionAsync(budget.Token);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        Assert.Equal("ready", await reader.ReadLineAsync(budget.Token));
        await Task.Delay(TimeSpan.FromSeconds(1.1), budget.Token);
        Assert.False(replacement.IsCompleted);
        if (accepted)
        {
            using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            writer.WriteLine("accepted");
            await replacement.WaitAsync(TimeSpan.FromSeconds(5));
        }
        else
        {
            server.Dispose();
            await Assert.ThrowsAsync<InvalidOperationException>(() => replacement.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task DifferentAccountOrExitedReplacementCannotCompleteHandoff()
    {
        using var handoff = new ElevationHandoff();
        Assert.Throws<InvalidOperationException>(() => ElevationHandoff.ConfirmReplacement("S-1-0-0", handoff.PipeName));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handoff.WaitForReadyAsync(Task.CompletedTask));
    }
}
