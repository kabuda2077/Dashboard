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
    public async Task DrainsBothPipesAtTheLimitWithoutTruncationOrDeadlock()
    {
        var limit = CoreVersionReader.MaxOutputCharactersPerStream;
        using var process = Start($"[Console]::Out.Write(('x' * {limit})); [Console]::Error.Write(('y' * {limit})); exit 0");
        var output = await CoreVersionReader.ReadProcessAsync(process, default, TimeSpan.FromSeconds(10));
        Assert.Equal(new string('x', limit) + " " + new string('y', limit), output);
        Assert.True(process.HasExited);
    }

    [Theory]
    [InlineData("Out", "stdout")]
    [InlineData("Error", "stderr")]
    public async Task ContinuousOutputBeyondLimitFailsAndReapsTheChild(string writer, string stream)
    {
        using var process = Start($"$chunk = 'x' * 2048; while ($true) {{ [Console]::{writer}.Write($chunk) }}");
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CoreVersionReader.ReadProcessAsync(process, default, TimeSpan.FromSeconds(10)));
            Assert.Contains(stream, error.Message);
            Assert.Contains("上限", error.Message);
            Assert.Contains(CoreVersionReader.MaxOutputCharactersPerStream.ToString(), error.Message);
            Assert.True(process.HasExited);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
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
