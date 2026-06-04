using Microsoft.Win32;

namespace Dashboard;

public static class AutostartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Dashboard";
    private const string LegacyAppName = "MihomoDashboard";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return IsCurrentExecutableValue(key?.GetValue(AppName))
            || IsCurrentExecutableValue(key?.GetValue(LegacyAppName));
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, true);

        if (enabled)
        {
            key.SetValue(AppName, $"\"{Application.ExecutablePath}\" --minimized");
            key.DeleteValue(LegacyAppName, false);
        }
        else
        {
            key.DeleteValue(AppName, false);
            key.DeleteValue(LegacyAppName, false);
        }
    }

    private static bool IsCurrentExecutableValue(object? value)
    {
        return value is string text && text.Contains(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }
}
