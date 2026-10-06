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
                var sidIndex = Array.FindIndex(args, arg => string.Equals(arg, "--expected-user-sid", StringComparison.OrdinalIgnoreCase));
                var expectedSid = sidIndex < 0 ? null : sidIndex + 1 < args.Length ? args[sidIndex + 1] : "";
                Environment.ExitCode = AutostartManager.RunManagementCommand(operation, expectedSid);
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
                WebViewContentUpdate webViewContentUpdate;
                try
                {
                    webViewContentUpdate = WebViewDataMaintenance.PlanForCurrentContent(AppSettings.AppDirectory);
                    webViewContentUpdate.PrepareUserDataDirectory();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    HostOperationLogger.Critical("webview", "WebView update preparation failed; startup cancelled.", exception);
                    MessageBox.Show(exception.Message, "Dashboard 更新准备失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
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
        catch (AppSettingsLoadException exception)
        {
            HostOperationLogger.Critical("settings", "Settings could not be loaded; original retained.", exception);
            MessageBox.Show(
                "设置文件无法读取或不是新版格式，原文件已保留，程序没有恢复默认设置或覆盖它。\n\n"
                    + "请检查文件或使用全新目录重新配置；此版本不迁移旧格式。不要删除唯一副本。\n\n"
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
