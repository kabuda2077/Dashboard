using System.Diagnostics;
using System.Text;

namespace Dashboard;

internal static class CoreVersionReader
{
    // Version probes only need a few lines. Bound each pipe independently while
    // still draining both concurrently (at most 32 Ki characters in total).
    internal const int MaxOutputCharactersPerStream = 16 * 1024;
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
        var stdout = Task.Run(() => ReadLimitedAsync(process.StandardOutput, "stdout", budget));
        var stderr = Task.Run(() => ReadLimitedAsync(process.StandardError, "stderr", budget));
        var exit = process.WaitForExitAsync(budget.Token);
        var completion = Task.WhenAll(exit, stdout, stderr);
        try
        {
            await completion.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException($"版本探测进程退出码：{process.ExitCode}");
            return (await stdout + " " + await stderr).Trim();
        }
        catch
        {
            // Overflow, timeout, cancellation and I/O failures share the same
            // cleanup. Cancel sibling reads before killing/reaping the child;
            // await every task so none outlive the process or linked token source.
            budget.Cancel();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception error) { HostOperationLogger.Error("version", "Unable to terminate version probe.", error); }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception error) { HostOperationLogger.Error("version", "Unable to reap version probe.", error); }
            try { await completion.ConfigureAwait(false); }
            catch { }
            token.ThrowIfCancellationRequested();
            // Task.WhenAll preserves a read failure ahead of canceled siblings.
            // Only a canceled completion represents the time budget expiring.
            if (completion.IsCanceled) throw new TimeoutException("读取内核版本超时。");
            throw;
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, string stream,
        CancellationTokenSource budget)
    {
        var output = new StringBuilder();
        var buffer = new char[2048];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), budget.Token).ConfigureAwait(false)) > 0)
            {
                if (count > MaxOutputCharactersPerStream - output.Length)
                    throw new InvalidOperationException($"版本探测输出 {stream} 超过 {MaxOutputCharactersPerStream} 字符上限。");
                output.Append(buffer, 0, count);
            }
            return output.ToString();
        }
        catch
        {
            // Interrupt WaitForExitAsync and the other pipe immediately. Waiting
            // for the child first would let a continuously writing probe run forever.
            budget.Cancel();
            throw;
        }
    }
}
