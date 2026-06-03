namespace MihomoDashboard;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var startMinimized = args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        using var form = new MainForm(startMinimized);
        Application.Run(form);
    }
}
