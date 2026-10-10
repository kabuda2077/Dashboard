using System.Reflection;

namespace Dashboard.Tests;

public sealed class CrashLoggingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnexpectedCancellationAndDisposalAreNotSilenced(bool wrapped)
    {
        foreach (var error in new Exception[]
        {
            new OperationCanceledException(Guid.NewGuid().ToString("N")),
            new ObjectDisposedException(Guid.NewGuid().ToString("N"))
        })
        {
            var report = typeof(Program).GetMethod("ReportCrash", BindingFlags.Static | BindingFlags.NonPublic)!;
            report.Invoke(null, [wrapped ? new InvalidOperationException("outer", error) : error]);
            var log = File.ReadAllText(Path.Combine(AppSettings.LogDirectory, "crash.log"));
            Assert.Contains(error.Message, log);
        }
    }
}
