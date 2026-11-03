using System.Globalization;
using System.IO;

namespace MemoLine;

internal enum AppLanguage
{
    Zh,
    En,
}

/// <summary>
/// 用户可见文案的唯一来源。日志和命令行参数故意不进这张表——那是给开发者看的。
/// 选择持久化在 %LOCALAPPDATA%\MemoLine\language.txt；没有该文件时跟随系统 UI 语言。
/// </summary>
internal static class Lang
{
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["app.name"] = ["MemoLine · 备忘录晾衣绳", "MemoLine · notes on a line"],
        ["tray.tip.idle"] = ["MemoLine · 备忘录晾衣绳", "MemoLine · notes on a line"],
        ["tray.tip.open"] = ["MemoLine：已展开（点托盘图标收起）", "MemoLine: line is out (click the tray icon to hide it)"],
        ["tray.tip.closed"] = ["MemoLine：已收起，{0} 张便签在绳上", "MemoLine: hidden, {0} notes on the line"],
        ["tray.showHide"] = ["显示 / 收起绳子", "Show / hide the line"],
        ["tray.new"] = ["新建便签（Ctrl+Alt+N）", "New note (Ctrl+Alt+N)"],
        ["tray.undo"] = ["撤销删除（Ctrl+Alt+Z）", "Undo delete (Ctrl+Alt+Z)"],
        ["tray.anchor"] = ["显示下拉绳（顶边绳头）", "Show pull cord (top edge)"],
        ["tray.tips"] = ["使用小 tips", "Tips"],
        ["tray.langZh"] = ["中文", "中文"],
        ["tray.langEn"] = ["English", "English"],
        ["tray.quit"] = ["退出 MemoLine", "Quit MemoLine"],
        ["ctx.edit"] = ["编辑", "Edit"],
        ["ctx.remove"] = ["摘下来", "Unclip"],
        ["tip.onboarding"] = ["点一下绳头，或把鼠标顶到屏幕顶边中间", "Click the cord, or push the cursor to the middle of the top edge"],
        ["quad.urgent_important"] = ["紧急且重要", "Urgent & important"],
        ["quad.urgent_not_important"] = ["紧急不重要", "Urgent, not important"],
        ["quad.not_urgent_important"] = ["重要不紧急", "Important, not urgent"],
        ["quad.neither"] = ["不紧急不重要", "Neither"],
        ["quad.short.urgent_important"] = ["紧急重要", "Urgent+important"],
        ["quad.short.urgent_not_important"] = ["紧急不重要", "Urgent only"],
        ["quad.short.not_urgent_important"] = ["重要不紧急", "Important only"],
        ["quad.short.neither"] = ["不紧急不重要", "Neither"],
        ["add.slot"] = ["新建便签", "New note"],
        ["edit.save"] = ["保存", "Save"],
        ["edit.cancel"] = ["取消", "Cancel"],
        ["edit.hint"] = ["Ctrl+Enter 保存 · Esc 取消", "Ctrl+Enter / Esc"],
        ["edit.empty"] = ["先写点什么，再保存", "Write something first"],
        ["edit.counter"] = ["{0}/{1} 字", "{0}/{1} chars"],
        ["preview.created"] = ["创建 {0} · {1}", "Created {0} · {1}"],
        ["preview.hint"] = ["单击收起 · 双击编辑", "Click to close · double-click to edit"],
        ["toast.deleted"] = ["已摘下 · Ctrl+Alt+Z 撤销", "Unclipped · Ctrl+Alt+Z to undo"],
        ["toast.undone"] = ["已把便签挂回去", "Note restored to the line"],
        ["toast.limit"] = ["最多 {0} 张便签，先摘掉一些再新建", "{0} notes max — unclip a few before adding more"],
        ["toast.corrupt"] = ["便签数据损坏，已备份为 {0}", "Note data was damaged and backed up to {0}"],
        ["toast.readonly"] = ["数据文件无法备份，已进入只读模式（本次不会覆盖原文件）", "Could not back up the data file; running read-only this session"],
        ["toast.undoEmpty"] = ["没有可撤销的删除", "Nothing to undo"],
        ["toast.hotkeys"] = ["部分快捷键被其他程序占用，可用托盘菜单操作", "Some hotkeys are taken by another app — use the tray menu"],
        ["tips.title"] = ["使用小 tips", "How to use MemoLine"],
        ["tips.open"] = ["顶边中部悬停，或点下拉绳，展开绳子", "Hover the top edge center, or click the pull cord"],
        ["tips.preview"] = ["单击便签看全文，双击就地编辑", "Click a note for the full text, double-click to edit in place"],
        ["tips.edit"] = ["保存 Ctrl+Enter，取消 Esc；点别处也会保存", "Ctrl+Enter saves, Esc cancels; clicking away also saves"],
        ["tips.remove"] = ["右键便签 → 编辑 / 摘下来", "Right-click a note → Edit / Unclip"],
        ["tips.new"] = ["Ctrl+Alt+N 新建便签", "Ctrl+Alt+N creates a note"],
        ["tips.undo"] = ["Ctrl+Alt+Z 撤销最近一次删除", "Ctrl+Alt+Z undoes the latest unclip"],
        ["tips.scroll"] = ["滚轮沿绳翻看更早的便签", "Scroll along the line to reach older notes"],
        ["tips.hotkeys"] = ["Ctrl+Alt+T 开关绳子 · Ctrl+Alt+Shift+Q 退出", "Ctrl+Alt+T toggles the line · Ctrl+Alt+Shift+Q quits"],
    };

    public static AppLanguage Current { get; private set; } = Load();

    public static event Action? Changed;

    public static string T(string key) => Table.TryGetValue(key, out var values)
        ? values[(int)Current]
        : key;

    public static string Format(string key, params object[] args) => string.Format(T(key), args);

    public static void Preview(AppLanguage language)
    {
        Current = language;
        Log.Write($"language preview={language} (not persisted)");
    }

    public static void Set(AppLanguage language)
    {
        if (language == Current)
        {
            return;
        }

        Current = language;
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(AppPaths.LanguagePath, language.ToString());
        }
        catch (Exception error)
        {
            Log.Write($"language save failed: {error.GetType().Name}: {error.Message}");
        }

        Log.Write($"language switched to {language}");
        Changed?.Invoke();
    }

    /// <summary>自检用：中英条目数必须一一对应，避免某个 key 只有一边。</summary>
    public static bool TableIsComplete()
    {
        foreach (var (key, values) in Table)
        {
            if (values.Length != 2 || string.IsNullOrWhiteSpace(values[0]) || string.IsNullOrWhiteSpace(values[1]))
            {
                Log.Write($"lang: incomplete entry {key}");
                return false;
            }
        }

        return true;
    }

    private static AppLanguage Load()
    {
        try
        {
            if (Enum.TryParse<AppLanguage>(File.ReadAllText(AppPaths.LanguagePath).Trim(), out var saved))
            {
                Log.Write($"language={saved} (from {AppPaths.LanguagePath})");
                return saved;
            }
        }
        catch (Exception)
        {
        }

        var system = new CultureInfo(CultureInfo.CurrentUICulture.Name).TwoLetterISOLanguageName;
        var inferred = system.Equals("zh", StringComparison.OrdinalIgnoreCase) ? AppLanguage.Zh : AppLanguage.En;
        Log.Write($"language={inferred} (inferred from UI culture {system}; no setting file yet)");
        return inferred;
    }
}

internal static class AppPaths
{
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MemoLine");

    public static string NotesPath => Path.Combine(DataDirectory, "notes.json");

    public static string LanguagePath => Path.Combine(DataDirectory, "language.txt");

    public static string OnboardFlagPath => Path.Combine(DataDirectory, "onboarded.flag");
}

internal static class OnboardingState
{
    private static bool? _needsTip;

    /// <summary>自检时置 true：只改内存状态，不碰用户的首次引导标记。</summary>
    internal static bool SuppressWrites { get; set; }

    public static bool NeedsTip => _needsTip ??= !File.Exists(AppPaths.OnboardFlagPath);

    public static void MarkOnboarded()
    {
        _needsTip = false;
        if (SuppressWrites)
        {
            Log.Write("G4 onboarding flag write suppressed (selftest)");
            return;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(AppPaths.OnboardFlagPath, DateTime.Now.ToString("O"));
            Log.Write("G4 onboarded flag written");
        }
        catch (Exception error)
        {
            Log.Write($"G4 onboarding flag write failed: {error.Message}");
        }
    }
}
