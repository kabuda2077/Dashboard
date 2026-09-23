namespace Dashboard.Tests;

public sealed class WebViewTrustPolicyTests
{
    private readonly WebViewTrustPolicy _policy = new(new Uri("http://127.0.0.1:33291/"));

    [Theory]
    [InlineData("http://127.0.0.1:33291/")]
    [InlineData("http://127.0.0.1:33291/index.html")]
    [InlineData("http://127.0.0.1:33291/?hostname=localhost#/core")]
    [InlineData("http://127.0.0.1:33291/index.html#/proxies")]
    public void DashboardEntryAndHashRoutesAreTrusted(string uri)
    {
        Assert.True(_policy.IsTrustedDocument(uri));
        Assert.Equal(DashboardNavigationTarget.Dashboard, _policy.ClassifyNavigation(uri));
    }

    [Theory]
    [InlineData("http://127.0.0.1:33292/")]
    [InlineData("https://127.0.0.1:33291/")]
    [InlineData("http://localhost:33291/")]
    [InlineData("http://127.0.0.1.example.com:33291/")]
    [InlineData("http://127.0.0.1:33291@evil.example/")]
    [InlineData("http://user@127.0.0.1:33291/")]
    [InlineData("http://127.0.0.1:33291/__mihomo/icon-cache/icon.svg")]
    [InlineData("http://127.0.0.1:33291/assets/app.js")]
    [InlineData("http://127.0.0.1:33291/other.html")]
    [InlineData("about:blank")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("file:///C:/Windows/test.html")]
    [InlineData("not a URL")]
    [InlineData(null)]
    public void OtherDocumentsNeverReceiveHostPrivileges(string? uri)
    {
        Assert.False(_policy.IsTrustedDocument(uri));
    }

    [Theory]
    [InlineData("https://github.com/MetaCubeX/mihomo", "ExternalWeb")]
    [InlineData("http://example.org/page", "ExternalWeb")]
    [InlineData("mailto:test@example.org", "Blocked")]
    [InlineData("file:///C:/Windows/notepad.exe", "Blocked")]
    [InlineData("https://user:pass@example.org/", "Blocked")]
    [InlineData("http://127.0.0.1:33291/__mihomo/icon-cache/test.svg", "Blocked")]
    public void OnlyCredentialFreeExternalWebLinksCanLeaveTheApp(string uri, string target)
    {
        Assert.Equal(target, _policy.ClassifyNavigation(uri).ToString());
    }

    [Fact]
    public void ObsoleteSenderOrUntrustedCurrentDocumentCannotSendCommands()
    {
        var current = new object();
        const string trusted = "http://127.0.0.1:33291/#/core";
        Assert.True(_policy.CanReceiveMessage(current, current, trusted, trusted));
        Assert.False(_policy.CanReceiveMessage(new object(), current, trusted, trusted));
        Assert.False(_policy.CanReceiveMessage(null, null, trusted, trusted));
        Assert.False(_policy.CanReceiveMessage(current, current, "https://example.org/", trusted));
        Assert.False(_policy.CanReceiveMessage(current, current, trusted, "about:blank"));
    }

    [Fact]
    public void ActualAllocatedServerPortDefinesTrust()
    {
        var policy = new WebViewTrustPolicy(new Uri("http://127.0.0.1:49821/"));
        Assert.True(policy.IsTrustedDocument("http://127.0.0.1:49821/#/core"));
        Assert.False(policy.IsTrustedDocument("http://127.0.0.1:33291/#/core"));
        Assert.Contains("http://127.0.0.1:49821", policy.DocumentGuardScript);
        Assert.Contains("window.top !== window", policy.DocumentGuardScript);
        Assert.Contains("location.pathname", policy.DocumentGuardScript);
    }
}
