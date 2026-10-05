using System.Text.Json;

namespace Dashboard.Tests;

public sealed class DashboardStatePublisherTests
{
    private static DashboardStatePublisher Publisher(List<JsonElement> messages, Func<bool> held) => new(
        () => new DashboardState(), () => new DashboardRuntimeState(), () => new Dictionary<string, string>(),
        () => true, held, message => messages.Add(JsonSerializer.SerializeToElement(message, HostBridgeJson.JsonOptions)));

    [Fact]
    public void LogAppendIsBatchedUntilFlush()
    {
        var messages = new List<JsonElement>();
        using var publisher = Publisher(messages, () => false);
        publisher.QueueLogAppend("first\n"); publisher.QueueLogAppend("second\n");
        Assert.Empty(messages);
        publisher.Flush();
        var message = Assert.Single(messages);
        Assert.Equal("logAppend", message.GetProperty("type").GetString());
        Assert.Equal("first\nsecond\n", message.GetProperty("logText").GetString());
    }

    [Fact]
    public void HiddenViewGetsOneCurrentSnapshotOnResumeNotAReplayOfOldEvents()
    {
        var messages = new List<JsonElement>();
        var held = true;
        using var publisher = Publisher(messages, () => held);
        publisher.SendRuntimeState(); publisher.QueueLogAppend("old"); publisher.ShowNotice("ready"); publisher.Flush();
        Assert.Empty(messages);
        held = false; publisher.Flush(); publisher.Flush();
        Assert.Equal(new[] { "state", "notice" }, messages.Select(message => message.GetProperty("type").GetString()));
        Assert.Equal("ready", messages[1].GetProperty("message").GetString());
    }
}
