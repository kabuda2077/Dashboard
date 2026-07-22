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
            MainForm? form = null;
            if (!SingleInstance.TryCreate(
                    () =>
                    {
                        try
                        {
                            if (form is not null && !form.IsDisposed && form.IsHandleCreated)
                            {
                                form.BeginInvoke(new Action(form.ShowFromTray));
                            }
                        }
                        catch
                        {
                        }
                    },
                    waitForPreviousExit: elevatedRestart,
                    out var singleInstance))
            {
                return;
            }

            using (singleInstance!)
            {
                using var host = new DashboardHost();
                using var mainForm = new MainForm(host, startMinimized, startCore);
                form = mainForm;
                HostOperationLogger.Info("performance", $"host:mainFormCreated durationMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0}");
                Application.Run(mainForm);
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
