using System.Reflection;

namespace Dashboard;

internal static class DashboardVersion
{
    public static string Current { get; } = ReadCurrentVersion();

    private static string ReadCurrentVersion()
    {
        var informationalVersion = typeof(DashboardVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var normalized = AppUpdateChecker.NormalizeVersion(informationalVersion ?? "");
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var version = typeof(DashboardVersion).Assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}
