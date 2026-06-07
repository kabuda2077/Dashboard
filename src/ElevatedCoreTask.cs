using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Dashboard;

public static class ElevatedCoreTask
{
    private const string TaskName = "Dashboard Mihomo Core";
    private const int TaskStateRunning = 4;

    public static string GetSignature(AppSettings settings)
    {
        var configDirectory = GetConfigDirectory(settings);
        var command = BuildTaskCommand(settings, configDirectory);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command))).ToLowerInvariant();
    }

    public static void EnsureRegistered(AppSettings settings)
    {
        var signature = GetSignature(settings);
        if (string.Equals(settings.ElevatedCoreTaskSignature, signature, StringComparison.OrdinalIgnoreCase)
            && Exists())
        {
            return;
        }

        Register(settings);
        settings.ElevatedCoreTaskSignature = signature;
        settings.Save();
    }

    public static void Register(AppSettings settings)
    {
        var configDirectory = GetConfigDirectory(settings);
        var command = BuildTaskCommand(settings, configDirectory);
        var args = new[]
        {
            "/Create",
            "/TN", TaskName,
            "/SC", "ONCE",
            "/ST", "00:00",
            "/RL", "HIGHEST",
            "/F",
            "/TR", command
        };

        var exitCode = RunSchtasks(args, elevated: true);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("创建最高权限计划任务失败。");
        }
    }

    public static void Run(AppSettings settings)
    {
        ValidatePaths(settings);
        EnsureRegistered(settings);
        var exitCode = RunSchtasks(new[] { "/Run", "/TN", TaskName }, elevated: false);
        if (exitCode != 0)
        {
            Register(settings);
            exitCode = RunSchtasks(new[] { "/Run", "/TN", TaskName }, elevated: false);
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException("通过计划任务启动 mihomo 失败。");
        }
    }

    public static void Stop()
    {
        _ = RunSchtasks(new[] { "/End", "/TN", TaskName }, elevated: false);
    }

    public static bool IsRunning()
    {
        try
        {
            var task = GetTask();
            return task is not null && (int)task.State == TaskStateRunning;
        }
        catch
        {
            return false;
        }
    }

    public static bool Exists()
    {
        try
        {
            return GetTask() is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string GetConfigDirectory(AppSettings settings)
    {
        return Path.GetDirectoryName(settings.ConfigPath) ?? AppContext.BaseDirectory;
    }

    private static string BuildTaskCommand(AppSettings settings, string configDirectory)
    {
        return $"{Quote(settings.CorePath)} -d {Quote(configDirectory)} -f {Quote(settings.ConfigPath)}";
    }

    private static void ValidatePaths(AppSettings settings)
    {
        if (!File.Exists(settings.CorePath))
        {
            throw new FileNotFoundException("找不到 mihomo 内核，请检查路径。", settings.CorePath);
        }

        if (!File.Exists(settings.ConfigPath))
        {
            throw new FileNotFoundException("找不到 mihomo 配置文件，请检查路径。", settings.ConfigPath);
        }
    }

    private static int RunSchtasks(IEnumerable<string> arguments, bool elevated)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = elevated,
            Verb = elevated ? "runas" : string.Empty
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!elevated)
        {
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return -1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static dynamic? GetTask()
    {
        var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
        if (schedulerType is null)
        {
            return null;
        }

        dynamic service = Activator.CreateInstance(schedulerType)!;
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        try
        {
            return folder.GetTask(TaskName);
        }
        catch
        {
            return null;
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
