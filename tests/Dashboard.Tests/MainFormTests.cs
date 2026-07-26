using System.Runtime.InteropServices;

namespace Dashboard.Tests;

public sealed class MainFormTests
{
    [Fact]
    public void WebViewAbortIsRecognizedAsRecoverableInitializationFailure()
    {
        var aborted = new COMException("aborted", unchecked((int)0x80004004));

        Assert.True(MainForm.IsWebViewInitializationAborted(aborted));
        Assert.True(MainForm.IsWebViewInitializationAborted(new InvalidOperationException("wrapped", aborted)));
        Assert.False(MainForm.IsWebViewInitializationAborted(new COMException("failed", unchecked((int)0x80004005))));
    }
}
