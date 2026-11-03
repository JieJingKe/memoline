using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace MemoLine;

/// <summary>
/// 牛皮纸配色、共享阴影位图、金属夹几何。卡片本体与离屏预览共用同一套画刷，避免两处走样。
/// </summary>
internal static class KraftPaper
{
    /// <summary>用户提供的晾衣夹素材（reference/clothespin.svg，viewBox 0 0 1024）。</summary>
    private const string ClothespinData =
        "F0 M604.16 600.746667H417.564444V349.297778c0-26.168889 21.617778-47.786667 47.786667-47.786667H557.511111c26.168889 0 47.786667 21.617778 47.786667 47.786667v251.448889z m-169.528889-17.066667h152.462222V349.297778c0-17.066667-13.653333-30.72-30.72-30.72h-92.16c-17.066667 0-30.72 13.653333-30.72 30.72v234.382222z " +
        "M827.164444 723.626667H193.422222c-18.204444 0-32.995556-14.791111-32.995555-32.995556v-20.48c0-13.653333 9.102222-25.031111 21.617777-28.444444l242.346667-58.026667h172.942222l232.106667 55.751111c18.204444 4.551111 31.857778 21.617778 31.857778 39.822222v10.24c0 19.342222-14.791111 34.133333-34.133334 34.133334zM426.666667 600.746667l-241.208889 58.026666c-5.688889 1.137778-9.102222 5.688889-9.102222 11.377778v20.48c0 9.102222 6.826667 15.928889 15.928888 15.928889h634.88c9.102222 0 17.066667-7.964444 17.066667-17.066667v-10.24c0-11.377778-7.964444-21.617778-19.342222-23.893333l-230.968889-55.751111H426.666667zM608.711111 457.386667h-13.653333v-17.066667h13.653333c3.413333 0 6.826667-2.275556 6.826667-5.688889 0-2.275556-3.413333-5.688889-6.826667-5.688889h-13.653333v-17.066666h13.653333c12.515556 0 22.755556 10.24 22.755556 21.617777s-10.24 23.893333-22.755556 23.893334zM425.528889 457.386667h-10.24c-12.515556 0-22.755556-10.24-22.755556-21.617778s10.24-21.617778 22.755556-21.617778h10.24v17.066667h-10.24c-3.413333 0-6.826667 2.275556-6.826667 5.688889 0 2.275556 3.413333 5.688889 6.826667 5.688889h10.24v14.791111z";

    public static readonly Geometry ClothespinGeometry = Geometry.Parse(ClothespinData);

    public static readonly Brush Background = CreateBackground();
    public static readonly Brush Edge = Freeze(new SolidColorBrush(Color.FromArgb(0xB4, 0xC9, 0xAE, 0x86)));
    public static readonly Brush EdgeHover = Freeze(new SolidColorBrush(Color.FromArgb(0xD8, 0xAF, 0x8F, 0x62)));
    public static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x22, 0x11)));

    private static readonly BitmapSource ShadowBitmap = CreateShadowBitmap();

    public static UIElement BuildShadow()
    {
        var shadow = new Image
        {
            Source = ShadowBitmap,
            Width = Metrics.CardWidth + Metrics.ShadowPadding * 2,
            Height = Metrics.CardHeight + Metrics.ShadowPadding * 2,
            Margin = new Thickness(-Metrics.ShadowPadding),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(shadow, BitmapScalingMode.HighQuality);
        return shadow;
    }

    public static Border BuildPaper(UIElement content) => new()
    {
        Width = Metrics.CardWidth,
        Height = Metrics.CardHeight,
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        BorderBrush = Edge,
        Background = Background,
        Padding = new Thickness(8),
        Child = content,
    };

    public static UIElement BuildClothespin() => new System.Windows.Shapes.Path
    {
        Data = ClothespinGeometry,
        Stretch = Stretch.Uniform,
        Width = Metrics.PinWidth,
        Height = Metrics.PinHeight,
        Fill = Metal,
        Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0x6E, 0x76, 0x7E)),
        StrokeThickness = 0.8,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, -Metrics.PinHeight * 0.75, 0, 0),
        IsHitTestVisible = false,
    };

    private static readonly Brush Metal = BuildMetal();

    private static Brush BuildMetal()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.35, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF8, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF8, 0xF2, 0xF5, 0xF8), 0.18));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xD6, 0xDD, 0xE4), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xB9, 0xC2, 0xCC), 0.72));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xED, 0xF1, 0xF5), 1));
        brush.Freeze();
        return brush;
    }

    private static BitmapSource CreateShadowBitmap()
    {
        const double scale = 1.0;
        var width = Metrics.CardWidth + Metrics.ShadowPadding * 2;
        var height = Metrics.CardHeight + Metrics.ShadowPadding * 2;

        var silhouette = new Border
        {
            Width = Metrics.CardWidth,
            Height = Metrics.CardHeight,
            CornerRadius = new CornerRadius(8),
            Background = Background,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Metrics.ShadowPadding),
            Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 2,
                Opacity = 0.28,
                Color = Color.FromRgb(0x4A, 0x3B, 0x2A),
            },
        };

        var host = new Grid { Width = width, Height = height };
        host.Children.Add(silhouette);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(host);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush CreateBackground()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.25, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xFA, 0xFD, 0xF7, 0xEA), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xFA, 0xF8, 0xEF, 0xDA), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF7, 0xF1, 0xE2, 0xC4), 1));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
