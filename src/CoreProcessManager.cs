using System.Diagnostics;
using System.Text;

namespace Dashboard;

public sealed class CoreProcessManager : IDisposable
{
    private const int MaxLogLines = 500;
    private const int MaxLogLineLength = 8192;
    private const int LogEventFlushIntervalMs = 125;
    private readonly Queue<string> _logLines = new(MaxLogLines);
    private readonly object _logLinesLock = new();
    private readonly object _processLock = new();
    private readonly object _logEventLock = new();
    private readonly HashSet<int> _stoppingProcessIds = new();
    private readonly StringBuilder _pendingLogEvents = new();
    private System.Threading.Timer? _logEventTimer;
    private readonly object _operationLock = new();
    private bool _disposed;
    private Process? _process;
    private CoreProcessJob? _processJob;
    private bool _expectSelfRestart;
    private CancellationTokenSource? _outputCancellation;
    private Task? _outputTask;

    private readonly Action<Process> _killProcess;
    private readonly Func<Process, int, bool> _waitForExit;

    public CoreProcessManager() : this(process => process.Kill(entireProcessTree: true),
        (process, timeout) => process.WaitForExit(timeout))
    { }

    internal CoreProcessManager(Action<Process> killProcess, Func<Process, int, bool> waitForExit)
    {
        _killProcess = killProcess;
        _waitForExit = waitForExit;
    }

    public event EventHandler? StatusChanged;
    public event EventHandler<string>? LogReceived;

    public bool IsRunning
    {
        get
        {
            lock (_processLock)
            {
                return _process is not null && IsProcessRunning(_process);
            }
        }
    }

    public int? ProcessId
    {
        get
        {
            lock (_processLock)
            {
                var process = _process;
                return process is not null && IsProcessRunning(process) ? process.Id : null;
            }
        }
    }

    public string GetLogTail(int maxLength)
    {
        List<string> lines;
        lock (_logLinesLock)
        {
            lines = new List<string>(_logLines);
        }

        var sb = new StringBuilder(maxLength);

        // 从后往前拼接，直到达到长度限制
        for (int i = lines.Count - 1; i >= 0 && sb.Length < maxLength; i--)
        {
            var line = lines[i];
            if (sb.Length + line.Length > maxLength && sb.Length > 0)
            {
                break;
            }
            sb.Insert(0, line);
        }

        return sb.ToString();
    }

