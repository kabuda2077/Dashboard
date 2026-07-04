using System.Reflection;
using System.Text.Json;
using Dashboard;

namespace Dashboard.Tests;

public sealed class CoreUpdaterTests
{
    [Fact]
    public void SelectsPreferredWindowsX64MihomoAsset()
    {
        using var document = JsonDocument.Parse("""
        {
          "assets": [
            {
              "name": "mihomo-windows-amd64-compatible.zip",
              "browser_download_url": "https://example.test/compatible.zip",
              "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            },
            {
              "name": "mihomo-windows-amd64-v2-go125.zip",
              "browser_download_url": "https://example.test/v2.zip",
              "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
            },
            {
              "name": "mihomo-windows-amd64-v3-go125.zip",
              "browser_download_url": "https://example.test/v3.zip",
              "digest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
            }
          ]
        }
        """);

        var asset = InvokePrivateStatic<object>(
            typeof(CoreUpdater),
            "FindWindowsX64Asset",
            document.RootElement.GetProperty("assets"));

        Assert.Equal("mihomo-windows-amd64-v3-go125.zip", GetProperty<string>(asset, "Name"));
        Assert.Equal("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", GetProperty<string>(asset, "Sha256Digest"));
    }

    [Theory]
    [InlineData("v1.2.3", "release-1.2.3")]
    [InlineData("1.2.3", "v1.2.3")]
    [InlineData("V1.2.3", "Release-1.2.3")]
    public void VersionComparisonNormalizesReleaseAndPrefixMarkers(string installed, string latest)
    {
        var same = InvokePrivateStatic<bool>(typeof(CoreUpdater), "IsSameVersion", installed, latest);

        Assert.True(same);
    }

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object[] arguments)
    {
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(type.FullName, methodName);
        return (T)method.Invoke(null, arguments)!;
    }

    private static T GetProperty<T>(object target, string propertyName)
    {
        return (T)(target.GetType().GetProperty(propertyName)?.GetValue(target)
            ?? throw new MissingMemberException(target.GetType().FullName, propertyName));
    }
}
