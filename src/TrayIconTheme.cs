using Microsoft.Win32;

namespace Dashboard;

internal sealed class TrayIconTheme : IDisposable
{
    private readonly NotifyIcon _trayIcon;
    private readonly Action<Action> _dispatch;
    private readonly Func<bool> _usesLightTheme;
    private readonly Icon _whiteIcon;
    private readonly Icon _darkIcon;
    private bool _disposed;

    public TrayIconTheme(
        NotifyIcon trayIcon,
        Action<Action> dispatch,
        string resourceDirectory,
        Func<bool>? usesLightTheme = null)
    {
        _trayIcon = trayIcon;
        _dispatch = dispatch;
        _usesLightTheme = usesLightTheme ?? SystemUsesLightTheme;
        _whiteIcon = new Icon(Path.Combine(resourceDirectory, "tray.ico"));
        _darkIcon = new Icon(Path.Combine(resourceDirectory, "tray-light.ico"));
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Refresh();
    }

    private static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        // The taskbar follows Windows mode, independently of the application theme.
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    internal void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        _dispatch(Refresh);

    private void Refresh()
    {
        // A theme notification may already be queued when the application exits.
        if (_disposed) return;
        var icon = _usesLightTheme() ? _darkIcon : _whiteIcon;
        if (!ReferenceEquals(_trayIcon.Icon, icon)) _trayIcon.Icon = icon;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _trayIcon.Icon = null;
        _whiteIcon.Dispose();
        _darkIcon.Dispose();
    }
}
