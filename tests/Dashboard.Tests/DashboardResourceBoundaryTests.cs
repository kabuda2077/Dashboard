using System.Diagnostics;
using System.Net;

namespace Dashboard.Tests;

public sealed class DashboardResourceBoundaryTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JunctionBelowAResourceRootCannotExposeSiblingFiles(bool icon)
    {
        var temp = TestRoot;
        var root = Path.Combine(temp, "ui");
        var icons = Path.Combine(temp, "icons");
        var outside = Path.Combine(temp, "outside");
        Directory.CreateDirectory(root); Directory.CreateDirectory(icons); Directory.CreateDirectory(outside);
        var junction = Path.Combine(icon ? icons : root, "linked");
        await File.WriteAllTextAsync(Path.Combine(outside, "private.txt"), "not a dashboard resource");
        using var server = new DashboardServer(root, icons);
        try
        {
            // Directory junctions in this test's temporary tree need no symbolic-link privilege.
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.Arguments = $"/d /c mklink /J \"{junction}\" \"{outside}\"";
            using var create = Process.Start(info)!;
            var output = create.StandardOutput.ReadToEndAsync();
            var error = create.StandardError.ReadToEndAsync();
            await create.WaitForExitAsync();
            Assert.True(create.ExitCode == 0, await output + await error);
            using var client = new HttpClient();
            var origin = server.StartForTests();
            using var response = await client.GetAsync(new Uri(origin, (icon ? "__mihomo/icon-cache/" : "") + "linked/private.txt"));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("not a dashboard resource", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await server.StopAsync();
            if (Directory.Exists(junction)) Directory.Delete(junction);
            CleanupTestDirectory();
        }
    }
}
