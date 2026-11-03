using System.Windows.Media;

namespace MemoLine;

internal enum Quadrant
{
    UrgentImportant = 0,
    UrgentNotImportant = 1,
    NotUrgentImportant = 2,
    Neither = 3,
}

internal static class Quadrants
{
    public const Quadrant Default = Quadrant.NotUrgentImportant;

    public static readonly Quadrant[] All =
    [
        Quadrant.UrgentImportant,
        Quadrant.UrgentNotImportant,
        Quadrant.NotUrgentImportant,
        Quadrant.Neither,
    ];

    public static string Key(Quadrant quadrant) => quadrant switch
    {
        Quadrant.UrgentImportant => "urgent_important",
        Quadrant.UrgentNotImportant => "urgent_not_important",
        Quadrant.NotUrgentImportant => "not_urgent_important",
        _ => "neither",
    };

    public static bool TryParse(string? value, out Quadrant quadrant)
    {
        quadrant = Default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var candidate in All)
        {
            if (string.Equals(Key(candidate), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                quadrant = candidate;
                return true;
            }
        }

        return false;
    }

    public static Color Color(Quadrant quadrant) => quadrant switch
    {
        Quadrant.UrgentImportant => System.Windows.Media.Color.FromRgb(0xD6, 0x45, 0x45),
        Quadrant.UrgentNotImportant => System.Windows.Media.Color.FromRgb(0xD9, 0x8E, 0x04),
        Quadrant.NotUrgentImportant => System.Windows.Media.Color.FromRgb(0x2F, 0x85, 0x5A),
        _ => System.Windows.Media.Color.FromRgb(0x8A, 0x8F, 0x98),
    };

    public static Color RingColor(Quadrant quadrant)
    {
        var color = Color(quadrant);
        return System.Windows.Media.Color.FromRgb(
            (byte)(color.R * 0.72),
            (byte)(color.G * 0.72),
            (byte)(color.B * 0.72));
    }

    public static Brush Brush(Quadrant quadrant) => BrushCache[quadrant];

    public static Brush RingBrush(Quadrant quadrant) => RingCache[quadrant];

    public static string Label(Quadrant quadrant) => Lang.T($"quad.{Key(quadrant)}");

    public static string ShortLabel(Quadrant quadrant) => Lang.T($"quad.short.{Key(quadrant)}");

    private static readonly Dictionary<Quadrant, Brush> BrushCache = Frozen(useRing: false);
    private static readonly Dictionary<Quadrant, Brush> RingCache = Frozen(useRing: true);

    private static Dictionary<Quadrant, Brush> Frozen(bool useRing)
    {
        var map = new Dictionary<Quadrant, Brush>();
        foreach (var quadrant in All)
        {
            var brush = new SolidColorBrush(useRing ? RingColor(quadrant) : Color(quadrant));
            brush.Freeze();
            map[quadrant] = brush;
        }

        return map;
    }
}
