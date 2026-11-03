using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MemoLine;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (GetValue(args, "--lang") is { } lang && Enum.TryParse<AppLanguage>(lang, true, out var previewLanguage))
        {
            Lang.Preview(previewLanguage);
        }

        if (args.Contains("--render-preview"))
        {
            return RenderPreview.Run(GetValue(args, "--render-preview"));
        }

        if (args.Contains("--render-tips"))
        {
            return RenderPreview.RunTips(GetValue(args, "--render-tips"));
        }

        if (args.Contains("--selftest"))
        {
            return SelfTest.Run(args);
        }

        using var mutex = new Mutex(true, @"Local\MemoLine", out var isFirst);
        if (!isFirst)
        {
            Log.Write("another MemoLine instance is running; exit");
            return 1;
        }

        Log.Reset();
        var notesPath = GetValue(args, "--data") ?? AppPaths.NotesPath;
        var store = new NotesStore(notesPath);
        store.Load();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        app.DispatcherUnhandledException += (_, unhandled) =>
        {
            Log.Write($"FATAL ui exception {unhandled.Exception}");
            unhandled.Handled = true;
            app.Shutdown();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, unhandled) =>
            Log.Write($"FATAL background exception {unhandled.ExceptionObject}");

        var window = new LineWindow(store);
        var zone = new HotZone(window);
        var anchor = new AnchorLine();
        var tip = new OnboardingTip();
        var tips = new TipsPanel();
        var anchorEnabled = !args.Contains("--no-anchor");

        window.Show();
        var tray = new TrayIcon(window.WindowHandle) { AnchorEnabled = anchorEnabled, UndoAvailable = window.CanUndo };

        if (!window.HotkeysReady)
        {
            var hotkeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            hotkeyTimer.Tick += (_, _) =>
            {
                hotkeyTimer.Stop();
                window.Toast.ShowMessage(Lang.T("toast.hotkeys"), 9000);
            };
            hotkeyTimer.Start();
        }

        void UpdateTray()
        {
            tray.UndoAvailable = window.CanUndo;
            tray.SetTip(window.IsOpen
                ? Lang.T("tray.tip.open")
                : Lang.Format("tray.tip.closed", window.NoteCount));
        }

        void Quit()
        {
            window.FinishEditingForExit();
            store.Flush();
            window.Close();
            app.Shutdown();
        }

        void ShowTip()
        {
            tip.Show();
            tip.PlaceBelowAnchor(anchor);
            Log.Write($"G4 onboarding tip shown at ({tip.Left:F0},{tip.Top:F0})");
        }

        tray.ToggleRequested += window.ToggleManually;
        tray.NewRequested += window.StartNewNote;
        tray.UndoRequested += window.UndoDelete;
        tray.AnchorToggleRequested += () => window.ToggleAnchor();
        tray.TipsRequested += tips.Toggle;
        tray.QuitRequested += Quit;
        window.QuitRequested += Quit;

        window.ContentChanged += UpdateTray;

        window.OpenChanged += open =>
        {
            if (open)
            {
                tip.Dismiss();
            }

            anchor.SetVisible(anchorEnabled && !open);
            UpdateTray();
        };

        window.AnchorToggleRequested += () =>
        {
            anchorEnabled = !anchorEnabled;
            tray.AnchorEnabled = anchorEnabled;
            anchor.PlaceOnPrimaryMonitor(window);
            anchor.SetVisible(anchorEnabled && !window.IsOpen);
            Log.Write($"G6 pull cord visible={anchorEnabled}");
            if (anchorEnabled && OnboardingState.NeedsTip)
            {
                ShowTip();
            }
            else
            {
                tip.Dismiss();
            }
        };

        Lang.Changed += () =>
        {
            window.RefreshLanguage();
            tip.RefreshLanguage();
            tips.RefreshLanguage();
            if (tip.IsVisible)
            {
                tip.PlaceBelowAnchor(anchor);
            }

            UpdateTray();
        };

        anchor.PullRequested += () =>
        {
            Log.Write("G4 cord pulled (hover/click)");
            window.Reveal(manual: true);
        };

        anchor.PlaceOnPrimaryMonitor(window);
        anchor.SetVisible(anchorEnabled);
        if (anchorEnabled && OnboardingState.NeedsTip)
        {
            ShowTip();
        }

        UpdateTray();
        zone.Start();

        if (store.LoadError is not null)
        {
            var message = store.ReadOnly
                ? Lang.T("toast.readonly")
                : Lang.Format("toast.corrupt", store.CorruptBackupPath ?? notesPath);
            var toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            toastTimer.Tick += (_, _) =>
            {
                toastTimer.Stop();
                window.Toast.ShowMessage(message, 9000);
            };
            toastTimer.Start();
        }

        if (GetValue(args, "--quit-after") is { } quitAfter && int.TryParse(quitAfter, out var quitMs))
        {
            var quitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(quitMs) };
            quitTimer.Tick += (_, _) =>
            {
                quitTimer.Stop();
                store.Flush();
                app.Shutdown();
            };
            quitTimer.Start();
            Log.Write($"dev: quit after {quitMs}ms");
        }

        Log.Write($"ready: notes={store.Count} data={notesPath}");
        Log.Write("Ctrl+Alt+T 开关绳子，Ctrl+Alt+N 新建，Ctrl+Alt+Z 撤销删除，Ctrl+Alt+L 开关绳头，Ctrl+Alt+Shift+Q 退出");

        app.Exit += (_, _) =>
        {
            store.Flush();
            tray.Dispose();
        };

        app.Run(window);
        return 0;
    }

    private static string? GetValue(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
