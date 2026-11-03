using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MemoLine;

/// <summary>绳尾常驻的空夹子：虚线纸影 + 夹子，点一下就在这个位置开始写新便签。</summary>
internal sealed class AddSlot : Grid
{
    private static readonly Brush OutlineIdle = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0x33, 0x22, 0x11)));
    private static readonly Brush OutlineHover = Freeze(new SolidColorBrush(Color.FromArgb(0xB8, 0x33, 0x22, 0x11)));
    private static readonly Brush LabelIdle = Freeze(new SolidColorBrush(Color.FromArgb(0x99, 0x33, 0x22, 0x11)));
    private static readonly Brush LabelHover = Freeze(new SolidColorBrush(Color.FromArgb(0xD8, 0x33, 0x22, 0x11)));

    private readonly TranslateTransform _shift = new();
    private readonly Rectangle _outline;
    private readonly TextBlock _label;

    public AddSlot()
    {
        Width = Metrics.CardWidth;
        Height = Metrics.CardHeight;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        RenderTransform = _shift;

        _outline = new Rectangle
        {
            RadiusX = 8,
            RadiusY = 8,
            Stroke = OutlineIdle,
            StrokeThickness = 1,
            StrokeDashArray = [3, 3],
            // 半透明牛皮纸底：白桌面上靠边线，深色桌面上靠纸色，两种背景都看得见。
            Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0xFD, 0xF7, 0xEA)),
        };

        var plus = new TextBlock
        {
            Text = "+",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 22,
            Foreground = LabelIdle,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 48, 0, 0),
        };
        _label = new TextBlock
        {
            Text = Lang.T("add.slot"),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11.5,
            Foreground = LabelIdle,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 74, 0, 0),
        };

        Children.Add(_outline);
        Children.Add(plus);
        Children.Add(_label);
        Children.Add(KraftPaper.BuildClothespin());

        MouseEnter += (_, _) =>
        {
            _outline.Stroke = OutlineHover;
            plus.Foreground = LabelHover;
            _label.Foreground = LabelHover;
        };
        MouseLeave += (_, _) =>
        {
            _outline.Stroke = OutlineIdle;
            plus.Foreground = LabelIdle;
            _label.Foreground = LabelIdle;
        };
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ClickRequested?.Invoke();
        };
    }

    public event Action? ClickRequested;

    public void RefreshLanguage() => _label.Text = Lang.T("add.slot");

    public void ShiftTo(double oldLeft, double newLeft, bool animate)
    {
        var offset = _shift.X;
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        var delta = oldLeft + offset - newLeft;
        if (!animate || Math.Abs(delta) < 0.5)
        {
            _shift.X = 0;
            return;
        }

        _shift.X = 0;
        _shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(Metrics.ShiftMs))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    internal double LayoutOffsetX => _shift.X;

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
