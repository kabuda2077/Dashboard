using System.Diagnostics;
using System.Text;

namespace MihomoDashboard;

public sealed class MihomoManager : IDisposable
{
    private const int MaxLogLength = 80_000;
    private readonly StringBuilder _log = new();
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
            lock (_log)
            {
                return _log.ToString();
            }
        }
    }

    public string GetLogTail(int maxLength)
    {
        lock (_log)
        {
            if (_log.Length <= maxLength)
            {
                return _log.ToString();
            }

            return _log.ToString(_log.Length - maxLength, maxLength);
        }
    }

    public void Start(AppSettings settings)
    {
        DisposeExitedProcess();

        if (IsRunning)
        {
            AppendLog("mihomo is already running.");
            return;
        }

        if (!File.Exists(settings.CorePath))
        {
            throw new FileNotFoundException("找不到 mihomo 内核，请检查路径。", settings.CorePath);
        }

        if (!File.Exists(settings.ConfigPath))
        {
            throw new FileNotFoundException("找不到 mihomo 配置文件，请检查路径。", settings.ConfigPath);
        }

        var configDirectory = Path.GetDirectoryName(settings.ConfigPath) ?? AppContext.BaseDirectory;
        var arguments = $"-d \"{configDirectory}\" -f \"{settings.ConfigPath}\"";

        var startInfo = new ProcessStartInfo(settings.CorePath, arguments)
        {
            WorkingDirectory = configDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
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
                AppendLog($"mihomo exited with code {GetExitCodeText(process)}.");
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
            throw new InvalidOperationException("mihomo 启动失败。");
        }

        processId = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        AppendLog($"mihomo started. pid={processId}");
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            DisposeExitedProcess();
            AppendLog("mihomo is not running.");
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
            process.WaitForExit(3000);
            AppendLog("mihomo stopped.");
        }
        catch (Exception ex)
        {
            AppendLog($"failed to stop mihomo: {ex.Message}");
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
        lock (_log)
        {
            _log.Append(entry);
            if (_log.Length > MaxLogLength)
            {
                _log.Remove(0, _log.Length - MaxLogLength);
            }
        }

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
}
