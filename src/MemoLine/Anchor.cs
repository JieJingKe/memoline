using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MemoLine;

/// <summary>可点但绝不激活的置顶透明窗。装饰窗走 WS_EX_TRANSPARENT + 不参与命中。</summary>
internal abstract class GhostWindow : Window
{
    protected IntPtr Handle;

    protected GhostWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.Manual;
        IsHitTestVisible = !ClickThrough;
    }

    protected virtual bool ClickThrough => true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = ((HwndSource?)PresentationSource.FromVisual(this))?.Handle ?? IntPtr.Zero;
        if (Handle == IntPtr.Zero)
        {
            return;
        }

        if (ClickThrough)
        {
            Native.MakeClickThrough(Handle);
        }
        else
        {
            Native.MakeNonActivating(Handle);
        }

        ((HwndSource)PresentationSource.FromVisual(this)!).AddHook((_, msg, _, _, ref handled) =>
        {
            if (msg == Native.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            }

            return IntPtr.Zero;
        });
    }
}

internal sealed class AnchorLine : GhostWindow
{
    public const double CordWidthDip = 34;
    public const double CordHeightDip = 50;

    /// <summary>绳头横向位置：工作区宽度的 2/3 处（沿用 Clipline 的用户指定位置）。</summary>
    public const double AnchorRatio = 2.0 / 3.0;

    private const string PullCordData =
        "M544 388.5V32c0-17.7-14.3-32-32-32s-32 14.3-32 32v356.5c0 42.2-21.2 81.3-55.9 105.2-86.6 59.4-147.7 195.6-147.7 294.7 0 130.1 105.5 235.6 235.6 235.6s235.6-105.5 235.6-235.6c0-99-61.1-235.2-147.7-294.7-34.7-23.8-55.9-63-55.9-105.2z m89.3 521.2C600.9 942.1 557.8 960 512 960s-88.9-17.9-121.3-50.3c-32.4-32.4-50.3-75.5-50.3-121.3 0-52.8 23-121.8 58.5-175.8 17.9-27.3 38.3-49.5 58.8-64.2 18.8-13.5 37.1-20.4 54.4-20.4s35.5 6.8 54.4 20.4c20.5 14.7 40.8 36.9 58.8 64.2 35.5 54 58.5 123 58.5 175.8-0.2 45.8-18 88.9-50.5 121.3z";

    public static readonly Geometry CordGeometry = Geometry.Parse(PullCordData);

    private static readonly Brush NormalFill = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 0xC9, 0x9C, 0x66)));
    private static readonly Brush HoverFill = Freeze(new SolidColorBrush(Color.FromArgb(0xFF, 0xE8, 0xC7, 0x8C)));
    private static readonly Brush NormalStroke = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 0x5A, 0x3C, 0x1E)));
    private static readonly Brush HoverStroke = Freeze(new SolidColorBrush(Color.FromArgb(0xFF, 0x8A, 0x5F, 0x2E)));

    private readonly System.Windows.Shapes.Path _cord;

    public AnchorLine()
    {
        Width = CordWidthDip;
        Height = CordHeightDip;
        _cord = new System.Windows.Shapes.Path
        {
            Data = CordGeometry,
            Stretch = Stretch.Uniform,
            Fill = NormalFill,
            Stroke = NormalStroke,
            StrokeThickness = 1.1,
        };
        Content = _cord;
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    protected override bool ClickThrough => false;

    public event Action? PullRequested;

    public void PlaceOnPrimaryMonitor(LineWindow rope)
    {
        var monitor = rope.PrimaryMonitor;
        var scale = rope.Scale;
        var centerPhysical = monitor.rcWork.Left + ((double)monitor.rcWork.Width * AnchorRatio);
        Left = (centerPhysical - (CordWidthDip * scale / 2)) / scale;
        Top = monitor.rcMonitor.Top / scale;
        Log.Write($"G4 anchor placed physical=({Left * scale:F0},{Top * scale:F0},{CordWidthDip * scale:F0}x{CordHeightDip * scale:F0}) scale={scale:F2} interactive=True");
    }

    public void SetVisible(bool visible)
    {
        if (visible == IsVisible)
        {
            return;
        }

        Log.Write($"G4 anchor visible={visible}");
        if (visible)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _cord.Fill = HoverFill;
        _cord.Stroke = HoverStroke;
        PullRequested?.Invoke();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _cord.Fill = NormalFill;
        _cord.Stroke = NormalStroke;
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        PullRequested?.Invoke();
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}

internal sealed class OnboardingTip : GhostWindow
{
    private readonly TextBlock _text;

    public OnboardingTip()
    {
        Width = 300;
        Height = 52;
        _text = new TextBlock
        {
            Text = Lang.T("tip.onboarding"),
            FontSize = 13,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1F, 0x22, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 4, 10, 4),
            Child = _text,
        };
    }

    public void RefreshLanguage() => _text.Text = Lang.T("tip.onboarding");

    public void PlaceBelowAnchor(AnchorLine anchor)
    {
        Left = anchor.Left + (anchor.Width - Width) / 2;
        Top = anchor.Top + anchor.Height + 2;
    }

    public void Dismiss()
    {
        if (IsVisible)
        {
            Hide();
            Log.Write("G4 onboarding tip dismissed");
        }
    }
}

/// <summary>右下角非激活小提示：撤销入口、数据损坏等一次性消息。点一下或几秒后收起。</summary>
internal sealed class Toast : GhostWindow
{
    private const double MarginDip = 16;

    private readonly TextBlock _text;
    private readonly Border _shell;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(4200) };

    public Toast()
    {
        Width = 380;
        _text = new TextBlock
        {
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF6)),
            TextWrapping = TextWrapping.Wrap,
        };
        _shell = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x1F, 0x22, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            Child = _text,
        };
        Content = _shell;

        MouseLeftButtonUp += (_, _) => Dismiss();
        _timer.Tick += (_, _) => Dismiss();
    }

    protected override bool ClickThrough => false;

    public void ShowMessage(string message, int durationMs = 4200)
    {
        _text.Text = message;
        _shell.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Ceiling(_shell.DesiredSize.Height);
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - MarginDip;
        Top = area.Bottom - Height - MarginDip;
        if (!IsVisible)
        {
            Show();
        }

        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(durationMs);
        _timer.Start();
        Log.Write($"toast: {message}");
    }

    public void Dismiss()
    {
        _timer.Stop();
        if (IsVisible)
        {
            Hide();
        }
    }
}
