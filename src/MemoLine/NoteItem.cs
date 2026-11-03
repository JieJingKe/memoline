using System.Globalization;

namespace MemoLine;

internal sealed class NoteItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;

    public Quadrant Quadrant { get; set; } = Quadrants.Default;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>尚未写进存储的新便签；保存时转正，取消时整卡移除。</summary>
    public bool IsDraft { get; set; }

    public string CreatedLabel
    {
        get
        {
            var local = CreatedAt.ToLocalTime();
            return local.Year == DateTimeOffset.Now.Year
                ? local.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
                : local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    public string CreatedFullLabel
    {
        get
        {
            var local = CreatedAt.ToLocalTime();
            return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
    }

    public string ShortPreview => Text.Length <= 40
        ? Text.Replace("\r", " ").Replace("\n", " ").Trim()
        : Text.Replace("\r", " ").Replace("\n", " ").Trim()[..40] + "…";

    public static NoteItem Draft() => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
        IsDraft = true,
    };

    public static string NormalizeText(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= Metrics.MaxTextLength ? trimmed : trimmed[..Metrics.MaxTextLength];
    }
}
