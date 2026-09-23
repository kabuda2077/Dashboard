using System.Text;

namespace Dashboard;

internal sealed class DashboardStatePublisher : IDisposable
{
    private const int LogAppendIntervalMs = 150;

    private readonly Func<DashboardState> _buildState;
    private readonly Func<DashboardRuntimeState> _buildRuntimeState;
    private readonly Func<IReadOnlyDictionary<string, string>> _buildIconCacheMap;
    private readonly Func<bool> _hasDashboardWebView;
    private readonly Func<bool> _shouldHoldUpdates;
    private readonly Action<object> _postDashboardMessage;
    private readonly System.Windows.Forms.Timer _logAppendTimer = new() { Interval = LogAppendIntervalMs };
    private readonly StringBuilder _pendingLogAppend = new();
    private bool _dashboardStateDirty;
    private string? _pendingNotice;

    public DashboardStatePublisher(
        Func<DashboardState> buildState,
        Func<DashboardRuntimeState> buildRuntimeState,
        Func<IReadOnlyDictionary<string, string>> buildIconCacheMap,
        Func<bool> hasDashboardWebView,
        Func<bool> shouldHoldUpdates,
        Action<object> postDashboardMessage)
    {
        _buildState = buildState;
        _buildRuntimeState = buildRuntimeState;
        _buildIconCacheMap = buildIconCacheMap;
        _hasDashboardWebView = hasDashboardWebView;
        _shouldHoldUpdates = shouldHoldUpdates;
        _postDashboardMessage = postDashboardMessage;
        _logAppendTimer.Tick += (_, _) => FlushLogAppend();
    }

    public void SendState()
    {
        if (!_hasDashboardWebView() || _shouldHoldUpdates())
        {
            _dashboardStateDirty = true;
            return;
        }

        _postDashboardMessage(HostOutboundMessage.StateMessage(_buildState()));
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

        _postDashboardMessage(HostOutboundMessage.Runtime(_buildRuntimeState()));
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

        _postDashboardMessage(HostOutboundMessage.IconCacheUpdated(_buildIconCacheMap()));
    }

    public void ShowNotice(string message)
    {
        if (_shouldHoldUpdates() || !_hasDashboardWebView())
        {
            _pendingNotice = message;
            return;
        }

        _postDashboardMessage(HostOutboundMessage.Notice(message));
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
        _logAppendTimer.Stop();
        _dashboardStateDirty = true;
    }

    public void Flush()
    {
        if (_shouldHoldUpdates())
        {
            return;
        }

        if (_dashboardStateDirty)
        {
            _logAppendTimer.Stop();
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
        _logAppendTimer.Dispose();
    }
}
