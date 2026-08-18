namespace Dashboard.Tests;

public sealed class DashboardHostTests
{
    [Theory]
    [InlineData(false, DashboardHost.MihomoRepositoryUrl)]
    [InlineData(true, DashboardHost.SingBoxRepositoryUrl)]
    public void SelectsRepositoryForActiveCore(bool isSingBox, string expected)
    {
        Assert.Equal(expected, DashboardHost.GetCoreRepositoryUrl(isSingBox));
    }
}
