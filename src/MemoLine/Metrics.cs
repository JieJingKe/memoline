namespace MemoLine;

/// <summary>所有绳/卡几何的唯一来源，避免卡片、布局、出图预览三处各写一份。</summary>
internal static class Metrics
{
    public const double CardWidth = 200;
    public const double CardHeight = 132;
    public const double Gap = 20;
    public const double Step = CardWidth + Gap;
    public const double SidePadding = 32;
    public const double LineY = 18;

    public const double PinWidth = 34;
    public const double PinHeight = 21.5;

    /// <summary>阴影位图四周留白（DIP）：模糊 14 + 偏移 2 之外再留余量。</summary>
    public const double ShadowPadding = 20;

    public const double BaseWindowHeight = LineY + CardHeight + 30;

    public const int MaxNotes = 200;
    public const int MaxTextLength = 2000;
}
