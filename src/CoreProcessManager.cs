using System.Diagnostics;
using System.Text;

namespace Dashboard;

public sealed class CoreProcessManager : IDisposable
{
    private const int MaxLogLines = 500;
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

    private readonly Action<Process> _killProcess;
    private readonly Func<Process, int, bool> _waitForExit;

    public CoreProcessManager() : this(process => process.Kill(entireProcessTree: true),
        (process, timeout) => process.WaitForExit(timeout)) { }

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

    public void Start(AppSettings settings)
    {
        lock (_operationLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StartOwnedProcess(settings);
        }
    }

    private void StartOwnedProcess(AppSettings settings)
    {
        DisposeExitedProcess();

        if (IsRunning)
        {
            AppendLog($"{settings.CoreDisplayName} is already running.");
            return;
        }

        var corePath = settings.ActiveCorePath;
        var configPath = settings.ActiveConfigPath;
        var coreName = settings.CoreDisplayName;

        if (!File.Exists(corePath))
        {
            throw new FileNotFoundException($"找不到 {coreName} 内核，请检查路径。", corePath);
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"找不到 {coreName} 配置文件，请检查路径。", configPath);
        }

        var configDirectory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
        var arguments = settings.IsSingBox
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

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        var processId = 0;
        process.OutputDataReceived += (_, e) => AppendLog(e.Data);
        process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        process.Exited += (_, _) =>
        {
            if (processId == 0 || !ShouldSuppressExitedLog(processId))
            {
                AppendLog($"{coreName} exited with code {GetExitCodeText(process)}.");
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        };
        lock (_processLock)
        {
            _process = process;
        }

        bool started;
        try { started = process.Start(); }
        catch
        {
            lock (_processLock) { if (ReferenceEquals(_process, process)) _process = null; }
            process.Dispose();
            throw;
        }
        if (!started)
        {
            process.Dispose();
            lock (_processLock)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }
            throw new InvalidOperationException($"{coreName} 启动失败。");
        }

        processId = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
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
                lock (_processLock)
                {
                    if (ReferenceEquals(_process, process)) _process = null;
                }
                process.Dispose();
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AppendLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

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

            _process = null;
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
            _disposed = true;
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
                }),
                ("Exited core process", () => exitedProcess?.Dispose()));
        }
    }
}
