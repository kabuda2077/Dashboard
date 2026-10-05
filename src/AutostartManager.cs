using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Dashboard;

internal static class AutostartManager
{
    internal const string TaskName = @"\Dashboard\Autostart";
    internal const string ScheduledArguments = "--minimized --scheduled-start";
    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 2;

    public static int RunManagementCommand(string operation, string? expectedUserSid = null)
    {
        try
        {
            if (expectedUserSid is not null && !string.Equals(expectedUserSid, GetCurrentUserSid(), StringComparison.Ordinal))
            {
                LogResult(operation, AutostartOperationResult.Failure("Elevation changed the Windows account; no autostart task was modified."));
                return FailureExitCode;
            }
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
        return await Task.Run(async () =>
        {
            var before = ReadTaskDefinition();
            var permission = CheckOwnership(before, Application.ExecutablePath, AppSettings.AppDirectory, GetCurrentUserSid(), WindowsIdentity.GetCurrent().Name);
            if (!permission.Success) return permission;
            if (!enabled && !before.Exists) return AutostartOperationResult.Ok("No current autostart task exists.");
            var result = await RunElevatedOperationAsync(enabled ? "install" : "remove").ConfigureAwait(false);
            if (!result.Success) return result;
            var after = ReadTaskDefinition();
            if (after.Error is not null) return AutostartOperationResult.Failure("Task operation completed but verification failed: " + after.Error);
            return FinalizeOperation(enabled, result,
                enabled ? Status(after, Application.ExecutablePath, AppSettings.AppDirectory, GetCurrentUserSid(), WindowsIdentity.GetCurrent().Name) : null,
                !enabled && after.Exists);
        }).ConfigureAwait(false);
    }

    public static AutostartTaskStatus QueryStatus() =>
        Status(ReadTaskDefinition(), Application.ExecutablePath, AppSettings.AppDirectory, GetCurrentUserSid(), WindowsIdentity.GetCurrent().Name);

    private static AutostartTaskStatus Status(AutostartTaskDefinition task, string exe, string directory, string sid, string? account) =>
        task.Error is not null ? new(false, false, task.Error)
        : !task.Exists ? new(false, false, "No autostart task exists.")
        : VerifyTaskXml(task.Xml ?? "", exe, directory, sid, account);

    internal static AutostartOperationResult CheckOwnership(AutostartTaskDefinition task, string exe, string directory, string sid, string? account = null)
    {
        if (task.Error is not null) return AutostartOperationResult.Failure("Cannot determine autostart task ownership: " + task.Error);
        if (!task.Exists) return AutostartOperationResult.Ok("No existing task.");
        try
        {
            var root = XDocument.Parse(task.Xml ?? "").Root ?? throw new FormatException("Missing task definition.");
            XNamespace ns = root.Name.Namespace;
            var principals = root.Element(ns + "Principals")?.Elements(ns + "Principal").ToArray() ?? [];
            var actions = root.Element(ns + "Actions")?.Elements().ToArray() ?? [];
            var logons = root.Element(ns + "Triggers")?.Elements(ns + "LogonTrigger").ToArray() ?? [];
            if (principals.Length == 1 && UserIdMatches(principals[0].Element(ns + "UserId")?.Value, sid, account)
                && actions.Length == 1 && actions[0].Name == ns + "Exec"
                && PathsEqual(actions[0].Element(ns + "Command")?.Value, exe)
                && PathsEqual(actions[0].Element(ns + "WorkingDirectory")?.Value, directory)
                && logons.All(trigger => UserIdMatches(trigger.Element(ns + "UserId")?.Value, sid, account)))
                return AutostartOperationResult.Ok("Task belongs to this user and installation.");
        }
        catch (Exception) { }
        return AutostartOperationResult.Failure("同名自启任务不属于当前 Windows 用户和 Dashboard 目录，未覆盖或删除。请在原目录或任务计划程序中确认归属后处理。");
    }

    internal static AutostartOperationResult ChangeOwnedTask(bool enabled, Func<AutostartTaskDefinition> query,
        Func<bool, AutostartOperationResult> mutate, string exe, string directory, string sid, string? account = null)
    {
        var before = query();
        var permission = CheckOwnership(before, exe, directory, sid, account);
        if (!permission.Success) return permission;
        if (!enabled && !before.Exists) return AutostartOperationResult.Ok("No task to remove.");
        var changed = mutate(before.Exists);
        if (!changed.Success) return changed;
        var after = query();
        if (after.Error is not null) return AutostartOperationResult.Failure("Task operation completed but verification failed: " + after.Error);
        return FinalizeOperation(enabled, changed, enabled ? Status(after, exe, directory, sid, account) : null, !enabled && after.Exists);
    }

    private static AutostartTaskDefinition ReadTaskDefinition() => ReadTaskDefinition(TaskName);

    internal static AutostartTaskDefinition ReadTaskDefinition(string taskPath)
    {
        object? service = null, folder = null, task = null;
        var queryingTask = false;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler is unavailable.");
            service = Activator.CreateInstance(type)!;
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            queryingTask = true;
            task = ((dynamic)folder).GetTask(taskPath);
            return new(true, (string)((dynamic)task).Xml);
        }
        // COM interop can translate these HRESULTs into FileNotFoundException /
        // DirectoryNotFoundException rather than preserving COMException.
        catch (Exception error) when (queryingTask && unchecked((uint)error.GetBaseException().HResult) is 0x80070002 or 0x80070003)
        { return new(false); }
        catch (Exception error) { return new(false, Error: error.Message); }
        finally
        {
            foreach (var value in new[] { task, folder, service })
                if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
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
        bool taskExistsAfterRemoval)
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

        return operationResult;
    }

