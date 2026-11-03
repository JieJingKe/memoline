namespace MemoLine;

/// <summary>
/// 顶边几何的唯一来源：触发区恒定是工作区中间 1/3，绳子本体宽度随便签数变化但封顶在工作区内。
/// </summary>
internal static class Zone
{
    public const int EdgeThresholdPx = 6;
    public const double MaxWidthDip = 900;
    public const double TriggerRatioOfWorkArea = 1.0 / 3.0;
    public const double RopeRatioOfWorkArea = 0.4;

    public static (double LeftDip, double WidthDip) Trigger(Native.MONITORINFOEX monitor, double scale)
    {
        var workWidthDip = (double)monitor.rcWork.Width / scale;
        var widthDip = Math.Min(workWidthDip * TriggerRatioOfWorkArea, MaxWidthDip);
        return (monitor.rcWork.Left / scale + (workWidthDip - widthDip) / 2, widthDip);
    }

    public static (double LeftDip, double WidthDip) Span(Native.MONITORINFOEX monitor, double scale, double minWidthDip)
    {
        var workWidthDip = WorkWidthDip(monitor, scale);
        var natural = Math.Min(workWidthDip * RopeRatioOfWorkArea, MaxWidthDip);
        var widthDip = Math.Clamp(Math.Max(natural, minWidthDip), 120, workWidthDip - 24);
        return (monitor.rcWork.Left / scale + (workWidthDip - widthDip) / 2, widthDip);
    }

    public static double WorkWidthDip(Native.MONITORINFOEX monitor, double scale) => (double)monitor.rcWork.Width / scale;

    public static bool Contains(Native.POINT cursor, Native.MONITORINFOEX monitor, double scale)
    {
        var (leftDip, widthDip) = Trigger(monitor, scale);
        var left = leftDip * scale;
        return cursor.Y <= monitor.rcWork.Top + EdgeThresholdPx
            && cursor.X >= left
            && cursor.X <= left + widthDip * scale;
    }
}
