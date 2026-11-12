namespace Dashboard.Tests;

public sealed class CoreUpdateSourceTests
{
    [Theory]
    [InlineData("Mihomo Meta TanakaLun-Alpha-9fc52a02 windows amd64 with go1.26.8", "manual", "TanakaLun/mihomo")]
    [InlineData("TanakaLun-Alpha-9fc52a02", "manual", "TanakaLun/mihomo")]
    [InlineData("Mihomo Meta alpha-smart-ab12 windows amd64", "manual", "vernesong/mihomo")]
    [InlineData("Mihomo Smart v1.19.29 windows amd64", "manual", "vernesong/mihomo")]
    [InlineData("Clash.Meta alpha-smart-ab12 windows amd64", "manual", "vernesong/mihomo")]
    [InlineData("Mihomo Meta v1.19.29 windows amd64 with go1.26.8", "automatic", "MetaCubeX/mihomo")]
    [InlineData("v1.19.29", "automatic", "MetaCubeX/mihomo")]
    [InlineData("Mihomo Meta alpha-abc123 windows amd64", "automatic", "MetaCubeX/mihomo")]
    [InlineData("beta-ABC123", "automatic", "MetaCubeX/mihomo")]
    [InlineData("meta-ab123456", "automatic", "MetaCubeX/mihomo")]
    [InlineData("Mihomo Meta v1.19.29-custom windows amd64", "unknown", "")]
    [InlineData("Mihomo Meta custom windows amd64 with go1.26.8", "unknown", "")]
    [InlineData("Mihomo Meta alpha-custom windows amd64", "unknown", "")]
    [InlineData("Mihomo Meta 1.19.29 windows amd64", "unknown", "")]
    [InlineData("third-party v1.19.29", "unknown", "")]
    [InlineData("unrecognized output\nMihomo Meta v1.19.29", "unknown", "")]
    [InlineData("", "unknown", "")]
    public void MihomoUsesOnlyKnownVersionFields(string output, string mode, string repository)
    {
        var source = CoreUpdateSource.Resolve(output, CoreKind.Mihomo);
        Assert.Equal(mode, source.Mode);
        Assert.Equal(repository.Length == 0 ? "" : "https://github.com/" + repository + "/releases", source.ReleaseUrl);
        Assert.Equal(repository.Length == 0 ? "" : "https://github.com/" + repository, CoreUpdateSource.RepositoryUrlFor(source.ReleaseUrl));
    }

    [Theory]
    [InlineData("sing-box version 1.13.19-reF1nd", "manual", "reF1nd/sing-box-releases")]
    [InlineData("sing-box version 1.14.0-beta.15-reF1nd.2", "manual", "reF1nd/sing-box-releases")]
    [InlineData("sing-box version 1.13.19", "manual", "SagerNet/sing-box")]
    [InlineData("sing-box version v1.14.0-rc.1", "manual", "SagerNet/sing-box")]
    [InlineData("sing-box version 1.14.0-beta.15", "manual", "SagerNet/sing-box")]
    [InlineData("sing-box version 1.14.0-custom", "unknown", "")]
    [InlineData("sing-box version custom\nEnvironment: go1.26.8", "unknown", "")]
    [InlineData("custom sing-box 1.14.0", "unknown", "")]
    public void SingBoxAlwaysRequiresManualReplacement(string output, string mode, string repository)
    {
        var source = CoreUpdateSource.Resolve(output, CoreKind.SingBox);
        Assert.Equal(mode, source.Mode);
        Assert.Equal(repository.Length == 0 ? "" : "https://github.com/" + repository + "/releases", source.ReleaseUrl);
        var project = repository == "reF1nd/sing-box-releases" ? "reF1nd/sing-box" : repository;
        Assert.Equal(project.Length == 0 ? "" : "https://github.com/" + project, CoreUpdateSource.RepositoryUrlFor(source.ReleaseUrl));
    }
}
