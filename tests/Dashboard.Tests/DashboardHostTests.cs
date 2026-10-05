namespace Dashboard.Tests;

public sealed class DashboardHostTests
{
    [Theory]
    [InlineData(false, "https://github.com/MetaCubeX/mihomo")]
    [InlineData(true, "https://github.com/reF1nd/sing-box")]
    public void RepositoryLinkUsesTheSelectedCore(bool singBox, string expected) =>
        Assert.Equal(expected, DashboardHost.GetCoreRepositoryUrl(singBox));

    [Fact]
    public void NewProfileScopesAreStableAndIndependentOfApiAddress()
    {
        Assert.Equal("desktop:mihomo", AppSettings.ProfileId(CoreKind.Mihomo));
        Assert.Equal("desktop:sing-box", AppSettings.ProfileId(CoreKind.SingBox));
        Assert.NotEqual(AppSettings.ProfileId(CoreKind.Mihomo), AppSettings.ProfileId(CoreKind.SingBox));
    }
}
