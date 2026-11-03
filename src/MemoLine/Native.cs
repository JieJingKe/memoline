using System.Runtime.InteropServices;
using System.Text;

namespace MemoLine;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;

        public readonly override string ToString() => $"({Left},{Top},{Width}x{Height})";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;

        public readonly override string ToString() => $"{szDevice} monitor={rcMonitor} work={rcWork}";
    }

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_EX_TRANSPARENT = 0x00000020;

    public const int WM_NULL = 0x0000;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_HOTKEY = 0x0312;
    public const int MA_NOACTIVATE = 0x0003;

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_SHIFT = 0x0004;

    public const int WM_APP = 0x8000;
    public const int WM_TRAYICON = WM_APP + 2;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_CONTEXTMENU = 0x007B;

    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIM_SETVERSION = 0x00000004;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NOTIFY_VERSION_4 = 0x00000004;

    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_CHECKED = 0x00000008;
    public const uint MF_GRAYED = 0x00000001;
    public const uint TPM_RIGHTBUTTON = 0x00000002;
    public const uint TPM_RETURNCMD = 0x00000100;
    public const int SM_CXSMICON = 49;
    public const int SM_CXDOUBLECLK = 36;
    public const int SM_CYDOUBLECLK = 37;

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIcon(IntPtr hInstance, int width, int height, byte planes, byte bitsPixel, byte[] andMask, byte[] xorMask);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(IntPtr hMenu, uint flags, UIntPtr id, string text);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    public static void MakeNonActivating(IntPtr hwnd)
    {
        var current = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(current | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    /// <summary>编辑态需要键盘焦点：只摘掉 NOACTIVATE，TOOLWINDOW 保留（不进 Alt+Tab）。</summary>
    public static void MakeActivatable(IntPtr hwnd)
    {
        var current = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(current & ~WS_EX_NOACTIVATE));
        var after = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        Log.Write($"exstyle make-activatable hwnd=0x{hwnd.ToInt64():x} before=0x{current:x} after=0x{after:x} noactivate={(after & WS_EX_NOACTIVATE) != 0}");
    }

    public static void MakeClickThrough(IntPtr hwnd)
    {
        var current = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var before = current;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(current | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT));
        var after = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        Log.Write($"exstyle click-through hwnd=0x{hwnd.ToInt64():x} before=0x{before:x} after=0x{after:x} transparent={(after & WS_EX_TRANSPARENT) != 0}");
    }

    /// <summary>空白区域让点击穿到下层窗口；只改扩展样式，不触发重绘。</summary>
    public static void SetClickThrough(IntPtr hwnd, bool on)
    {
        var current = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var next = on ? current | WS_EX_TRANSPARENT : current & ~WS_EX_TRANSPARENT;
        if (next != current)
        {
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(next));
        }
    }

    public static string DescribeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "none";
        }

        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        GetWindowThreadProcessId(hwnd, out var pid);
        var process = "unknown";
        try
        {
            process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
        }
        catch (Exception)
        {
        }

        return $"0x{hwnd.ToInt64():x} [{process}] \"{sb}\"";
    }
}
