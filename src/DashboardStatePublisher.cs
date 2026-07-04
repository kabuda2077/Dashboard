using System.Text;

namespace Dashboard;

internal sealed class DashboardStatePublisher : IDisposable
{
    private const int MinRefreshIntervalMs = 150;
    private const int MaxRefreshDelayMs = 1000;

    private readonly DashboardStateBuilder _stateBuilder;
    private readonly Func<bool> _hasDashboardWebView;
    private readonly Func<bool> _shouldHoldUpdates;
    private readonly Action<object> _postDashboardMessage;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = MinRefreshIntervalMs };
    private readonly System.Windows.Forms.Timer _logAppendTimer = new() { Interval = MinRefreshIntervalMs };
    private readonly StringBuilder _pendingLogAppend = new();
    private bool _stateRefreshPending;
    private bool _dashboardStateDirty;
    private string? _pendingNotice;
    private DateTime _lastStateRefresh = DateTime.MinValue;

    public DashboardStatePublisher(
        DashboardStateBuilder stateBuilder,
        Func<bool> hasDashboardWebView,
        Func<bool> shouldHoldUpdates,
        Action<object> postDashboardMessage)
    {
        _stateBuilder = stateBuilder;
        _hasDashboardWebView = hasDashboardWebView;
        _shouldHoldUpdates = shouldHoldUpdates;
        _postDashboardMessage = postDashboardMessage;
        _refreshTimer.Tick += (_, _) => RefreshNow();
        _logAppendTimer.Tick += (_, _) => FlushLogAppend();
    }

    public void QueueRefresh()
    {
        _stateRefreshPending = true;
        if (_shouldHoldUpdates())
        {
            _dashboardStateDirty = true;
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = (now - _lastStateRefresh).TotalMilliseconds;

        if (elapsed < MinRefreshIntervalMs)
        {
            if (!_refreshTimer.Enabled)
            {
                _refreshTimer.Interval = Math.Max(50, MinRefreshIntervalMs - (int)elapsed);
                _refreshTimer.Start();
            }
        }
        else if (elapsed > MaxRefreshDelayMs)
        {
            RefreshNow();
        }
        else if (!_refreshTimer.Enabled)
        {
            _refreshTimer.Interval = MinRefreshIntervalMs;
            _refreshTimer.Start();
        }
    }

    public void RefreshNow()
    {
        _refreshTimer.Stop();
        _stateRefreshPending = false;
        _lastStateRefresh = DateTime.UtcNow;

        if (_shouldHoldUpdates())
        {
            _dashboardStateDirty = true;
            return;
        }

        SendState();
    }

    public void SendState()
    {
        if (!_hasDashboardWebView() || _shouldHoldUpdates())
        {
            _dashboardStateDirty = true;
            return;
        }

        _postDashboardMessage(HostOutboundMessage.StateMessage(_stateBuilder.Build()));
        ClearPendingLogAppend();
        _dashboardStateDirty = false;
        FlushPendingNotice();
    }

    public void SendRuntimeState()
    {
        if (!_hasDashboardWebView() || _shouldHoldUpdates())
        {
            _dashboardStateDirty = true;
            return;
        }

        _postDashboardMessage(HostOutboundMessage.Runtime(_stateBuilder.BuildRuntime()));
        FlushPendingNotice();
    }

    public void QueueLogAppend(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return;
        }

        if (!_hasDashboardWebView() || _shouldHoldUpdates())
        {
            _dashboardStateDirty = true;
            ClearPendingLogAppend();
            return;
        }

        _pendingLogAppend.Append(logText);
        if (!_logAppendTimer.Enabled)
        {
            _logAppendTimer.Start();
        }
    }

    public void SendIconCacheUpdated()
    {
        if (!_hasDashboardWebView() || _shouldHoldUpdates())
        {
            _dashboardStateDirty = true;
            return;
        }

        _postDashboardMessage(HostOutboundMessage.IconCacheUpdated(_stateBuilder.BuildIconCacheMap()));
    }

    public Task ShowNoticeAsync(string message)
    {
        if (_shouldHoldUpdates() || !_hasDashboardWebView())
        {
            _pendingNotice = message;
            return Task.CompletedTask;
        }

        _postDashboardMessage(HostOutboundMessage.Notice(message));
        return Task.CompletedTask;
    }

    public void SendWindowChromeState(bool isMaximized)
    {
        if (!_hasDashboardWebView() || _shouldHoldUpdates())
        {
            return;
        }

        _postDashboardMessage(HostOutboundMessage.WindowState(isMaximized));
    }

    public void MarkDirty()
    {
        _dashboardStateDirty = true;
    }

    public void StopRefreshTimer()
    {
        _refreshTimer.Stop();
        _logAppendTimer.Stop();
        _stateRefreshPending = false;
    }

    public void Flush()
    {
        if (_shouldHoldUpdates())
        {
            return;
        }

        if (_stateRefreshPending || _dashboardStateDirty)
        {
            StopRefreshTimer();
            SendState();
            return;
        }

        FlushLogAppend();
        FlushPendingNotice();
    }

    private void FlushLogAppend()
    {
        _logAppendTimer.Stop();
        if (_pendingLogAppend.Length == 0)
        {
            return;
        }

        if (!_hasDashboardWebView() || _shouldHoldUpdates())
        {
            ClearPendingLogAppend();
            _dashboardStateDirty = true;
            return;
        }

        var logText = _pendingLogAppend.ToString();
        ClearPendingLogAppend();
        _postDashboardMessage(HostOutboundMessage.LogAppend(logText));
    }

    private void ClearPendingLogAppend()
    {
        _pendingLogAppend.Clear();
    }

    private void FlushPendingNotice()
    {
        if (string.IsNullOrWhiteSpace(_pendingNotice))
        {
            return;
        }

        var message = _pendingNotice;
        _pendingNotice = null;
        _postDashboardMessage(HostOutboundMessage.Notice(message));
    }

    public void Dispose()
    {
        _refreshTimer.Dispose();
        _logAppendTimer.Dispose();
    }
}
