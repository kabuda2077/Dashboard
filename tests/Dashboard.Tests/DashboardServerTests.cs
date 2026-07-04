using System.Net;
using Dashboard;

namespace Dashboard.Tests;

public sealed class DashboardServerTests
{
    [Fact]
    public async Task StaticRequestsCannotTraverseOutsideDashboardRoot()
    {
        var tempRoot = CreateTempDirectory();
        var root = Path.Combine(tempRoot, "dashboard");
        var outside = Path.Combine(tempRoot, "secret.txt");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "INDEX");
        await File.WriteAllTextAsync(outside, "SECRET");

        using var server = new DashboardServer(root, Path.Combine(tempRoot, "icons"));
        using var client = new HttpClient();
        var baseUri = server.Start();

        var body = await client.GetStringAsync($"{baseUri.AbsoluteUri}%2e%2e%2Fsecret.txt");

        Assert.Equal("INDEX", body);
    }

    [Fact]
    public async Task IconCacheRequestsCannotTraverseOutsideIconCacheRoot()
    {
        var tempRoot = CreateTempDirectory();
        var dashboardRoot = Path.Combine(tempRoot, "dashboard");
        var iconRoot = Path.Combine(tempRoot, "icons");
        Directory.CreateDirectory(dashboardRoot);
        Directory.CreateDirectory(iconRoot);
        await File.WriteAllTextAsync(Path.Combine(dashboardRoot, "index.html"), "INDEX");
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "secret.svg"), "SECRET");

        using var server = new DashboardServer(dashboardRoot, iconRoot);
        using var client = new HttpClient();
        var baseUri = server.Start();

        using var response = await client.GetAsync($"{baseUri.AbsoluteUri}__mihomo/icon-cache/%2e%2e%2Fsecret.svg");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StaticHashedAssetsUseImmutableCacheControlAndEtag()
    {
        var tempRoot = CreateTempDirectory();
        var dashboardRoot = Path.Combine(tempRoot, "dashboard");
        var assetsRoot = Path.Combine(dashboardRoot, "assets");
        Directory.CreateDirectory(assetsRoot);
        await File.WriteAllTextAsync(Path.Combine(dashboardRoot, "index.html"), "INDEX");
        await File.WriteAllTextAsync(Path.Combine(assetsRoot, "index-abc123.js"), "console.log(1)");

        using var server = new DashboardServer(dashboardRoot, Path.Combine(tempRoot, "icons"));
        using var client = new HttpClient();
        var baseUri = server.Start();

        using var response = await client.GetAsync($"{baseUri.AbsoluteUri}assets/index-abc123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString());
        Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task StaticRequestsReturnNotModifiedForMatchingEtag()
    {
        var tempRoot = CreateTempDirectory();
        var dashboardRoot = Path.Combine(tempRoot, "dashboard");
        var assetsRoot = Path.Combine(dashboardRoot, "assets");
        Directory.CreateDirectory(assetsRoot);
        await File.WriteAllTextAsync(Path.Combine(dashboardRoot, "index.html"), "INDEX");
        await File.WriteAllTextAsync(Path.Combine(assetsRoot, "index-abc123.js"), "console.log(1)");

        using var server = new DashboardServer(dashboardRoot, Path.Combine(tempRoot, "icons"));
        using var client = new HttpClient();
        var baseUri = server.Start();

        using var first = await client.GetAsync($"{baseUri.AbsoluteUri}assets/index-abc123.js");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUri.AbsoluteUri}assets/index-abc123.js");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Dashboard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
