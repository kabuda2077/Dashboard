using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Dashboard;

internal static class AutostartManager
{
    internal const string TaskName = @"\Dashboard\Autostart";
    internal const string ScheduledArguments = "--minimized --scheduled-start";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Dashboard";
    private const string LegacyAppName = "MihomoDashboard";
    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 2;

    public static int RunManagementCommand(string operation)
    {
        try
        {
            var result = operation.ToLowerInvariant() switch
            {
                "install" => InstallTask(),
                "remove" => RemoveTask(),
                _ => AutostartOperationResult.Failure($"Unknown autostart operation: {operation}")
            };
            LogResult(operation, result);
            return result.Success ? SuccessExitCode : FailureExitCode;
        }
        catch (Exception ex)
        {
            HostOperationLogger.Error("autostart", $"Autostart management command failed: {operation}.", ex);
            return FailureExitCode;
        }
    }

    public static async Task<AutostartOperationResult> SetEnabledAsync(bool enabled)
    {
        var result = await RunElevatedOperationAsync(enabled ? "install" : "remove");
        var verification = enabled && result.Success ? QueryStatus() : null;
        var taskExistsAfterRemoval = !enabled && result.Success && TaskExists();
        return FinalizeOperation(
            enabled,
            result,
            verification,
            taskExistsAfterRemoval,
            RemoveLegacyRunEntries);
    }

    public static AutostartTaskStatus QueryStatus()
    {
        var query = RunSchtasks(["/Query", "/TN", TaskName, "/XML"]);
        if (query.ExitCode != 0)
        {
            return new AutostartTaskStatus(false, false, query.ErrorText);
        }

        return VerifyTaskXml(
            query.StandardOutput,
            Application.ExecutablePath,
            AppSettings.AppDirectory,
            GetCurrentUserSid(),
            WindowsIdentity.GetCurrent().Name);
    }

