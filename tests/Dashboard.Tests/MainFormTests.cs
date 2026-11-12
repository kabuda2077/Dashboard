using System.Runtime.InteropServices;

namespace Dashboard.Tests;

public sealed class MainFormTests
{
    [Fact]
    public void WebSocketDurationsDoNotChangeTheStartupNavigationTimeline()
    {
        var previousDocumentMs = 0d;
        Assert.Contains("documentMs=100", MainForm.FormatTrayRestoreMetric("frontend:bootstrapRequested", 100, ref previousDocumentMs));
        var socket = MainForm.FormatTrayRestoreMetric("frontend:ws:traffic:firstMessage", 5, ref previousDocumentMs);
        Assert.StartsWith("durationMs=5", socket);
        Assert.DoesNotContain("documentMs", socket);
        Assert.Equal(100, previousDocumentMs);
        Assert.Contains("sincePreviousMs=20", MainForm.FormatTrayRestoreMetric("frontend:bootstrapReceived", 120, ref previousDocumentMs));
        Assert.Equal(120, previousDocumentMs);
    }

    [Fact]
    public void WebViewAbortIsRecognizedAsRecoverableInitializationFailure()
    {
        var aborted = new COMException("aborted", unchecked((int)0x80004004));

        Assert.True(MainForm.IsWebViewInitializationAborted(aborted));
        Assert.True(MainForm.IsWebViewInitializationAborted(new InvalidOperationException("wrapped", aborted)));
        Assert.False(MainForm.IsWebViewInitializationAborted(new COMException("failed", unchecked((int)0x80004005))));
    }
}
