using System.Diagnostics;
using System.Text;

namespace Dashboard;

public sealed class CoreProcessManager : IDisposable
{
    private const int MaxLogLines = 500;
    private readonly CircularBuffer<string> _logLines = new(MaxLogLines);
    private readonly object _processLock = new();
    private readonly HashSet<int> _stoppingProcessIds = new();
    private Process? _process;

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

    public string LogText
    {
        get
        {
            var lines = _logLines.GetAll();
            return string.Join("", lines);
        }
    }

    public string GetLogTail(int maxLength)
    {
        var lines = _logLines.GetAll();
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

        if (TryAttachExistingProcess(corePath, coreName))
        {
            return;
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

        if (!process.Start())
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

    private bool TryAttachExistingProcess(string corePath, string coreName)
    {
        var existingProcess = FindExistingProcess(corePath);
        if (existingProcess is null)
        {
            return false;
        }

        try
        {
            existingProcess.EnableRaisingEvents = true;
            var processId = existingProcess.Id;
            existingProcess.Exited += (_, _) =>
            {
                if (!ShouldSuppressExitedLog(processId))
                {
                    AppendLog($"{coreName} exited with code {GetExitCodeText(existingProcess)}.");
                }
                StatusChanged?.Invoke(this, EventArgs.Empty);
            };

            lock (_processLock)
            {
                _process = existingProcess;
            }

            AppendLog($"{coreName} already running. attached pid={processId}");
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch
        {
            existingProcess.Dispose();
            return false;
        }
    }

    private static Process? FindExistingProcess(string corePath)
    {
        var currentProcessId = Environment.ProcessId;
        var processName = Path.GetFileNameWithoutExtension(corePath);
        var fullCorePath = Path.GetFullPath(corePath);

        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (process.Id == currentProcessId || process.HasExited)
                {
                    process.Dispose();
                    continue;
                }

                var modulePath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(modulePath)
                    || !string.Equals(Path.GetFullPath(modulePath), fullCorePath, StringComparison.OrdinalIgnoreCase))
                {
                    process.Dispose();
                    continue;
                }

                return process;
            }
            catch
            {
                process.Dispose();
            }
        }

        return null;
    }

    public void Stop()
    {
        Stop(TimeSpan.FromSeconds(3));
    }

    public void Stop(TimeSpan waitTimeout)
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
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit((int)waitTimeout.TotalMilliseconds))
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
            process.Dispose();
            lock (_processLock)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
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
        _logLines.Add(entry);
        LogReceived?.Invoke(this, entry);
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
        if (IsRunning)
        {
            Stop();
        }

        Process? process;
        lock (_processLock)
        {
            process = _process;
            _process = null;
        }

        process?.Dispose();
    }

    /// <summary>
    /// 环形缓冲区，用于高效存储固定数量的日志行
    /// </summary>
    private sealed class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private readonly object _lock = new();
        private int _start;
        private int _count;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentException("Capacity must be positive", nameof(capacity));
            }
            _buffer = new T[capacity];
        }

        public void Add(T item)
        {
            lock (_lock)
            {
                if (_count < _buffer.Length)
                {
                    _buffer[_count++] = item;
                }
                else
                {
                    _buffer[_start] = item;
                    _start = (_start + 1) % _buffer.Length;
                }
            }
        }

        public List<T> GetAll()
        {
            lock (_lock)
            {
                var result = new List<T>(_count);
                for (int i = 0; i < _count; i++)
                {
                    result.Add(_buffer[(_start + i) % _buffer.Length]);
                }
                return result;
            }
        }
    }
}