    public static bool HasCurrentLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return IsCurrentExecutableValue(key?.GetValue(AppName))
            || IsCurrentExecutableValue(key?.GetValue(LegacyAppName));
    }

    public static void RemoveLegacyRunEntries()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(AppName, false);
        key?.DeleteValue(LegacyAppName, false);
    }

    internal static string BuildTaskXml(string executablePath, string workingDirectory, string userSid)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "Start Dashboard silently after user logon.")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "Delay", "PT5S"))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "IdleSettings",
                        new XElement(ns + "StopOnIdleEnd", "false"),
                        new XElement(ns + "RestartOnIdle", "false")),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    new XElement(ns + "WakeToRun", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Priority", "7")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", executablePath),
                        new XElement(ns + "Arguments", ScheduledArguments),
                        new XElement(ns + "WorkingDirectory", workingDirectory)))));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    internal static AutostartTaskStatus VerifyTaskXml(
        string xml,
        string executablePath,
        string workingDirectory,
        string userSid,
        string? userAccount = null)
    {
        try
        {
            var document = XDocument.Parse(xml);
            var root = document.Root;
            if (root is null)
            {
                return new AutostartTaskStatus(true, false, "Task XML has no root element.");
            }

            XNamespace ns = root.Name.Namespace;
            var command = GetValue(root, ns, "Actions", "Exec", "Command");
            var arguments = GetValue(root, ns, "Actions", "Exec", "Arguments");
            var taskWorkingDirectory = GetValue(root, ns, "Actions", "Exec", "WorkingDirectory");
            var principal = root.Element(ns + "Principals")?.Element(ns + "Principal");
            var trigger = root.Element(ns + "Triggers")?.Element(ns + "LogonTrigger");
            var settings = root.Element(ns + "Settings");

            var checks = new (bool Success, string Message)[]
            {
                (PathsEqual(command, executablePath), "Executable path does not match the current application."),
                (string.Equals(arguments?.Trim(), ScheduledArguments, StringComparison.Ordinal), "Arguments do not match."),
                (PathsEqual(taskWorkingDirectory, workingDirectory), "Working directory does not match."),
                (UserIdMatches(principal?.Element(ns + "UserId")?.Value, userSid, userAccount), "Principal user does not match."),
                (string.Equals(principal?.Element(ns + "LogonType")?.Value, "InteractiveToken", StringComparison.OrdinalIgnoreCase), "Logon type is not InteractiveToken."),
                (string.Equals(principal?.Element(ns + "RunLevel")?.Value, "HighestAvailable", StringComparison.OrdinalIgnoreCase), "Run level is not HighestAvailable."),
                (UserIdMatches(trigger?.Element(ns + "UserId")?.Value, userSid, userAccount), "Logon trigger user does not match."),
                (string.Equals(trigger?.Element(ns + "Delay")?.Value, "PT5S", StringComparison.OrdinalIgnoreCase), "Logon delay is not PT5S."),
                (ReadBool(trigger?.Element(ns + "Enabled"), defaultValue: true), "Logon trigger is disabled."),
                (string.Equals(settings?.Element(ns + "MultipleInstancesPolicy")?.Value, "IgnoreNew", StringComparison.OrdinalIgnoreCase), "Multiple instance policy is not IgnoreNew."),
                (ReadBool(settings?.Element(ns + "StartWhenAvailable")), "StartWhenAvailable is disabled."),
                (!ReadBool(settings?.Element(ns + "DisallowStartIfOnBatteries")), "Task is blocked on battery power."),
                (!ReadBool(settings?.Element(ns + "StopIfGoingOnBatteries")), "Task stops on battery power."),
                (ReadBool(settings?.Element(ns + "Enabled"), defaultValue: true), "Task is disabled."),
                (string.Equals(settings?.Element(ns + "ExecutionTimeLimit")?.Value, "PT0S", StringComparison.OrdinalIgnoreCase), "Execution time is limited.")
            };
            var failed = checks.Where(check => !check.Success).Select(check => check.Message).FirstOrDefault();
            return failed is null
                ? new AutostartTaskStatus(true, true, "Scheduled task is valid.")
                : new AutostartTaskStatus(true, false, failed);
        }
        catch (Exception ex)
        {
            return new AutostartTaskStatus(true, false, $"Invalid task XML: {ex.Message}");
        }
    }

    internal static AutostartOperationResult FinalizeOperation(
        bool enabled,
        AutostartOperationResult operationResult,
        AutostartTaskStatus? verification,
        bool taskExistsAfterRemoval,
        Action removeLegacyEntries)
    {
        if (!operationResult.Success)
        {
            LogResult(enabled ? "install" : "remove", operationResult);
            return operationResult;
        }

        if (enabled && verification?.IsValid != true)
        {
            var result = AutostartOperationResult.Failure(
                $"Scheduled task verification failed: {verification?.Message ?? "missing task status"}");
            LogResult("verify", result);
            return result;
        }

        if (!enabled && taskExistsAfterRemoval)
        {
            var result = AutostartOperationResult.Failure("Scheduled task still exists after removal.");
            LogResult("verify-remove", result);
            return result;
        }

        try
        {
            removeLegacyEntries();
            return operationResult;
        }
        catch (Exception ex)
        {
            return AutostartOperationResult.Failure($"Failed to remove legacy startup entry: {ex.Message}");
        }
    }

    private static AutostartOperationResult InstallTask()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"dashboard-autostart-{Guid.NewGuid():N}.xml");
        try
        {
            EnsureTaskFolder();
            var xml = BuildTaskXml(Application.ExecutablePath, AppSettings.AppDirectory, GetCurrentUserSid());
            File.WriteAllText(tempPath, xml, System.Text.Encoding.Unicode);
            var create = RunSchtasks(["/Create", "/TN", TaskName, "/XML", tempPath, "/F"]);
            if (create.ExitCode != 0)
            {
                return AutostartOperationResult.Failure(create.ErrorText);
            }

            var status = QueryStatus();
            return status.IsValid
                ? AutostartOperationResult.Ok("Scheduled task installed and verified.")
                : AutostartOperationResult.Failure(status.Message);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static AutostartOperationResult RemoveTask()
    {
        var remove = RunSchtasks(["/Delete", "/TN", TaskName, "/F"]);
        if (remove.ExitCode != 0 && TaskExists())
        {
            return AutostartOperationResult.Failure(remove.ErrorText);
        }

        return TaskExists()
            ? AutostartOperationResult.Failure("Scheduled task still exists after deletion.")
            : AutostartOperationResult.Ok("Scheduled task removed.");
    }

    private static async Task<AutostartOperationResult> RunElevatedOperationAsync(string operation)
    {
        if (DashboardHost.IsRunningAsAdministrator())
        {
            return await Task.Run(() => operation == "install" ? InstallTask() : RemoveTask());
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(
                Application.ExecutablePath,
                $"--manage-autostart {operation}")
            {
                WorkingDirectory = AppSettings.AppDirectory,
                UseShellExecute = true,
                Verb = "runas"
            });
            if (process is null)
            {
                return AutostartOperationResult.Failure("Failed to start elevated autostart manager.");
            }

            await process.WaitForExitAsync();
            return process.ExitCode == SuccessExitCode
                ? AutostartOperationResult.Ok($"Autostart {operation} completed.")
                : AutostartOperationResult.Failure($"Elevated autostart manager exited with code {process.ExitCode}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return AutostartOperationResult.Failure("UAC request was cancelled.");
        }
        catch (Exception ex)
        {
            return AutostartOperationResult.Failure(ex.Message);
        }
    }

    private static bool TaskExists()
    {
        return RunSchtasks(["/Query", "/TN", TaskName]).ExitCode == 0;
    }

    private static void EnsureTaskFolder()
    {
        var serviceType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Task Scheduler service is unavailable.");
        object? serviceObject = null;
        object? rootFolderObject = null;
        object? dashboardFolderObject = null;
        try
        {
            serviceObject = Activator.CreateInstance(serviceType)
                ?? throw new InvalidOperationException("Failed to create the Task Scheduler service object.");
            dynamic service = serviceObject;
            service.Connect();
            try
            {
                dashboardFolderObject = service.GetFolder("\\Dashboard");
            }
            catch (Exception ex) when (IsMissingTaskFolderException(ex))
            {
                rootFolderObject = service.GetFolder("\\");
                dynamic rootFolder = rootFolderObject;
                dashboardFolderObject = rootFolder.CreateFolder("Dashboard");
            }
        }
        finally
        {
            ReleaseComObject(dashboardFolderObject);
            ReleaseComObject(rootFolderObject);
            ReleaseComObject(serviceObject);
        }
    }

    internal static bool IsMissingTaskFolderException(Exception exception)
    {
        const int fileNotFoundHResult = unchecked((int)0x80070002);
        return exception is FileNotFoundException or DirectoryNotFoundException
            || exception.HResult == fileNotFoundHResult;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static SchtasksResult RunSchtasks(IEnumerable<string> arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("schtasks.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new SchtasksResult(process.ExitCode, standardOutput, standardError);
        }
        catch (Exception ex)
        {
            return new SchtasksResult(-1, "", ex.Message);
        }
    }

    private static string GetCurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new InvalidOperationException("Unable to resolve the current user SID.");
    }

    private static bool IsCurrentExecutableValue(object? value)
    {
        return value is string text
            && text.Contains(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetValue(XElement root, XNamespace ns, params string[] path)
    {
        XElement? current = root;
        foreach (var segment in path)
        {
            current = current?.Element(ns + segment);
        }
        return current?.Value;
    }

    private static bool ReadBool(XElement? element, bool defaultValue = false)
    {
        return element is null
            ? defaultValue
            : bool.TryParse(element.Value, out var value) && value;
    }

    private static bool UserIdMatches(string? actual, string userSid, string? userAccount)
    {
        return string.Equals(actual, userSid, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(userAccount)
                && string.Equals(actual, userAccount, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void LogResult(string operation, AutostartOperationResult result)
    {
        if (result.Success)
        {
            HostOperationLogger.Info("autostart", $"{operation}: {result.Message}");
        }
        else
        {
            HostOperationLogger.Error("autostart", $"{operation}: {result.Message}", new InvalidOperationException(result.Message));
        }
    }

    private sealed record SchtasksResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string ErrorText => string.IsNullOrWhiteSpace(StandardError) ? StandardOutput.Trim() : StandardError.Trim();
    }
}

internal sealed record AutostartOperationResult(bool Success, string Message)
{
    public static AutostartOperationResult Ok(string message) => new(true, message);
    public static AutostartOperationResult Failure(string message) => new(false, message);
}

internal sealed record AutostartTaskStatus(bool Exists, bool IsValid, string Message);
