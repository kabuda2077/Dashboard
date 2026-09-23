using Dashboard;

namespace Dashboard.Tests;

public sealed class DashboardStatePublisherTests
{
    [Fact]
    public void QueueLogAppendBatchesTextUntilFlush()
    {
        var messages = new List<HostOutboundMessage>();
        using var publisher = CreatePublisher(messages, () => false);

        publisher.QueueLogAppend("first\n");
        publisher.QueueLogAppend("second\n");

        Assert.Empty(messages);

        publisher.Flush();

        var message = Assert.Single(messages);
        Assert.Equal(HostBridgeMessageType.LogAppend, message.Type);
        Assert.Equal("first\nsecond\n", message.LogText);
    }

    [Fact]
    public void FlushDefersDirtyStateWhileUpdatesAreHeld()
    {
        var holdUpdates = true;
        var messages = new List<HostOutboundMessage>();
        using var publisher = CreatePublisher(messages, () => holdUpdates);

        publisher.SendRuntimeState();
        publisher.Flush();

        Assert.Empty(messages);

        holdUpdates = false;
        publisher.Flush();

        Assert.Equal(HostBridgeMessageType.State, Assert.Single(messages).Type);
    }

    [Fact]
    public void DirtyFlushSendsFullStateThenNoticeAndClearsQueuedLog()
    {
        var holdUpdates = false;
        var messages = new List<HostOutboundMessage>();
        using var publisher = CreatePublisher(messages, () => holdUpdates);

        publisher.QueueLogAppend("stale incremental log\n");
        holdUpdates = true;
        publisher.ShowNotice("ready");
        publisher.MarkDirty();

        holdUpdates = false;
        publisher.Flush();
        publisher.Flush();

        Assert.Collection(
            messages,
            message => Assert.Equal(HostBridgeMessageType.State, message.Type),
            message =>
            {
                Assert.Equal(HostBridgeMessageType.Notice, message.Type);
                Assert.Equal("ready", message.Message);
            });
        Assert.DoesNotContain(messages, message => message.Type == HostBridgeMessageType.LogAppend);
    }

    private static DashboardStatePublisher CreatePublisher(
        List<HostOutboundMessage> messages,
        Func<bool> shouldHoldUpdates)
    {
        return new DashboardStatePublisher(
            BuildState,
            () => new DashboardRuntimeState
            {
                IsRunning = false,
                ProcessId = null,
                CoreTitle = "Core",
                CoreVersion = string.Empty,
                CanUpgradeCore = false,
                IsCoreUpgrading = false,
                IsCoreSwitching = false,
                IsWindowMaximized = false
            },
            () => new Dictionary<string, string>(),
            () => true,
            shouldHoldUpdates,
            message => messages.Add(Assert.IsType<HostOutboundMessage>(message)));
    }

    private static DashboardState BuildState()
    {
        return new DashboardState
        {
            IsRunning = false,
            ProcessId = null,
            CoreType = "mihomo",
            CoreTitle = "Mihomo Core",
            CoreVersion = string.Empty,
            CorePath = string.Empty,
            ConfigPath = string.Empty,
            ApiUrl = "http://127.0.0.1:9090",
            Secret = string.Empty,
            MihomoCorePath = string.Empty,
            MihomoConfigPath = string.Empty,
            MihomoApiUrl = "http://127.0.0.1:9090",
            MihomoSecret = string.Empty,
            SingBoxCorePath = string.Empty,
            SingBoxConfigPath = string.Empty,
            SingBoxApiUrl = "http://127.0.0.1:9090",
            SingBoxSecret = string.Empty,
            SetupCompleted = true,
            ReadOnlyTunEnabled = null,
            StartCoreOnLaunch = false,
            MinimizeToTray = false,
            LightweightMode = false,
            Autostart = false,
            AppVersion = "1.0.0",
            LatestAppVersion = string.Empty,
            IsAppUpdateChecking = false,
            AppUpdateAvailable = false,
            LatestCoreVersion = string.Empty,
            IsCoreUpdateChecking = false,
            CoreUpdateAvailable = false,
            CanUpgradeCore = false,
            IsCoreUpgrading = false,
            IsCoreSwitching = false,
            IsWindowMaximized = false,
            LogText = string.Empty,
            IconCacheMap = new Dictionary<string, string>()
        };
    }
}
