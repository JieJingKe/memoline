using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MemoLine;

/// <summary>托盘"使用小 tips"面板：贴右下角、非激活、点一下或 12 秒后收起。</summary>
internal sealed class TipsPanel : GhostWindow
{
    private const double MarginDip = 16;
    private const int AutoHideMs = 12000;

    private static readonly string[] TipKeys =
    [
        "tips.open",
        "tips.preview",
        "tips.edit",
        "tips.remove",
        "tips.new",
        "tips.undo",
        "tips.scroll",
        "tips.hotkeys",
    ];

    private readonly TextBlock _title;
    private readonly StackPanel _lines = new();
    private readonly Border _shell;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(AutoHideMs) };

    public TipsPanel()
    {
        Width = 380;
        _title = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };

        var stack = new StackPanel();
        stack.Children.Add(_title);
        stack.Children.Add(_lines);
        _shell = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1F, 0x22, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12, 16, 12),
            Child = stack,
        };
        Content = _shell;

        MouseLeftButtonUp += (_, _) =>
        {
            Log.Write("tips dismissed by click");
            HideTips();
        };
        _hideTimer.Tick += (_, _) =>
        {
            Log.Write("tips auto-hidden");
            HideTips();
        };

        RebuildText();
    }

    protected override bool ClickThrough => false;

    public void Toggle()
    {
        if (IsVisible)
        {
            HideTips();
            return;
        }

        PlaceAboveTray();
        Show();
        _hideTimer.Stop();
        _hideTimer.Start();
        Log.Write($"tips shown at ({Left:F0},{Top:F0},{Width:F0}x{Height:F0})");
    }

    public void RefreshLanguage() => RebuildText();

    private void HideTips()
    {
        _hideTimer.Stop();
        if (IsVisible)
        {
            Hide();
        }
    }

    private void PlaceAboveTray()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - MarginDip;
        Top = area.Bottom - Height - MarginDip;
    }

    private void RebuildText()
    {
        _title.Text = Lang.T("tips.title");
        _lines.Children.Clear();
        foreach (var key in TipKeys)
        {
            _lines.Children.Add(new TextBlock
            {
                Text = "· " + Lang.T(key),
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xEF)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        FitHeight();
    }

    private void FitHeight()
    {
        _shell.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Ceiling(_shell.DesiredSize.Height);
        if (IsVisible)
        {
            PlaceAboveTray();
        }
    }
}
