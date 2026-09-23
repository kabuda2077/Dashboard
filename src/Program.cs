using System.Diagnostics;

namespace Dashboard;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var diagnosticLogging = args.Any(arg =>
            string.Equals(arg, "--diagnostic-log", StringComparison.OrdinalIgnoreCase));
        HostOperationLogger.Configure(diagnosticLogging);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                ReportCrash(exception);
            }
        };

        try
        {
            ApplicationConfiguration.Initialize();

            var autostartOperationIndex = Array.FindIndex(
                args,
                arg => string.Equals(arg, "--manage-autostart", StringComparison.OrdinalIgnoreCase));
            if (autostartOperationIndex >= 0)
            {
                var operation = autostartOperationIndex + 1 < args.Length
                    ? args[autostartOperationIndex + 1]
                    : "";
                Environment.ExitCode = AutostartManager.RunManagementCommand(operation);
                return;
            }

            var startMinimized = args.Any(arg =>
                string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--scheduled-start", StringComparison.OrdinalIgnoreCase));
            var startCore = args.Any(arg => string.Equals(arg, "--start-core", StringComparison.OrdinalIgnoreCase));
            var elevatedRestart = args.Any(arg => string.Equals(arg, "--elevated-restart", StringComparison.OrdinalIgnoreCase));
            DashboardApplicationContext? context = null;
            var activationPending = 0;
            if (!SingleInstance.TryCreate(
                    () =>
                    {
                        var currentContext = context;
                        if (currentContext is null)
                        {
                            Interlocked.Exchange(ref activationPending, 1);
                            return;
                        }

                        currentContext.ActivateMainWindow();
                    },
                    waitForPreviousExit: elevatedRestart,
                    out var singleInstance))
            {
                return;
            }

            using (singleInstance!)
            {
                var webViewContentUpdate = WebViewDataMaintenance.PlanForCurrentContent(
                    AppSettings.AppDirectory);
                using var applicationContext = new DashboardApplicationContext(
                    startMinimized,
                    startCore,
                    webViewContentUpdate);
                context = applicationContext;
                if (Interlocked.Exchange(ref activationPending, 0) != 0)
                {
                    applicationContext.ActivateMainWindow();
                }
                HostOperationLogger.Diagnostic("performance", $"host:applicationContextCreated durationMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0}");
                Application.Run(applicationContext);
            }
        }
        catch (Exception exception) when (exception is AppSettingsLoadException or AppSettingsMigrationException)
        {
            HostOperationLogger.Critical("settings", "Settings could not be loaded or migrated; original retained.", exception);
            MessageBox.Show(
                "设置文件无法读取或完成安全迁移，原文件已保留，程序没有恢复默认设置或覆盖它。\n\n"
                    + "请检查文件访问权限，并在恢复原文件或修复问题后重试。不要删除唯一副本。\n\n"
                    + exception.Message,
                "Dashboard 设置需要恢复",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (DashboardOriginUnavailableException exception)
        {
            HostOperationLogger.Critical("startup", exception.Message, exception);
            MessageBox.Show(
                exception.Message,
                "Dashboard 本地端口不可用",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception exception)
        {
            ReportCrash(exception);
        }
        finally
        {
            HostOperationLogger.Shutdown(TimeSpan.FromSeconds(2));
        }
    }

    private static void ReportCrash(Exception exception)
    {
        if (IsShutdownNoise(exception))
        {
            return;
        }

        HostOperationLogger.Critical("crash", "Unhandled application exception.", exception);
    }

    private static bool IsShutdownNoise(Exception exception)
    {
        return exception is OperationCanceledException or ObjectDisposedException
            || exception.GetBaseException() is OperationCanceledException or ObjectDisposedException;
    }
}
