using System.Diagnostics;
using System.Text;

namespace MihomoDashboard;

public sealed class MihomoManager : IDisposable
{
    private const int MaxLogLength = 80_000;
    private readonly StringBuilder _log = new();
    private Process? _process;

    public event EventHandler? StatusChanged;
    public event EventHandler<string>? LogReceived;

    public bool IsRunning => _process is not null && IsProcessRunning(_process);
    public int? ProcessId
    {
        get
        {
            var process = _process;
            return process is not null && IsProcessRunning(process) ? process.Id : null;
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
        process.OutputDataReceived += (_, e) => AppendLog(e.Data);
        process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        process.Exited += (_, _) =>
        {
            AppendLog($"mihomo exited with code {GetExitCodeText(process)}.");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        };

        _process = process;
        if (!process.Start())
        {
            process.Dispose();
            _process = null;
            throw new InvalidOperationException("mihomo 启动失败。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        AppendLog($"mihomo started. pid={process.Id}");
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

        var process = _process!;
        try
        {
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
            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearLog()
    {
        lock (_log)
        {
            _log.Clear();
        }
        LogReceived?.Invoke(this, "");
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
        var process = _process;
        if (process is null || IsProcessRunning(process))
        {
            return;
        }

        process.Dispose();
        _process = null;
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

    public void Dispose()
    {
        if (IsRunning)
        {
            Stop();
        }
        _process?.Dispose();
    }
}
