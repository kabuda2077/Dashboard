using System.Text.Json;
using Dashboard;

namespace Dashboard.Tests;

public sealed class SingBoxUpdaterTests
{
    [Theory]
    [InlineData("1.14.0-alpha.35-reF1nd.1")]
    [InlineData("1.14.0-beta.12-reF1nd")]
    [InlineData("1.14.0-rc.1-reF1nd")]
    public void PrereleaseInstallSelectsLatestPrerelease(string installedVersion)
    {
        using var releases = CreateReleases();

        var selected = SingBoxUpdater.FindMatchingRelease(releases.RootElement, installedVersion);

        Assert.Equal("v1.14.0-beta.14-reF1nd", selected.GetProperty("tag_name").GetString());
    }

    [Fact]
    public void StableInstallSelectsLatestStableRelease()
    {
        using var releases = CreateReleases();

        var selected = SingBoxUpdater.FindMatchingRelease(releases.RootElement, "1.13.16-reF1nd");

        Assert.Equal("v1.13.18-reF1nd", selected.GetProperty("tag_name").GetString());
    }

    private static JsonDocument CreateReleases()
    {
        return JsonDocument.Parse("""
            [
              {
                "tag_name": "v1.13.18-reF1nd",
                "prerelease": false,
                "published_at": "2026-08-02T00:00:00Z"
              },
              {
                "tag_name": "v1.14.0-beta.12-reF1nd",
                "prerelease": true,
                "published_at": "2026-08-03T00:00:00Z"
              },
              {
                "tag_name": "v1.14.0-beta.14-reF1nd",
                "prerelease": true,
                "published_at": "2026-08-13T00:00:00Z"
              }
            ]
            """);
    }
}