    private static AutostartOperationResult InstallTask()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"dashboard-autostart-{Guid.NewGuid():N}.xml");
        try
        {
            var xml = BuildTaskXml(Application.ExecutablePath, AppSettings.AppDirectory, GetCurrentUserSid());
            File.WriteAllText(tempPath, xml, System.Text.Encoding.Unicode);
            return ChangeOwnedTask(true, ReadTaskDefinition, replaceOwned =>
            {
                var arguments = new List<string> { "/Create", "/TN", TaskName, "/XML", tempPath };
                // An absent task must not become an unconditional overwrite if another user creates it meanwhile.
                if (replaceOwned) arguments.Add("/F");
                var create = RunSchtasks(arguments);
                return create.ExitCode == 0 ? AutostartOperationResult.Ok("Scheduled task installed.") : AutostartOperationResult.Failure(create.ErrorText);
            }, Application.ExecutablePath, AppSettings.AppDirectory, GetCurrentUserSid(), WindowsIdentity.GetCurrent().Name);
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

    private static AutostartOperationResult RemoveTask() => ChangeOwnedTask(false, ReadTaskDefinition, _ =>
    {
        var remove = RunSchtasks(["/Delete", "/TN", TaskName, "/F"]);
        return remove.ExitCode == 0 ? AutostartOperationResult.Ok("Scheduled task removed.") : AutostartOperationResult.Failure(remove.ErrorText);
    }, Application.ExecutablePath, AppSettings.AppDirectory, GetCurrentUserSid(), WindowsIdentity.GetCurrent().Name);

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
                $"--manage-autostart {operation} --expected-user-sid {GetCurrentUserSid()}")
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
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                }
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            process.StandardInput.Close(); // never answer an unexpected overwrite prompt with consent
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var output = process.StandardOutput.ReadToEndAsync(budget.Token);
            var error = process.StandardError.ReadToEndAsync(budget.Token);
            try
            {
                process.WaitForExitAsync(budget.Token).GetAwaiter().GetResult();
                Task.WhenAll(output, error).GetAwaiter().GetResult();
                return new SchtasksResult(process.ExitCode, output.Result, error.Result);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                return new SchtasksResult(-1, "", "Task Scheduler command timed out; system state must be rechecked.");
            }
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
internal sealed record AutostartTaskDefinition(bool Exists, string? Xml = null, string? Error = null);
