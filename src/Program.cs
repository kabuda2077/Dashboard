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
                WebViewDataMaintenance.PrepareForCurrentContent(
                    AppSettings.AppDirectory,
                    DashboardVersion.Current);
                using var applicationContext = new DashboardApplicationContext(startMinimized, startCore);
                context = applicationContext;
                if (Interlocked.Exchange(ref activationPending, 0) != 0)
                {
                    applicationContext.ActivateMainWindow();
                }
                HostOperationLogger.Diagnostic("performance", $"host:applicationContextCreated durationMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0}");
                Application.Run(applicationContext);
            }
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
