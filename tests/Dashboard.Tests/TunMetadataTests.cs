using System.Reflection;
using Xunit;

namespace Dashboard.Tests;

public sealed class TunMetadataTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"inbounds\":[]}", false)]
    [InlineData("{\"inbounds\":[{\"type\":\"mixed\"}]}", false)]
    [InlineData("{\"inbounds\":[{\"type\":\"tun\"}]}", true)]
    [InlineData("{\"inbounds\":[{\"type\":\"tun\",\"disabled\":true}]}", false)]
    [InlineData("{\"inbounds\":[{\"type\":\"tun\",\"enabled\":false}]}", false)]
    [InlineData("{\"inbounds\":[{\"type\":\"tun\",\"enabled\":true,\"disabled\":false}]}", true)]
    [InlineData("{\"inbounds\":\"invalid-current-value\"}", null)]
    [InlineData("{\"inbounds\":null}", null)]
    [InlineData("[]", null)]
    [InlineData("{\"inbounds\":[null]}", null)]
    [InlineData("{\"inbounds\":[{}]}", null)]
    [InlineData("{\"inbounds\":[{\"type\":42}]}", null)]
    [InlineData("{\"inbounds\":[{\"type\":\"tun\",\"enabled\":\"false\"}]}", null)]
    [InlineData("{\"inbounds\":[{\"type\":\"tun\",\"disabled\":null}]}", null)]
    [InlineData("{\"inbounds\":[{\"type\":\"tun\"},42]}", null)]
    [InlineData("not-json", null)]
    public void LocalConfigDistinguishesDisabledFromUnknown(string document, bool? expected)
    {
        var path = Path.Combine(TestRoot, "tun.json");
        try
        {
            File.WriteAllText(path, document);
            var readTun = typeof(DashboardHost).GetMethod("ReadTun", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(expected, (bool?)readTun.Invoke(null, [path]));
        }
        finally { File.Delete(path); }
    }
}
