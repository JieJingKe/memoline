using System.Windows.Threading;

namespace MemoLine;

/// <summary>32ms 轮询指针：不用低级鼠标钩子，避免给全系统输入加延迟。</summary>
internal sealed class HotZone
{
    private const int PollMs = 32;
    private const int StayInsideMarginPx = 12;
    private const int LeaveGraceMs = 200;

    private readonly LineWindow _window;
    private readonly DispatcherTimer _timer;
    private long _outsideSince;

    public HotZone(LineWindow window)
    {
        _window = window;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollMs) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        _timer.Start();
        Log.Write($"G2 polling started every {PollMs}ms; trigger = top edge middle 1/3 (max 900 DIP)");
    }

    private void Poll()
    {
        if (_window.IsEditing || !Native.GetCursorPos(out var cursor))
        {
            return;
        }

        if (!_window.IsOpen)
        {
            if (_window.CursorInTriggerZone(cursor) && !_window.ShouldSuppress(cursor))
            {
                _window.Reveal();
            }

            return;
        }

        var bounds = _window.OpenedBoundsPhysical;
        var stillOnRope =
            cursor.X >= bounds.Left - StayInsideMarginPx &&
            cursor.X <= bounds.Right + StayInsideMarginPx &&
            cursor.Y <= bounds.Bottom + StayInsideMarginPx;
        if (stillOnRope || _window.InGrace)
        {
            _outsideSince = 0;
            return;
        }

        // 离开后先等 200ms，避免扫过卡片边缘时绳子闪一下。
        var now = Environment.TickCount64;
        if (_outsideSince == 0)
        {
            _outsideSince = now;
            return;
        }

        if (now - _outsideSince < LeaveGraceMs)
        {
            return;
        }

        _outsideSince = 0;
        _window.Retract();
    }
}
