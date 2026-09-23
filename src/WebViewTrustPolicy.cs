using System.Text.Json;

namespace Dashboard;

internal enum DashboardNavigationTarget
{
    Dashboard,
    ExternalWeb,
    Blocked
}

// Only the HTML entry is a privileged document. In particular, a cached SVG
// served from this same origin must not acquire the desktop message bridge.
internal sealed class WebViewTrustPolicy(Uri dashboardUri)
{
    public bool IsTrustedDocument(string? address)
    {
        return TryGetWebUri(address, out var uri)
            && IsDashboardOrigin(uri!)
            && uri!.AbsolutePath is "/" or "/index.html";
    }

    public DashboardNavigationTarget ClassifyNavigation(string? address)
    {
        if (IsTrustedDocument(address)) return DashboardNavigationTarget.Dashboard;
        if (!TryGetWebUri(address, out var uri) || IsDashboardOrigin(uri!))
            return DashboardNavigationTarget.Blocked;
        return DashboardNavigationTarget.ExternalWeb;
    }

    public bool CanReceiveMessage(object? sender, object? currentWebView, string? source, string? currentAddress)
    {
        return currentWebView is not null
            && ReferenceEquals(sender, currentWebView)
            && IsTrustedDocument(source)
            && IsTrustedDocument(currentAddress);
    }

    public string DocumentGuardScript =>
        "if (window.top !== window || location.origin !== "
        + JsonSerializer.Serialize(dashboardUri.GetLeftPart(UriPartial.Authority))
        + " || (location.pathname !== '/' && location.pathname !== '/index.html')) return;";

    private bool IsDashboardOrigin(Uri uri) =>
        string.Equals(uri.Scheme, dashboardUri.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.IdnHost, dashboardUri.IdnHost, StringComparison.OrdinalIgnoreCase)
        && uri.Port == dashboardUri.Port;

    private static bool TryGetWebUri(string? address, out Uri? uri) =>
        Uri.TryCreate(address, UriKind.Absolute, out uri)
        && uri.Scheme is "http" or "https"
        && string.IsNullOrEmpty(uri.UserInfo);
}
