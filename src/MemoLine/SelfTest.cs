using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MemoLine;

/// <summary>
/// 自动自检：数据层（防抖/原子写/损坏备份/上限/宽松加载）+ 窗口层（展开不抢焦点、
/// 布局容量、全文面板、编辑保存、删除与撤销、托盘、下拉绳）。不能替代人工观察的部分
/// 只写日志，不判失败——例如 Windows 前台锁可能不允许后台进程把焦点拿回来。
/// </summary>
internal static class SelfTest
{
    private static readonly List<string> Passed = [];
    private static readonly List<string> Failed = [];

    public static int Run(string[] args)
    {
        Log.Reset();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var exitCode = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                exitCode = await RunAll();
            }
            catch (Exception error)
            {
                Fail("unhandled exception", error.ToString());
            }
            finally
            {
                Log.Write($"selftest RESULT {(Failed.Count == 0 ? "PASS" : "FAIL")} passed={Passed.Count} failed={Failed.Count}");
                foreach (var failure in Failed)
                {
                    Log.Write($"selftest FAIL {failure}");
                }

                exitCode = Failed.Count == 0 ? 0 : 1;
                app.Shutdown();
            }
        };

        app.Run();
        return exitCode;
    }

    private static async Task<int> RunAll()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "memoline-selftest",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(root);
        Log.Write($"selftest data dir {root}");

        var onboardPath = AppPaths.OnboardFlagPath;
        var onboardExisted = File.Exists(onboardPath);
        OnboardingState.SuppressWrites = true;
        try
        {
            await DataTests(Path.Combine(root, "data"));
            await WindowTests(Path.Combine(root, "window"));
        }
        finally
        {
            OnboardingState.SuppressWrites = false;
            if (!onboardExisted && File.Exists(onboardPath))
            {
                try
                {
                    File.Delete(onboardPath);
                    Log.Write("selftest removed its onboarding flag side effect");
                }
                catch (Exception error)
                {
                    Log.Write($"selftest could not remove onboarding flag: {error.Message}");
                }
            }
        }

        return 0;
    }

    private static async Task DataTests(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "notes.json");
        var store = new NotesStore(path);
        store.Load();
        Check(store.Count == 0, "data: fresh store is empty", path);
        Check(store.LoadError is null, "data: fresh load has no error");
        Check(store.CanAdd, "data: fresh store accepts notes");

        var older = Seed("先写的那条", Quadrant.UrgentImportant, -30);
        var newer = Seed("后写的那条", Quadrant.Neither, -10);
        store.Add(newer);
        store.Add(older);
        Check(store.Count == 2, "data: add keeps two notes");
        Check(ReferenceEquals(store.Notes[0], older) && ReferenceEquals(store.Notes[1], newer), "data: notes sorted oldest first");
        Check(!File.Exists(path), "data: 400ms debounce leaves no file immediately");

        store.Flush();
        Check(File.Exists(path), "data: flush writes notes.json", path);
        Check(!File.Exists(path + ".tmp"), "data: atomic write leaves no .tmp behind");

        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var json = document.RootElement;
            Check(json.GetProperty("schema").GetInt32() == 1, "data: schema is 1");
            var notes = json.GetProperty("notes");
            Check(notes.GetArrayLength() == 2, "data: json holds two notes");
            var first = notes[0];
            Check(first.GetProperty("text").GetString() == "先写的那条", "data: json keeps note text");
            Check(first.GetProperty("quadrant").GetString() == "urgent_important", "data: json keeps quadrant key");
            Check(Guid.TryParse(first.GetProperty("id").GetString(), out _), "data: json note id is a guid");
            Check(DateTimeOffset.TryParse(first.GetProperty("createdAt").GetString(), out _), "data: json createdAt round-trips");
        }

        Check(File.ReadAllText(path).Contains("先写的那条", StringComparison.Ordinal), "data: chinese text is not escaped");

        using (var reloaded = new NotesStore(path))
        {
            reloaded.Load();
            Check(reloaded.Count == 2, "data: reload restores two notes");
            Check(reloaded.Notes[0].Id == older.Id && reloaded.Notes[1].Id == newer.Id, "data: reload keeps ids and order");
            Check(reloaded.Notes[0].Quadrant == Quadrant.UrgentImportant, "data: reload keeps quadrant");
            var beforeUpdated = reloaded.Notes[0].UpdatedAt;
            reloaded.Notes[0].Text = "  改过的那条  ";
            reloaded.Touch(reloaded.Notes[0]);
            reloaded.Flush();
            Check(reloaded.Notes[0].Text == "改过的那条", "data: touch trims text");
            Check(reloaded.Notes[0].UpdatedAt >= beforeUpdated, "data: touch moves updatedAt forward");
        }

        using (var reloaded = new NotesStore(path))
        {
            reloaded.Load();
            Check(reloaded.Notes[0].Text == "改过的那条", "data: edited text persisted");
            Check(reloaded.Remove(reloaded.Notes[1]), "data: remove existing note");
            reloaded.Flush();
        }

        using (var reloaded = new NotesStore(path))
        {
            reloaded.Load();
            Check(reloaded.Count == 1 && reloaded.Notes[0].Text == "改过的那条", "data: removal persisted");
        }

        store.Dispose();

        var normalized = NoteItem.NormalizeText("  " + new string('x', Metrics.MaxTextLength + 120) + "  ");
        Check(normalized.Length == Metrics.MaxTextLength, "data: normalize caps at 2000 chars", normalized.Length.ToString());
        Check(NoteItem.NormalizeText("   ").Length == 0, "data: normalize trims whitespace");

        var limitPath = Path.Combine(root, "limit", "notes.json");
        Directory.CreateDirectory(Path.GetDirectoryName(limitPath)!);
        using (var limit = new NotesStore(limitPath))
        {
            limit.Load();
            for (var index = 0; index < Metrics.MaxNotes; index++)
            {
                limit.Add(Seed($"第 {index} 条", Quadrant.Neither, index));
            }

            Check(limit.Count == Metrics.MaxNotes, "data: store holds 200 notes");
            Check(!limit.CanAdd, "data: store reports full at 200");
            Check(!limit.Add(Seed("第 201 条", Quadrant.Neither, 999)), "data: 201st note rejected");
            limit.Flush();
        }

        Check(File.Exists(limitPath), "data: full store persisted");
        using (var document = JsonDocument.Parse(File.ReadAllText(limitPath)))
        {
            Check(document.RootElement.GetProperty("notes").GetArrayLength() == Metrics.MaxNotes, "data: json caps at 200 notes");
        }

        var corruptPath = Path.Combine(root, "corrupt", "notes.json");
        Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
        File.WriteAllText(corruptPath, "{ this is not json");
        using (var corrupt = new NotesStore(corruptPath))
        {
            corrupt.Load();
            Check(corrupt.Count == 0, "data: corrupt file loads as empty");
            Check(corrupt.LoadError is not null, "data: corrupt file records load error");
            Check(corrupt.CorruptBackupPath is not null && File.Exists(corrupt.CorruptBackupPath), "data: corrupt file is backed up");
            Check(!File.Exists(corruptPath), "data: corrupt original is moved away");
            corrupt.Add(Seed("损坏之后新建", Quadrant.NotUrgentImportant, 0));
            corrupt.Flush();
            Check(File.Exists(corruptPath), "data: store recovers after corruption");
        }

        var lenientPath = Path.Combine(root, "lenient", "notes.json");
        Directory.CreateDirectory(Path.GetDirectoryName(lenientPath)!);
        var longText = new string('长', Metrics.MaxTextLength + 50);
        var lenientJson = $$"""
{
  "schema": 99,
  "futureField": true,
  "notes": [
    { "id": "not-a-guid", "text": "  保留这条  ", "quadrant": "urgent_important", "createdAt": "2026-01-02T03:04:05+08:00", "updatedAt": "2026-01-02T03:04:05+08:00", "extra": 1 },
    { "id": "{{Guid.NewGuid()}}", "text": "   ", "quadrant": "neither", "createdAt": "2026-01-02T03:04:05+08:00", "updatedAt": "2026-01-02T03:04:05+08:00" },
    { "id": "{{Guid.NewGuid()}}", "text": "未知象限", "quadrant": "someday", "createdAt": "not-a-time", "updatedAt": "" },
    { "id": "{{Guid.NewGuid()}}", "text": "{{longText}}", "quadrant": "neither", "createdAt": "2026-01-02T03:04:05+08:00", "updatedAt": "2026-01-02T03:04:05+08:00" }
  ]
}
""";
        File.WriteAllText(lenientPath, lenientJson);
        using (var lenient = new NotesStore(lenientPath))
        {
            lenient.Load();
            Check(lenient.LoadError is null, "data: future schema loads leniently");
            Check(lenient.Count == 3, "data: empty note dropped, three kept");
            var kept = lenient.Notes.First(note => note.Text.Trim() == "保留这条");
            Check(kept.Id != Guid.Empty, "data: missing id replaced with a guid");
            var unknown = lenient.Notes.First(note => note.Text == "未知象限");
            Check(unknown.Quadrant == Quadrants.Default, "data: unknown quadrant falls back to default");
            var truncated = lenient.Notes.First(note => note.Text.Length == Metrics.MaxTextLength);
            Check(truncated.Text.Length == Metrics.MaxTextLength, "data: oversized note truncated to 2000");
            Check(lenient.Notes.All(note => note.Text.Trim().Length > 0), "data: no blank notes survive");
        }

        var debouncePath = Path.Combine(root, "debounce", "notes.json");
        Directory.CreateDirectory(Path.GetDirectoryName(debouncePath)!);
        using (var debounce = new NotesStore(debouncePath))
        {
            debounce.Load();
            debounce.Add(Seed("防抖测试", Quadrant.Neither, 0));
            await Task.Delay(180);
            Check(!File.Exists(debouncePath), "data: debounce still idle at 180ms");
            await Task.Delay(420);
            Check(File.Exists(debouncePath), "data: debounce wrote after ~600ms");
        }

        Check(Lang.TableIsComplete(), "lang: zh/en table is complete");
        Check(Lang.T("ctx.remove").Length > 0, "lang: context menu text resolves");
    }

    private static async Task WindowTests(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "notes.json");
        var store = new NotesStore(path);
        store.Load();

        var seeds = new[]
        {
            Seed("买猫砂、洗衣液，顺路取快递。", Quadrant.Neither, -80),
            Seed("周五之前把季度复盘发出去。", Quadrant.UrgentImportant, -70),
            Seed("约下周一的对齐会。", Quadrant.UrgentNotImportant, -60),
            Seed("给绿萝换水。", Quadrant.Neither, -50),
            Seed("读到一半：Thinking in Systems。", Quadrant.NotUrgentImportant, -40),
            Seed("把合同里需要法务确认的两处标出来。", Quadrant.UrgentImportant, -30),
            Seed("整理一下绳子的容量算法：屏幕装得下几张，绳子就长到几张。", Quadrant.NotUrgentImportant, -20),
            Seed(
                "周会要讲的三件事：容量与沿绳滚动已经跑通；编辑态只在双击时取焦点，保存后回到原来的窗口；数据落在本地 notes.json，400ms 防抖加原子替换，损坏文件先备份再进入只读。再补一句把文本拉长，让全文预览真的需要滚动。",
                Quadrant.UrgentImportant,
                -10),
        };
        foreach (var note in seeds)
        {
            store.Add(note);
        }

        store.Flush();

        var window = new LineWindow(store);
        Log.Write($"selftest window cards={window.Cards.Count} notes={store.Count}");
        window.Show();
        await Task.Delay(200);

        Check(window.WindowHandle != IntPtr.Zero, "window: hwnd created");
        if (window.HotkeysReady)
        {
            Check(true, "window: five hotkeys registered");
        }
        else
        {
            Info("window: hotkeys partially blocked by another app (Clipline holds Ctrl+Alt+T/L/Shift+Q on this machine)");
        }
        var style = (long)Native.GetWindowLongPtr(window.WindowHandle, Native.GWL_EXSTYLE);
        Check((style & Native.WS_EX_NOACTIVATE) != 0, "window: WS_EX_NOACTIVATE set");
        Check((style & Native.WS_EX_TOOLWINDOW) != 0, "window: WS_EX_TOOLWINDOW set");
        Check(!window.ShowInTaskbar, "window: hidden from taskbar");

        var beforeReveal = Native.GetForegroundWindow();
        window.Reveal(manual: true);
        await Task.Delay(500);
        var afterReveal = Native.GetForegroundWindow();
        Log.Write($"selftest focus reveal before={Native.DescribeWindow(beforeReveal)} after={Native.DescribeWindow(afterReveal)} unchanged={beforeReveal == afterReveal}");
        Check(window.IsOpen, "window: reveal opens the line");
        Check(beforeReveal == afterReveal, "window: reveal keeps foreground unchanged");

        var monitor = window.CurrentMonitor;
        var bounds = window.OpenedBoundsPhysical;
        Log.Write($"selftest bounds={bounds} monitor={monitor} scale={window.Scale:F2} width={window.Width:F0} visible={window.VisibleCount}");
        Check(bounds.Top == monitor.rcMonitor.Top, "window: line sits on monitor top");
        Check(bounds.Left >= monitor.rcWork.Left && bounds.Right <= monitor.rcWork.Right, "window: line stays inside work area");

        var capacity = Math.Max(
            1,
            (int)((Zone.WorkWidthDip(monitor, window.Scale) - 24 - Metrics.SidePadding * 2 + Metrics.Gap) / Metrics.Step));
        var expectedVisible = Math.Max(1, capacity - 1);
        Check(window.VisibleCount == expectedVisible, "window: capacity matches work area", $"{window.VisibleCount}/{expectedVisible}");
        Check(window.Cards.Count == seeds.Length, "window: every note has a card", window.Cards.Count.ToString());

        var clickProbe = window.Cards[0];
        var probePoint = new Native.POINT { X = 500, Y = 300 };
        Check(!clickProbe.RegisterClickForTest(1000, probePoint), "card: first click is treated as single");
        Check(
            clickProbe.RegisterClickForTest(1040, new Native.POINT { X = 502, Y = 301 }),
            "card: quick nearby second click is double");
        Check(!clickProbe.RegisterClickForTest(3000, probePoint), "card: click after double resets to single");
        Check(
            !clickProbe.RegisterClickForTest(3040, new Native.POINT { X = 700, Y = 300 }),
            "card: distant second click is not double");

        var newest = window.Cards[^1];
        Check(Math.Abs(window.AddSlotLeft - (Canvas.GetLeft(newest) + Metrics.Step)) < 0.5, "window: add slot follows the newest note");
        Check(window.AddSlotLeft >= 0 && window.AddSlotLeft + Metrics.CardWidth <= window.Width + 0.5, "window: add slot is on screen", $"{window.AddSlotLeft:F0}+{Metrics.CardWidth}≤{window.Width:F0}");

        if (window.Cards.Count > window.VisibleCount)
        {
            var beforeScroll = window.ScrollOffset;
            window.ScrollForTest(240);
            await Task.Delay(80);
            Check(window.ScrollOffset > beforeScroll, "window: wheel scrolls along the line", $"{beforeScroll:F0}→{window.ScrollOffset:F0}");
            Check(
                Math.Abs(Canvas.GetLeft(newest) - (window.AddSlotLeft - Metrics.Step + window.ScrollOffset)) < 0.5,
                "window: newest note follows scroll");
        }
        else
        {
            Info("window: all cards fit, scroll branch skipped");
        }

        window.TogglePreviewForTest(newest);
        await Task.Delay(120);
        Check(window.IsPreviewOpen, "window: single click opens preview");
        Check(window.Height > Metrics.BaseWindowHeight, "window: preview grows the window", window.Height.ToString("F0"));
        window.TogglePreviewForTest(newest);
        await Task.Delay(80);
        Check(!window.IsPreviewOpen, "window: second click closes preview");
        Check(Math.Abs(window.Height - Metrics.BaseWindowHeight) < 0.5, "window: closing preview restores height");

        var beforeEditFocus = Native.GetForegroundWindow();
        window.StartNewNote();
        await Task.Delay(900);
        Check(window.IsEditing, "window: new note enters edit mode");
        Check(window.DraftCard is not null, "window: draft card exists");
        var draftId = window.DraftCard?.Note.Id;
        var cardsBeforeRepeat = window.Cards.Count;
        window.StartNewNote();
        window.StartNewNote();
        await Task.Delay(250);
        Check(window.IsEditing, "window: repeated new keeps editing");
        Check(window.DraftCard?.Note.Id == draftId, "window: repeated new reuses the blank draft");
        Check(window.Cards.Count == cardsBeforeRepeat, "window: repeated new does not rebuild cards", window.Cards.Count.ToString());
        Check(store.Count == seeds.Length, "window: repeated new does not touch the store", store.Count.ToString());
        var keyboardInside = window.DraftCard?.IsKeyboardFocusWithin == true;
        Log.Write($"selftest edit focus keyboardInside={keyboardInside} windowActive={window.IsActive}");
        Info($"window: editor keyboard focus={keyboardInside} (foreground lock may block activation)");

        window.Retract();
        await Task.Delay(300);
        Check(window.IsEditing && window.IsOpen, "window: retract is ignored while editing");

        if (window.DraftCard is { } draft)
        {
            draft.SetEditorTextForTest("  自动保存的便签  ");
            draft.SetQuadrantForTest(Quadrant.UrgentImportant);
            window.CommitCurrentEdit(restoreFocus: true);
            await Task.Delay(250);
            var afterEditFocus = Native.GetForegroundWindow();
            Log.Write($"selftest focus edit before={Native.DescribeWindow(beforeEditFocus)} after={Native.DescribeWindow(afterEditFocus)}");
            Check(afterEditFocus == beforeEditFocus, "window: save returns focus to the previous window");
            Check(!window.IsEditing, "window: commit leaves edit mode");
            Check(store.Count == seeds.Length + 1, "window: committed draft enters the store", store.Count.ToString());
            Check(window.Cards.Count == seeds.Length + 1, "window: committed draft keeps its card", window.Cards.Count.ToString());
            var saved = window.Cards[^1].Note;
            Check(saved.Text == "自动保存的便签", "window: committed text is trimmed");
            Check(saved.Quadrant == Quadrant.UrgentImportant, "window: committed quadrant persists");
        }

        var styleAfterEdit = (long)Native.GetWindowLongPtr(window.WindowHandle, Native.GWL_EXSTYLE);
        Check((styleAfterEdit & Native.WS_EX_NOACTIVATE) != 0, "window: NOACTIVATE restored after edit");

        store.SaveNow();
        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var notes = document.RootElement.GetProperty("notes");
            var found = notes.EnumerateArray().Any(note => note.GetProperty("text").GetString() == "自动保存的便签"
                && note.GetProperty("quadrant").GetString() == "urgent_important");
            Check(found, "window: committed draft persisted to json");
        }

        var victim = window.Cards[0];
        var victimId = victim.Note.Id;
        window.DeleteNote(victim);
        await Task.Delay(700);
        Check(store.Count == seeds.Length, "window: delete removes from store", store.Count.ToString());
        Check(window.Cards.Count == seeds.Length, "window: delete removes the card", window.Cards.Count.ToString());
        Check(window.CanUndo, "window: delete enables undo");

        window.UndoDelete();
        await Task.Delay(400);
        Check(store.Count == seeds.Length + 1, "window: undo restores the note", store.Count.ToString());
        Check(window.Cards.Count == seeds.Length + 1, "window: undo restores the card", window.Cards.Count.ToString());
        Check(!window.CanUndo, "window: undo stack drains");
        Check(store.Notes.Any(note => note.Id == victimId), "window: restored note keeps its id");

        window.Retract();
        await Task.Delay(350);
        var retractedBottom = window.Top + window.Height;
        Check(!window.IsOpen, "window: retract closes the line");
        Check(retractedBottom <= monitor.rcMonitor.Top / window.Scale + 0.5, "window: retracted window is off-screen", $"{retractedBottom:F0}");

        var anchor = new AnchorLine();
        anchor.PlaceOnPrimaryMonitor(window);
        anchor.Show();
        await Task.Delay(120);
        Check(anchor.IsVisible, "anchor: pull cord shows");
        var anchorLeft = anchor.Left * window.Scale;
        Check(
            anchorLeft >= monitor.rcWork.Left
            && anchorLeft + AnchorLine.CordWidthDip * window.Scale <= monitor.rcWork.Right,
            "anchor: pull cord inside work area",
            $"{anchorLeft:F0}");
        var anchorHandle = new WindowInteropHelper(anchor).Handle;
        var anchorStyle = (long)Native.GetWindowLongPtr(anchorHandle, Native.GWL_EXSTYLE);
        Check((anchorStyle & Native.WS_EX_NOACTIVATE) != 0, "anchor: does not activate");
        Check((anchorStyle & Native.WS_EX_TOOLWINDOW) != 0, "anchor: tool window");
        Check((anchorStyle & Native.WS_EX_TRANSPARENT) == 0, "anchor: remains clickable");
        anchor.Hide();

        using (var tray = new TrayIcon(window.WindowHandle))
        {
            await Task.Delay(120);
            Check(tray.IsAdded, "tray: icon added");
            tray.SetTip("MemoLine selftest");
        }

        var tips = new TipsPanel();
        tips.Toggle();
        await Task.Delay(100);
        Check(tips.IsVisible, "tips: panel opens");
        tips.Toggle();
        await Task.Delay(60);
        Check(!tips.IsVisible, "tips: second toggle closes");

        window.Close();
        store.Dispose();
    }

    private static NoteItem Seed(string text, Quadrant quadrant, int minutesFromNow)
    {
        var created = DateTimeOffset.Now.AddMinutes(minutesFromNow);
        return new NoteItem
        {
            Text = text,
            Quadrant = quadrant,
            CreatedAt = created,
            UpdatedAt = created,
        };
    }

    private static void Check(bool condition, string name, string? detail = null)
    {
        if (condition)
        {
            Passed.Add(name);
            Log.Write($"selftest PASS {name}{(string.IsNullOrEmpty(detail) ? string.Empty : " · " + detail)}");
        }
        else
        {
            Failed.Add(string.IsNullOrEmpty(detail) ? name : $"{name} · {detail}");
            Log.Write($"selftest FAIL {name}{(string.IsNullOrEmpty(detail) ? string.Empty : " · " + detail)}");
        }
    }

    private static void Fail(string name, string detail) => Check(false, name, detail);

    private static void Info(string message) => Log.Write($"selftest INFO {message}");
}
