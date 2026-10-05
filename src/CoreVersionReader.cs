using System.Diagnostics;

namespace Dashboard;

internal static class CoreVersionReader
{
    public static async Task<string> ReadAsync(string path, CoreKind kind, CancellationToken token = default)
    {
        using var process = Process.Start(new ProcessStartInfo(path, kind == CoreKind.SingBox ? "version" : "-v")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("无法启动版本探测进程。");
        return await ReadProcessAsync(process, token).ConfigureAwait(false);
    }

    internal static async Task<string> ReadProcessAsync(Process process, CancellationToken token,
        TimeSpan? timeout = null)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
        var stdout = process.StandardOutput.ReadToEndAsync(budget.Token);
        var stderr = process.StandardError.ReadToEndAsync(budget.Token);
        try
        {
            await process.WaitForExitAsync(budget.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException($"版本探测进程退出码：{process.ExitCode}");
            return (await stdout + " " + await stderr).Trim();
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception error) { HostOperationLogger.Error("version", "Unable to terminate version probe.", error); }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { }
            try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { }
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("读取内核版本超时。");
        }
    }
}
