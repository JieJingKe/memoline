using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MemoLine;

/// <summary>
/// 纯 Shell_NotifyIcon + 手工 HICON，不引 WinForms。免安装分发时这是普通用户的退出入口。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint ShowHideId = 201;
    private const uint NewId = 202;
    private const uint UndoId = 203;
    private const uint AnchorId = 204;
    private const uint TipsId = 205;
    private const uint LangZhId = 206;
    private const uint LangEnId = 207;
    private const uint QuitId = 208;
    private const uint IconId = 301;

    private readonly IntPtr _hwnd;
    private readonly HwndSource? _source;
    private readonly IntPtr _hicon;
    private bool _added;
    private bool _disposed;

    public TrayIcon(IntPtr hwnd)
    {
        _hwnd = hwnd;
        var size = Math.Clamp(Native.GetSystemMetrics(Native.SM_CXSMICON), 16, 64);
        _hicon = BuildIcon(size);
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        Add(Lang.T("tray.tip.idle"));
        Log.Write($"G6 tray icon added size={size}x{size} shellOk={_added} lastError={Marshal.GetLastWin32Error()}");
    }

    public event Action? ToggleRequested;

    public event Action? NewRequested;

    public event Action? UndoRequested;

    public event Action? AnchorToggleRequested;

    public event Action? TipsRequested;

    public event Action? QuitRequested;

    public bool AnchorEnabled { get; set; } = true;

    public bool UndoAvailable { get; set; }

    public bool IsAdded => _added;

    public void SetTip(string text)
    {
        if (!_added)
        {
            return;
        }

        var data = NewData(text, Native.NIF_TIP);
        Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref data);
    }

    private void Add(string tip)
    {
        var data = NewData(tip, Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP);
        _added = Native.Shell_NotifyIcon(Native.NIM_ADD, ref data);
        var versioned = NewData(tip, Native.NIF_MESSAGE);
        versioned.uVersion = Native.NOTIFY_VERSION_4;
        Native.Shell_NotifyIcon(Native.NIM_SETVERSION, ref versioned);
    }

    private Native.NOTIFYICONDATA NewData(string tip, uint flags) => new()
    {
        cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = Native.WM_TRAYICON,
        hIcon = _hicon,
        szTip = tip,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_TRAYICON)
        {
            return IntPtr.Zero;
        }

        switch (lParam.ToInt32() & 0xFFFF)
        {
            case Native.WM_LBUTTONUP:
            case Native.WM_LBUTTONDBLCLK:
                Log.Write("G6 tray left click → toggle");
                ToggleRequested?.Invoke();
                break;
            case Native.WM_RBUTTONUP:
            case Native.WM_CONTEXTMENU:
                Log.Write("G6 tray right click → menu");
                ShowMenu();
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        Native.GetCursorPos(out var point);
        var focusBefore = Native.GetForegroundWindow();

        // 不先唤起自己，菜单会一直挂着；这个窗口是 NOACTIVATE，通常不会真的抢走前台。
        Native.SetForegroundWindow(_hwnd);

        var menu = Native.CreatePopupMenu();
        Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)ShowHideId, Lang.T("tray.showHide"));
        Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)NewId, Lang.T("tray.new"));
        Native.AppendMenuW(
            menu,
            Native.MF_STRING | (UndoAvailable ? 0 : Native.MF_GRAYED),
            (UIntPtr)UndoId,
            Lang.T("tray.undo"));
        Native.AppendMenuW(
            menu,
            Native.MF_STRING | (AnchorEnabled ? Native.MF_CHECKED : 0),
            (UIntPtr)AnchorId,
            Lang.T("tray.anchor"));
        Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)TipsId, Lang.T("tray.tips"));
        Native.AppendMenuW(menu, Native.MF_SEPARATOR, UIntPtr.Zero, string.Empty);
        Native.AppendMenuW(
            menu,
            Native.MF_STRING | (Lang.Current == AppLanguage.Zh ? Native.MF_CHECKED : 0),
            (UIntPtr)LangZhId,
            Lang.T("tray.langZh"));
        Native.AppendMenuW(
            menu,
            Native.MF_STRING | (Lang.Current == AppLanguage.En ? Native.MF_CHECKED : 0),
            (UIntPtr)LangEnId,
            Lang.T("tray.langEn"));
        Native.AppendMenuW(menu, Native.MF_SEPARATOR, UIntPtr.Zero, string.Empty);
        Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)QuitId, Lang.T("tray.quit"));

        var command = Native.TrackPopupMenuEx(
            menu,
            Native.TPM_RIGHTBUTTON | Native.TPM_RETURNCMD,
            point.X,
            point.Y,
            _hwnd,
            IntPtr.Zero);
        Native.DestroyMenu(menu);
        Native.PostMessage(_hwnd, (uint)Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);

        var focusAfter = Native.GetForegroundWindow();
        Log.Write($"G6 tray menu command={command} focus before=0x{focusBefore.ToInt64():x} after=0x{focusAfter.ToInt64():x} unchanged={focusBefore == focusAfter}");

        switch ((uint)command)
        {
            case ShowHideId:
                ToggleRequested?.Invoke();
                break;
            case NewId:
                NewRequested?.Invoke();
                break;
            case UndoId:
                UndoRequested?.Invoke();
                break;
            case AnchorId:
                AnchorToggleRequested?.Invoke();
                break;
            case TipsId:
                TipsRequested?.Invoke();
                break;
            case LangZhId:
                Lang.Set(AppLanguage.Zh);
                break;
            case LangEnId:
                Lang.Set(AppLanguage.En);
                break;
            case QuitId:
                QuitRequested?.Invoke();
                break;
        }
    }

    /// <summary>白绳 + 银夹 + 牛皮纸便签 + 一颗红点，32 位 BGRA 直接喂 CreateIcon。</summary>
    private static IntPtr BuildIcon(int size)
    {
        const uint White = 0xF2FFFFFF;
        const uint Steel = 0xF2C8D0D8;
        const uint Kraft = 0xF5F0E1C2;
        const uint KraftEdge = 0xF5C0A479;
        const uint Ink = 0xE04A3A24;
        const uint RedDot = 0xF2D64545;

        var pixels = new uint[size * size];

        void Fill(int x, int y, int width, int height, uint color)
        {
            for (var row = y; row < y + height; row++)
            {
                for (var column = x; column < x + width; column++)
                {
                    if (row >= 0 && row < size && column >= 0 && column < size)
                    {
                        pixels[row * size + column] = color;
                    }
                }
            }
        }

        int S(double ratio) => (int)Math.Round(size * ratio);

        var lineTop = S(0.16);
        var lineHeight = Math.Max(1, S(0.09));
        Fill(0, lineTop, size, lineHeight, White);

        var pinWidth = Math.Max(2, S(0.22));
        var pinHeight = Math.Max(3, S(0.30));
        Fill((size - pinWidth) / 2, lineTop - Math.Max(0, S(0.06)), pinWidth, pinHeight, Steel);

        var cardWidth = Math.Max(4, S(0.54));
        var cardTop = lineTop + lineHeight + Math.Max(0, S(0.05));
        var cardHeight = Math.Max(3, size - cardTop - 2);
        var cardLeft = (size - cardWidth) / 2;
        Fill(cardLeft, cardTop, cardWidth, cardHeight, KraftEdge);
        Fill(cardLeft + 1, cardTop + 1, Math.Max(1, cardWidth - 2), Math.Max(1, cardHeight - 2), Kraft);

        var inkLeft = cardLeft + Math.Max(1, S(0.08));
        var inkWidth = Math.Max(1, cardWidth - S(0.16));
        Fill(inkLeft, cardTop + S(0.16), inkWidth, Math.Max(1, S(0.07)), Ink);
        Fill(inkLeft, cardTop + S(0.34), Math.Max(1, inkWidth - S(0.1)), Math.Max(1, S(0.07)), Ink);

        var dotSize = Math.Max(2, S(0.14));
        Fill(cardLeft + cardWidth - dotSize - 1, cardTop + 1, dotSize, dotSize, RedDot);

        var xor = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            var sourceRow = size - 1 - y;
            for (var x = 0; x < size; x++)
            {
                var color = pixels[sourceRow * size + x];
                var index = (y * size + x) * 4;
                xor[index] = (byte)(color & 0xFF);
                xor[index + 1] = (byte)((color >> 8) & 0xFF);
                xor[index + 2] = (byte)((color >> 16) & 0xFF);
                xor[index + 3] = (byte)((color >> 24) & 0xFF);
            }
        }

        var and = new byte[((size + 31) / 32) * 4 * size];
        return Native.CreateIcon(IntPtr.Zero, size, size, 1, 32, and, xor);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source?.RemoveHook(WndProc);

        if (_added)
        {
            var data = NewData(string.Empty, 0);
            Native.Shell_NotifyIcon(Native.NIM_DELETE, ref data);
            Log.Write("G6 tray icon removed");
        }

        if (_hicon != IntPtr.Zero)
        {
            Native.DestroyIcon(_hicon);
        }
    }
}
