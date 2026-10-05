using System.Diagnostics;

namespace Dashboard.Tests;

public sealed class CoreVersionReaderTests
{
    private static Process Start(string script)
    {
        var info = new ProcessStartInfo("pwsh.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }

    [Fact]
    public async Task DrainsBothPipesWithoutTruncationOrDeadlock()
    {
        using var process = Start("[Console]::Out.Write(('x' * 100000)); [Console]::Error.Write(('y' * 100000)); exit 0");
        var output = await CoreVersionReader.ReadProcessAsync(process, default, TimeSpan.FromSeconds(10));
        Assert.Equal(new string('x', 100000) + " " + new string('y', 100000), output);
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task NonZeroExitIsNotReportedAsAVersion()
    {
        using var process = Start("[Console]::Out.Write('not a version'); exit 17");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CoreVersionReader.ReadProcessAsync(process, default, TimeSpan.FromSeconds(10)));
        Assert.Contains("17", error.Message);
        Assert.True(process.HasExited);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutAndCallerCancellationReapTheChild(bool callerCancellation)
    {
        using var process = Start("Start-Sleep -Seconds 30");
        using var cancellation = new CancellationTokenSource();
        try
        {
            if (callerCancellation) cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
            var read = CoreVersionReader.ReadProcessAsync(process, cancellation.Token,
                callerCancellation ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(100));
            if (callerCancellation) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            else await Assert.ThrowsAsync<TimeoutException>(() => read);
            Assert.True(process.HasExited);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }
}