    internal Task StartAsync(CoreLaunchSpec spec, CancellationToken token = default) => Task.Run(() =>
    {
        lock (_operationLock)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            StartOwnedProcess(spec);
        }
    }, token);

    internal Task StopAsync(TimeSpan? timeout = null) => Task.Run(() => Stop(timeout ?? TimeSpan.FromSeconds(3)));

    internal bool OwnsProcess(int processId, int expectedRoot)
    {
        lock (_processLock)
            return !_disposed && _process is { } root && IsProcessRunning(root) && root.Id == expectedRoot
                && (processId == expectedRoot || _processJob?.ContainsProcess(processId) == true);
    }

    private void DisposeProcessJob()
    {
        lock (_processLock)
        {
            _processJob?.Dispose();
            _processJob = null;
        }
    }

    internal int BeginSelfUpgrade()
    {
        lock (_processLock)
        {
            if (_disposed || _process is null || !IsProcessRunning(_process))
                throw new InvalidOperationException("The managed core is not running.");
            _expectSelfRestart = true;
            return _process.Id;
        }
    }

    internal void EndSelfUpgrade()
    {
        lock (_processLock) _expectSelfRestart = false;
    }

    private void StartOwnedProcess(CoreLaunchSpec spec)
    {
        DisposeExitedProcess();

        if (IsRunning)
        {
            AppendLog($"{spec.Title} is already running.");
            return;
        }

        var corePath = spec.Profile.ExePath;
        var configPath = spec.Profile.ConfigPath;
        var coreName = spec.Title;

        if (!File.Exists(corePath))
        {
            throw new FileNotFoundException($"找不到 {coreName} 内核，请检查路径。", corePath);
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"找不到 {coreName} 配置文件，请检查路径。", configPath);
        }

        var configDirectory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
        var arguments = spec.Kind == CoreKind.SingBox
            ? $"run -D \"{configDirectory}\" -c \"{configPath}\""
            : $"-d \"{configDirectory}\" -f \"{configPath}\"";

        var startInfo = new ProcessStartInfo(corePath, arguments)
        {
            WorkingDirectory = configDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        StartProcess(startInfo, coreName, spec.Profile.Secret);
    }

    private void StartProcess(ProcessStartInfo startInfo, string coreName, string? secret)
    {
        var job = new CoreProcessJob();
        var process = new Process { StartInfo = startInfo };
        lock (_processLock) _process = process;
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"{coreName} 启动失败。");
            job.Add(process);
            lock (_processLock) _processJob = job;
        }
        catch
        {
            try { if (IsProcessRunning(process)) process.Kill(entireProcessTree: true); }
            finally
            {
                lock (_processLock) { if (ReferenceEquals(_process, process)) _process = null; }
                process.Dispose();
                job.Dispose();
            }
            throw;
        }

        var processId = process.Id;
        var cancellation = new CancellationTokenSource();
        _outputCancellation = cancellation;
        // Own the entire output/exit lifetime. Each pipe gets its own reader so a busy
        // stdout cannot prevent stderr from being drained (including synchronous reads).
        _outputTask = Task.Run(() => ObserveProcessAsync(process, coreName, secret, cancellation));
        AppendLog($"{coreName} started. pid={processId}");
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        Stop(TimeSpan.FromSeconds(3));
    }

    public void Stop(TimeSpan waitTimeout)
    {
        lock (_operationLock) StopOwnedProcess(waitTimeout);
    }

    private void StopOwnedProcess(TimeSpan waitTimeout)
    {
        EndSelfUpgrade();
        if (!IsRunning)
        {
            DisposeExitedProcess();
            AppendLog("core is not running.");
            return;
        }

        Process process;
        lock (_processLock)
        {
            process = _process!;
        }

        try
        {
            MarkStopping(process);
            _killProcess(process);
            if (!_waitForExit(process, (int)waitTimeout.TotalMilliseconds))
            {
                throw new TimeoutException("等待内核进程退出超时。");
            }
            AppendLog("core stopped.");
        }
        catch (Exception ex)
        {
            AppendLog($"failed to stop core: {ex.Message}");
            throw;
        }
        finally
        {
            // A failed kill/wait must not discard ownership of a live process.
            if (!IsProcessRunning(process))
            {
                CompleteOutput();
                DisposeProcessJob();
                ShouldSuppressExitedLog(process.Id);
                lock (_processLock)
                {
                    if (ReferenceEquals(_process, process)) _process = null;
                }
                process.Dispose();
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ObserveProcessAsync(Process process, string coreName, string? secret,
        CancellationTokenSource cancellation)
    {
        var stdout = Task.Run(() => ReadOutputAsync(process.StandardOutput, secret, cancellation));
        var stderr = Task.Run(() => ReadOutputAsync(process.StandardError, secret, cancellation));
        var readers = Task.WhenAll(stdout, stderr);
        var observed = process;
        try
        {
            while (true)
            {
                await observed.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
                Process? restarted = null;
                lock (_processLock)
                {
                    if (_expectSelfRestart && !_disposed && !cancellation.IsCancellationRequested && ReferenceEquals(_process, observed))
                    {
                        restarted = _processJob?.FindRestartedCore(process.StartInfo.FileName, observed.Id);
                        if (restarted is not null) _process = restarted;
                    }
                }
                if (restarted is null) break;
                AppendLog($"{coreName} self-upgrade restarted. pid={restarted.Id}");
                ThreadPool.QueueUserWorkItem(_ => NotifyProcessChanged(restarted, coreName));
                if (!ReferenceEquals(observed, process)) observed.Dispose();
                observed = restarted;
            }
            // A descendant may keep an inherited pipe open after the core exits.
            // Allow buffered final lines to drain, then cancel our reads as well.
            try { await readers.WaitAsync(TimeSpan.FromSeconds(2), cancellation.Token).ConfigureAwait(false); }
            catch (TimeoutException) { }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            HostOperationLogger.Error("core", "Unable to observe core process exit.", error);
        }
        finally
        {
            cancellation.Cancel();
            await readers.ConfigureAwait(false);
            if (!ReferenceEquals(observed, process)) process.Dispose();
        }

        // Notifications can call Stop/Dispose themselves. Dispatch only after the
        // owned reads finish, without making their completion depend on this callback
        // acquiring _operationLock while Stop is joining them.
        ThreadPool.QueueUserWorkItem(_ => NotifyProcessChanged(observed, coreName));
    }

    private void NotifyProcessChanged(Process process, string coreName)
    {
        lock (_operationLock)
        {
            lock (_processLock)
            {
                if (!ReferenceEquals(_process, process) || _disposed) return;
            }
            if (!IsProcessRunning(process) && !ShouldSuppressExitedLog(process.Id))
                AppendLog($"{coreName} exited with code {GetExitCodeText(process)}.");
            try { StatusChanged?.Invoke(this, EventArgs.Empty); }
            catch (Exception error) { HostOperationLogger.Error("core", "Core status notification failed.", error); }
        }
    }

    private async Task ReadOutputAsync(StreamReader reader, string? secret, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var buffer = new char[2048];
        var line = new StringBuilder(MaxLogLineLength);
        var truncated = false;
        var afterCarriageReturn = false;
        void Emit()
        {
            var text = line.ToString();
            if (!string.IsNullOrEmpty(secret))
            {
                // A retained prefix can end inside a secret. Find the unmatched
                // tail after complete occurrences first, so overlapping prefixes
                // (e.g. secret "aaa" in "aaaa") cannot undo complete redaction.
                var unmatchedStart = 0;
                if (truncated)
                {
                    int match;
                    while ((match = text.IndexOf(secret, unmatchedStart, StringComparison.Ordinal)) >= 0)
                        unmatchedStart = match + secret.Length;
                }
                var redacted = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
                if (truncated)
                {
                    for (var length = Math.Min(secret.Length - 1, text.Length - unmatchedStart); length > 0; length--)
                    {
                        if (!text.AsSpan(text.Length - length).SequenceEqual(secret.AsSpan(0, length))) continue;
                        redacted = redacted[..^length] + "[redacted]";
                        break;
                    }
                }
                text = redacted;
            }
            lock (_processLock)
            {
                if (ReferenceEquals(_outputCancellation, cancellation) && !_disposed)
                    AppendLog(truncated ? text + " [truncated]" : text);
            }
            line.Clear();
            truncated = false;
        }

        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    var character = buffer[i];
                    if (character == '\r' || character == '\n')
                    {
                        if (character != '\n' || !afterCarriageReturn) Emit();
                        afterCarriageReturn = character == '\r';
                    }
                    else
                    {
                        afterCarriageReturn = false;
                        if (line.Length < MaxLogLineLength) line.Append(character);
                        else truncated = true;
                    }
                }
            }
            if (line.Length > 0 || truncated) Emit();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            HostOperationLogger.Error("core", "Unable to read core output.", error);
        }
    }

    private void CompleteOutput()
    {
        var completion = _outputTask;
        var cancellation = _outputCancellation;
        if (completion is null) return;
        try
        {
            cancellation?.Cancel();
            completion.GetAwaiter().GetResult();
        }
        finally
        {
            _outputTask = null;
            _outputCancellation = null;
            cancellation?.Dispose();
        }
    }

    private void AppendLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (line.Length > MaxLogLineLength) line = line[..MaxLogLineLength] + " [truncated]";
        var entry = $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
        lock (_logLinesLock)
        {
            _logLines.Enqueue(entry);
            if (_logLines.Count > MaxLogLines)
            {
                _logLines.Dequeue();
            }
        }

        QueueLogReceived(entry);
    }

    private void QueueLogReceived(string entry)
    {
        lock (_logEventLock)
        {
            if (_pendingLogEvents.Length + entry.Length > 65536) _pendingLogEvents.Clear();
            _pendingLogEvents.Append(entry);
            _logEventTimer ??= new System.Threading.Timer(
                _ => FlushPendingLogReceived(),
                null,
                LogEventFlushIntervalMs,
                Timeout.Infinite);
        }
    }

    private void FlushPendingLogReceived()
    {
        string? batch = null;
        lock (_logEventLock)
        {
            if (_pendingLogEvents.Length > 0)
            {
                batch = _pendingLogEvents.ToString();
                _pendingLogEvents.Clear();
            }

            _logEventTimer?.Dispose();
            _logEventTimer = null;
        }

        if (!string.IsNullOrWhiteSpace(batch))
        {
            LogReceived?.Invoke(this, batch);
        }
    }

    private void DisposeExitedProcess()
    {
        Process? process;
        lock (_processLock)
        {
            process = _process;
            if (process is null || IsProcessRunning(process))
            {
                return;
            }
        }

        CompleteOutput();
        DisposeProcessJob();
        lock (_processLock)
        {
            if (ReferenceEquals(_process, process)) _process = null;
        }
        process.Dispose();
    }

    private static bool IsProcessRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string GetExitCodeText(Process process)
    {
        try
        {
            return process.ExitCode.ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    private void MarkStopping(Process process)
    {
        try
        {
            lock (_processLock)
            {
                _stoppingProcessIds.Add(process.Id);
            }
        }
        catch
        {
        }
    }

    private bool ShouldSuppressExitedLog(int processId)
    {
        try
        {
            lock (_processLock)
            {
                return _stoppingProcessIds.Remove(processId);
            }
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_operationLock)
        {
            lock (_processLock) _disposed = true;
            DisposeOwnedProcess();
        }
    }

    private void DisposeOwnedProcess()
    {
        try
        {
            if (IsRunning) Stop();
        }
        finally
        {
            // A failed termination must retain ownership of a live process,
            // while still releasing independent log/timer resources.
            Process? exitedProcess = null;
            lock (_processLock)
            {
                if (!IsRunning)
                {
                    exitedProcess = _process;
                    _process = null;
                }
            }
            ShutdownResourceDisposer.DisposeAll(
                ("Pending core log events", FlushPendingLogReceived),
                ("Core log timer", () =>
                {
                    lock (_logEventLock)
                    {
                        _logEventTimer?.Dispose();
                        _logEventTimer = null;
                    }
                }
            ),
                ("Core output readers", CompleteOutput),
                ("Core process job", () =>
                {
                    if (IsRunning) return;
                    DisposeProcessJob();
                }),
                ("Exited core process", () => exitedProcess?.Dispose()));
        }
    }
}
