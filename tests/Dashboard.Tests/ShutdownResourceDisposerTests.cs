namespace Dashboard.Tests;

public sealed class ShutdownResourceDisposerTests
{
    [Fact]
    public void FailureDoesNotSkipRemainingResources()
    {
        var calls = new List<string>();

        ShutdownResourceDisposer.DisposeAll(
            ("first", () => { calls.Add("first"); throw new InvalidOperationException("expected"); }),
            ("second", () => calls.Add("second")),
            ("third", () => calls.Add("third")));

        Assert.Equal(["first", "second", "third"], calls);
    }
}
