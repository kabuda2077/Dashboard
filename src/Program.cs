using System.Diagnostics;

namespace Dashboard;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var startedAt = Stopwatch.GetTimestamp();
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

            var startMinimized = args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
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
                using var applicationContext = new DashboardApplicationContext(startMinimized, startCore);
                context = applicationContext;
                if (Interlocked.Exchange(ref activationPending, 0) != 0)
                {
                    applicationContext.ActivateMainWindow();
                }
                HostOperationLogger.Info("performance", $"host:applicationContextCreated durationMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0}");
                Application.Run(applicationContext);
            }
        }
        catch (Exception exception)
        {
            ReportCrash(exception);
        }
    }

    private static void ReportCrash(Exception exception)
    {
        if (IsShutdownNoise(exception))
        {
            return;
        }

        var logPath = Path.Combine(AppSettings.LogDirectory, "crash.log");
        Directory.CreateDirectory(AppSettings.LogDirectory);
        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
    }

    private static bool IsShutdownNoise(Exception exception)
    {
        return exception is OperationCanceledException or ObjectDisposedException
            || exception.GetBaseException() is OperationCanceledException or ObjectDisposedException;
    }
}
