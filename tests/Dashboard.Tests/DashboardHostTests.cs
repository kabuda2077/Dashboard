namespace Dashboard.Tests;

public sealed class DashboardHostTests
{
    [Fact]
    public void NewProfileScopesAreStableAndIndependentOfApiAddress()
    {
        Assert.Equal("desktop:mihomo", AppSettings.ProfileId(CoreKind.Mihomo));
        Assert.Equal("desktop:sing-box", AppSettings.ProfileId(CoreKind.SingBox));
        Assert.NotEqual(AppSettings.ProfileId(CoreKind.Mihomo), AppSettings.ProfileId(CoreKind.SingBox));
    }
}
