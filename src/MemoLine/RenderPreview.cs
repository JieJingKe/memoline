using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapesPath = System.Windows.Shapes.Path;
using ShapesRectangle = System.Windows.Shapes.Rectangle;

namespace MemoLine;

/// <summary>
/// 离屏出图：一次画三态（收起/展开+全文/编辑），背景由白到深灰，
/// 同一张图能看清便签在浅色和深色桌面上的对比度。改观感先看图，再找人确认。
/// </summary>
internal static class RenderPreview
{
    public static int Run(string? outputPath)
    {
        _ = new Application();
        var target = outputPath ?? Path.Combine(Path.GetTempPath(), "memoline-preview.png");

        const double sceneWidth = 1000;
        const double sceneHeight = 720;

        var background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        background.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0));
        background.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0xF3, 0xF5), 0.35));
        background.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x3E, 0x44), 0.7));
        background.GradientStops.Add(new GradientStop(Color.FromRgb(0x1F, 0x22, 0x26), 1));
        background.Freeze();

        var canvas = new Canvas { Background = background, Width = sceneWidth, Height = sceneHeight };

        void Place(UIElement element, double left, double top)
        {
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            canvas.Children.Add(element);
        }

        void AddLine(double lineY)
        {
            Place(new ShapesRectangle
            {
                Width = sceneWidth - Metrics.SidePadding * 2 + 16,
                Height = 3,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
            }, Metrics.SidePadding - 8, lineY - 6);
        }

        // ① 收起态：白线 + 工作区 2/3 处的下拉绳
        var collapsedLineY = 40.0;
        AddLine(collapsedLineY);
        var lineLeft = Metrics.SidePadding - 8;
        var lineWidth = sceneWidth - Metrics.SidePadding * 2 + 16;
        var cordCenter = lineLeft + lineWidth * AnchorLine.AnchorRatio;
        Place(new ShapesPath
        {
            Data = AnchorLine.CordGeometry,
            Stretch = Stretch.Uniform,
            Width = AnchorLine.CordWidthDip,
            Height = AnchorLine.CordHeightDip,
            Fill = new SolidColorBrush(Color.FromArgb(0xF0, 0xC9, 0x9C, 0x66)),
            Stroke = new SolidColorBrush(Color.FromArgb(0xF0, 0x5A, 0x3C, 0x1E)),
            StrokeThickness = 1.1,
        }, cordCenter - AnchorLine.CordWidthDip / 2, collapsedLineY - 12);

        // ② 展开态：三张便签 + 绳尾空夹子 + 全文预览面板（最新的一张在右）
        var expandedLineY = 170.0;
        AddLine(expandedLineY);

        var chinese = Lang.Current == AppLanguage.Zh;
        var notes = chinese
            ? new[]
            {
                NewNote("周五之前把季度复盘发出去，顺便约一下下周一的对齐会。", Quadrant.UrgentImportant, 1),
                NewNote("读完《系统之美》第 4 章：库存和延迟。", Quadrant.NotUrgentImportant, 2),
                NewNote("周六采购：猫砂、洗衣液；取快递；给绿萝换水。", Quadrant.Neither, 3),
            }
            : new[]
            {
                NewNote("Send the quarterly review by Friday, and book Monday's alignment meeting.", Quadrant.UrgentImportant, 1),
                NewNote("Finish Chapter 4 of Thinking in Systems: stocks and delays.", Quadrant.NotUrgentImportant, 2),
                NewNote("Saturday errands: cat litter, detergent, parcel, water the pothos.", Quadrant.Neither, 3),
            };

        var startX = (sceneWidth - (4 * Metrics.CardWidth + 3 * Metrics.Gap)) / 2;
        for (var index = 0; index < notes.Length; index++)
        {
            Place(new CardView(notes[index]), startX + index * Metrics.Step, expandedLineY);
        }

        var addSlot = new AddSlot();
        Place(addSlot, startX + notes.Length * Metrics.Step, expandedLineY);

        var preview = LineWindow.BuildPreviewPanel(
            NewNote(
                chinese
                    ? "周六出门采购：\n1. 猫砂两袋、洗衣液一瓶\n2. 顺路取快递（驿站 18:00 前）\n3. 给绿萝换水，把薄荷搬回室内\n4. 晚上给妈妈打电话"
                    : "Saturday errands:\n1. Two bags of cat litter and a bottle of detergent\n2. Pick up the parcel before 6 pm\n3. Water the pothos and move the mint indoors\n4. Call Mom in the evening",
                Quadrant.Neither,
                4,
                notes[2].CreatedAt),
            out _);
        preview.Measure(new Size(preview.Width, double.PositiveInfinity));
        preview.Height = Math.Min(Math.Ceiling(preview.DesiredSize.Height), 400);
        Place(preview, startX + 2 * Metrics.Step, expandedLineY + Metrics.CardHeight + 10);

        // ③ 编辑态：双击就地编辑，四个象限圆点 + 保存/取消
        var editLineY = 520.0;
        AddLine(editLineY);
        var editing = new CardView(NewNote(
            chinese
                ? "周三前把合同条款过一遍，标出需要法务确认的两处。"
                : "Review the contract clauses before Wednesday.",
            Quadrant.UrgentNotImportant,
            5));
        editing.BeginEdit();
        editing.SetQuadrantForTest(Quadrant.UrgentNotImportant);
        Place(editing, startX, editLineY);

        canvas.Measure(new Size(sceneWidth, sceneHeight));
        canvas.Arrange(new Rect(0, 0, sceneWidth, sceneHeight));
        canvas.UpdateLayout();

        Save(canvas, target, sceneWidth, sceneHeight);
        Log.Write($"render-preview → {target}");
        return 0;
    }

    /// <summary>托盘"使用小 tips"面板：垫亮→暗渐变，一眼看清两种桌面上的可读性。</summary>
    public static int RunTips(string? outputPath)
    {
        _ = new Application();
        var target = outputPath ?? Path.Combine(Path.GetTempPath(), "memoline-tips.png");

        var panel = new TipsPanel();
        var content = (FrameworkElement)panel.Content;
        panel.Content = null;
        content.Width = panel.Width;
        content.Height = panel.Height;
        content.HorizontalAlignment = HorizontalAlignment.Right;
        content.VerticalAlignment = VerticalAlignment.Bottom;
        content.Margin = new Thickness(0, 0, 16, 16);

        const double sceneWidth = 520;
        const double sceneHeight = 380;
        var background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        background.GradientStops.Add(new GradientStop(Color.FromRgb(0x8A, 0xC7, 0xE8), 0));
        background.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0xF3, 0xF5), 0.45));
        background.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x2D, 0x31), 1));
        background.Freeze();

        var scene = new Grid
        {
            Width = sceneWidth,
            Height = sceneHeight,
            Background = background,
            Children = { content },
        };
        scene.Measure(new Size(sceneWidth, sceneHeight));
        scene.Arrange(new Rect(0, 0, sceneWidth, sceneHeight));
        scene.UpdateLayout();

        Save(scene, target, sceneWidth, sceneHeight);
        Log.Write($"render-tips → {target}");
        return 0;
    }

    private static NoteItem NewNote(string text, Quadrant quadrant, int minutesAgo, DateTimeOffset? createdAt = null) => new()
    {
        Text = text,
        Quadrant = quadrant,
        CreatedAt = createdAt ?? DateTimeOffset.Now.AddMinutes(-minutesAgo * 37),
        UpdatedAt = DateTimeOffset.Now.AddMinutes(-minutesAgo * 37),
    };

    private static void Save(Visual visual, string target, double width, double height)
    {
        const double scale = 2.0;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(target);
        encoder.Save(stream);
    }
}
