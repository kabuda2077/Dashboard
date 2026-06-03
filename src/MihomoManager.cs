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

    public bool IsRunning => _process is { HasExited: false };
    public int? ProcessId => IsRunning ? _process!.Id : null;

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

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        _process.OutputDataReceived += (_, e) => AppendLog(e.Data);
        _process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        _process.Exited += (_, _) =>
        {
            AppendLog($"mihomo exited with code {_process?.ExitCode}.");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        };

        if (!_process.Start())
        {
            _process.Dispose();
            _process = null;
            throw new InvalidOperationException("mihomo 启动失败。");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        AppendLog($"mihomo started. pid={_process.Id}");
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            AppendLog("mihomo is not running.");
            return;
        }

        try
        {
            _process!.Kill(entireProcessTree: true);
            _process.WaitForExit(3000);
            AppendLog("mihomo stopped.");
        }
        catch (Exception ex)
        {
            AppendLog($"failed to stop mihomo: {ex.Message}");
            throw;
        }
        finally
        {
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

    public void Dispose()
    {
        if (IsRunning)
        {
            Stop();
        }
        _process?.Dispose();
    }
}
