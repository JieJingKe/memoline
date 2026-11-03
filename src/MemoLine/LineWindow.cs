using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MemoLine;

internal sealed class LineWindow : Window
{
    private const double EdgeFade = 90;
    private const double PreviewWidth = 340;
    private const double PreviewBodyMaxHeight = 320;
    private const double PreviewTotalMaxHeight = 400;
    private const double PreviewGap = 8;

    private const int ToggleHotkeyId = 11;
    private const int QuitHotkeyId = 12;
    private const int AnchorHotkeyId = 13;
    private const int NewHotkeyId = 14;
    private const int UndoHotkeyId = 15;
    private const int ManualGraceMs = 2500;

    private const uint MenuEditId = 401;
    private const uint MenuRemoveId = 402;

    private readonly NotesStore _store;
    private readonly Canvas _canvas = new();
    private readonly List<CardView> _cards = [];
    private readonly Stack<NoteItem> _undo = new();
    private readonly Rectangle _line;
    private readonly AddSlot _addSlot;
    private readonly Toast _toast = new();

    private IntPtr _hwnd;
    private double _scale = 1.0;
    private double _ignoreRetractUntil;
    private double _scroll;
    private Native.MONITORINFOEX _monitor;
    private bool _open;
    private bool _suppressLogged;
    private bool _layoutReady;
    private int _removingCount;

    private bool _editing;
    private CardView? _editingCard;
    private CardView? _draftCard;
    private IntPtr _editReturnHwnd;
    private bool _suppressDeactivated;

    private CardView? _previewCard;
    private Border? _previewPanel;
    private ScrollViewer? _previewScroll;
    private double _previewHeight;

    public LineWindow(NotesStore store)
    {
        _store = store;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Title = "MemoLine";
        Background = Brushes.Transparent;
        Width = 420;
        Height = Metrics.BaseWindowHeight;
        Content = _canvas;

        _line = new Rectangle
        {
            Height = 3,
            RadiusX = 1.5,
            RadiusY = 1.5,
            Fill = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
        };
        _canvas.Children.Add(_line);

        _addSlot = new AddSlot();
        _addSlot.ClickRequested += StartNewNote;
        _canvas.Children.Add(_addSlot);

        foreach (var note in store.Notes)
        {
            var card = new CardView(note);
            WireCard(card);
            _cards.Add(card);
            _canvas.Children.Add(card);
        }

        Deactivated += OnDeactivated;
        Closing += OnClosing;
        Log.Write($"window constructed; notes={_cards.Count} canUndo={CanUndo}");
    }

    public bool IsOpen => _open;

    public bool IsEditing => _editing;

    public IntPtr WindowHandle => _hwnd;

    public int CardCount => _cards.Count;

    public int NoteCount => _store.Count;

    public int VisibleCount { get; private set; } = 1;

    public bool CanUndo => _undo.Count > 0;

    public Toast Toast => _toast;

    public double Scale => _scale;

    public bool HotkeysReady { get; private set; }

    public Native.MONITORINFOEX PrimaryMonitor => MonitorNearestTo(new Native.POINT { X = 0, Y = 0 });

    internal Native.MONITORINFOEX CurrentMonitor => _monitor;

    internal bool IsPreviewOpen => _previewPanel is not null;

    internal double ScrollOffset => _scroll;

    internal double AddSlotLeft => Canvas.GetLeft(_addSlot);

    public bool InGrace => Environment.TickCount64 < _ignoreRetractUntil;

    public (double LeftDip, double WidthDip) TriggerZoneDip => Zone.Trigger(_monitor, _scale);

    public Native.RECT OpenedBoundsPhysical => new()
    {
        Left = (int)Math.Round(Left * _scale),
        Top = (int)Math.Round(TargetTop() * _scale),
        Right = (int)Math.Round((Left + Width) * _scale),
        Bottom = (int)Math.Round((TargetTop() + Height) * _scale),
    };

    internal IReadOnlyList<CardView> Cards => _cards;

    internal CardView? DraftCard => _draftCard;

    internal CardView? EditingCard => _editingCard;

    internal void TogglePreviewForTest(CardView card) => TogglePreview(card);

    internal void ScrollForTest(int delta) => ScrollBy(delta);

    public event Action<bool>? OpenChanged;

    public event Action? AnchorToggleRequested;

    public event Action? QuitRequested;

    public event Action? ContentChanged;

    public bool CursorInTriggerZone(Native.POINT cursor) => Zone.Contains(cursor, MonitorNearestTo(cursor), _scale);

    public void Reveal(bool manual = false)
    {
        if (_open)
        {
            return;
        }

        if (!Native.GetCursorPos(out var cursor))
        {
            return;
        }

        _monitor = MonitorNearestTo(cursor);
        _open = true;
        Layout();
        Top = HiddenTop();

        if (OnboardingState.NeedsTip)
        {
            OnboardingState.MarkOnboarded();
        }

        OpenChanged?.Invoke(true);
        if (manual)
        {
            _ignoreRetractUntil = Environment.TickCount64 + ManualGraceMs;
        }

        Log.Write($"G2 reveal manual={manual} {_monitor} scale={_scale:F2} window=({Left:F0},{Top:F0},{Width:F0}x{Height:F0})");
        AnimateTop(HiddenTop(), TargetTop(), 420, new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut });
        for (var index = 0; index < _cards.Count; index++)
        {
            _cards[index].StartSwing(TimeSpan.FromMilliseconds(index * 45));
        }
    }

    public void Retract()
    {
        if (!_open || _editing || _removingCount > 0)
        {
            return;
        }

        _open = false;
        _scroll = 0;
        ClosePreview(relayout: false);
        foreach (var card in _cards)
        {
            card.SnapAnimations();
        }

        OpenChanged?.Invoke(false);
        Log.Write($"G2 retract window=({Left:F0},{Top:F0})");
        AnimateTop(TargetTop(), HiddenTop(), 200, new CubicEase { EasingMode = EasingMode.EaseIn });
    }

    public void ToggleManually()
    {
        if (_open)
        {
            Retract();
        }
        else
        {
            Reveal(manual: true);
        }
    }

    public void ToggleAnchor() => AnchorToggleRequested?.Invoke();

    public void RefreshLanguage()
    {
        foreach (var card in _cards)
        {
            card.RefreshLanguage();
        }

        _addSlot.RefreshLanguage();
        if (_previewCard is not null)
        {
            ShowPreview(_previewCard);
        }
    }

    /// <summary>托盘/热键入口：先展开绳子，再把空夹子变成编辑态。</summary>
    public void StartNewNote()
    {
        if (_editing)
        {
            // 空白草稿还开着时再次“新建”只是重复聚焦；取消再重建会让整条绳抖一下。
            if (_editingCard is { IsDraft: true } draft
                && string.IsNullOrEmpty(NoteItem.NormalizeText(draft.EditText)))
            {
                draft.FocusEditor();
                Log.Write("draft kept: blank editor already open");
                return;
            }

            CommitCurrentEdit(restoreFocus: false);
        }

        if (_open)
        {
            StartDraft();
            return;
        }

        Reveal(manual: true);
        RunAfter(TimeSpan.FromMilliseconds(460), StartDraft);
    }

    public void UndoDelete()
    {
        if (_editing)
        {
            CommitCurrentEdit(restoreFocus: false);
        }

        if (_undo.Count == 0)
        {
            _toast.ShowMessage(Lang.T("toast.undoEmpty"));
            return;
        }

        var note = _undo.Pop();
        if (!_store.Add(note))
        {
            _undo.Push(note);
            _toast.ShowMessage(Lang.Format("toast.limit", Metrics.MaxNotes));
            return;
        }

        var card = new CardView(note);
        WireCard(card);
        InsertCardSorted(card);
        _canvas.Children.Add(card);
        if (!_open)
        {
            Reveal(manual: true);
        }

        Layout(animate: true);
        card.PlayHang();
        _toast.ShowMessage(Lang.T("toast.undone"));
        Log.Write($"undo delete restored {note.Id}, notes={_store.Count}");
        ContentChanged?.Invoke();
    }

    internal void CommitCurrentEdit(bool restoreFocus)
    {
        var card = _editingCard;
        if (!_editing || card is null)
        {
            return;
        }

        var text = NoteItem.NormalizeText(card.EditText);
        if (string.IsNullOrEmpty(text))
        {
            CancelCurrentEdit(restoreFocus);
            return;
        }

        var quadrant = card.EditQuadrant;
        if (card.IsDraft)
        {
            var note = card.Note;
            note.Text = text;
            note.Quadrant = quadrant;
            if (!_store.Add(note))
            {
                card.ShowValidationError(Lang.Format("toast.limit", Metrics.MaxNotes));
                return;
            }

            card.EndEdit();
            card.MarkSaved();
            card.PlayHang();
            _draftCard = null;
            SortCards();
            Log.Write($"draft saved {note.Id} quadrant={Quadrants.Key(note.Quadrant)} len={note.Text.Length}");
        }
        else
        {
            var note = card.Note;
            note.Text = text;
            note.Quadrant = quadrant;
            _store.Touch(note);
            card.EndEdit();
            card.Refresh();
            Log.Write($"note updated {note.Id} quadrant={Quadrants.Key(note.Quadrant)} len={note.Text.Length}");
        }

        _editing = false;
        _editingCard = null;
        FinishEditFocus(restoreFocus);
        Layout(animate: false);
        ContentChanged?.Invoke();
    }

    internal void CancelCurrentEdit(bool restoreFocus)
    {
        var card = _editingCard;
        if (!_editing || card is null)
        {
            return;
        }

        var wasDraft = card.IsDraft;
        card.EndEdit();
        _editing = false;
        _editingCard = null;

        if (wasDraft)
        {
            _cards.Remove(card);
            _canvas.Children.Remove(card);
            _draftCard = null;
            Log.Write($"draft cancelled {card.Note.Id}");
        }
        else
        {
            card.Refresh();
            Log.Write($"note edit cancelled {card.Note.Id}");
        }

        FinishEditFocus(restoreFocus);
        Layout(animate: true);
        ContentChanged?.Invoke();
    }

    internal void DeleteNote(CardView card)
    {
        if (_editing || card.IsDraft || card.IsRemoving)
        {
            return;
        }

        var note = card.Note;
        if (!_store.Remove(note))
        {
            return;
        }

        _undo.Push(note);
        _cards.Remove(card);
        if (_previewCard == card)
        {
            ClosePreview(relayout: false);
        }

        _removingCount++;
        card.PlayRemove(() =>
        {
            _canvas.Children.Remove(card);
            _removingCount = Math.Max(0, _removingCount - 1);
            Layout(animate: true);
            ContentChanged?.Invoke();
            if (_open)
            {
                _toast.ShowMessage(Lang.T("toast.deleted"));
            }
        });

        Log.Write($"note removed {note.Id}, notes={_store.Count} canUndo=True");
        ContentChanged?.Invoke();
    }

    public void FinishEditingForExit()
    {
        if (!_editing)
        {
            return;
        }

        CommitCurrentEdit(restoreFocus: false);
        if (_editing)
        {
            CancelCurrentEdit(restoreFocus: false);
        }
    }

    public bool ShouldSuppress(Native.POINT cursor)
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _hwnd || !Native.IsWindowVisible(foreground))
        {
            return false;
        }

        var description = Native.DescribeWindow(foreground);
        if (description.Contains("[explorer]", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (description.Contains("[mstsc]", StringComparison.OrdinalIgnoreCase))
        {
            LogSuppression($"remote session foreground={description}");
            return true;
        }

        if (!Native.GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        var monitor = MonitorNearestTo(cursor);
        var coversWholeMonitor =
            rect.Left <= monitor.rcMonitor.Left &&
            rect.Top <= monitor.rcMonitor.Top &&
            rect.Right >= monitor.rcMonitor.Right &&
            rect.Bottom >= monitor.rcMonitor.Bottom;
        if (coversWholeMonitor)
        {
            LogSuppression($"fullscreen foreground={description}");
            return true;
        }

        _suppressLogged = false;
        return false;
    }

    private void LogSuppression(string message)
    {
        if (_suppressLogged)
        {
            return;
        }

        _suppressLogged = true;
        Log.Write($"G2 suppressed reveal {message}");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var source = (HwndSource?)PresentationSource.FromVisual(this);
        _hwnd = source?.Handle ?? IntPtr.Zero;
        if (_hwnd == IntPtr.Zero)
        {
            Log.Write("FATAL no hwnd");
            return;
        }

        Native.MakeNonActivating(_hwnd);
        source!.AddHook(WndProc);
        _scale = Native.GetDpiForWindow(_hwnd) / 96.0;

        var toggle = Native.RegisterHotKey(_hwnd, ToggleHotkeyId, Native.MOD_CONTROL | Native.MOD_ALT, 0x54);
        var quit = Native.RegisterHotKey(_hwnd, QuitHotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT, 0x51);
        var anchor = Native.RegisterHotKey(_hwnd, AnchorHotkeyId, Native.MOD_CONTROL | Native.MOD_ALT, 0x4C);
        var newNote = Native.RegisterHotKey(_hwnd, NewHotkeyId, Native.MOD_CONTROL | Native.MOD_ALT, 0x4E);
        var undo = Native.RegisterHotKey(_hwnd, UndoHotkeyId, Native.MOD_CONTROL | Native.MOD_ALT, 0x5A);
        HotkeysReady = toggle && quit && anchor && newNote && undo;
        Log.Write($"G1 hwnd=0x{_hwnd.ToInt64():x} scale={_scale:F2} hotkeys toggle={toggle} quit={quit} anchor={anchor} new={newNote} undo={undo}");

        ParkOffscreen();
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var id in new[] { ToggleHotkeyId, QuitHotkeyId, AnchorHotkeyId, NewHotkeyId, UndoHotkeyId })
        {
            Native.UnregisterHotKey(_hwnd, id);
        }

        base.OnClosed(e);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        FinishEditingForExit();
        _toast.Dismiss();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_editing || _suppressDeactivated)
        {
            return;
        }

        Log.Write("edit deactivated → save and leave focus where the user clicked");
        CommitCurrentEdit(restoreFocus: false);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_MOUSEACTIVATE)
        {
            if (_editing)
            {
                return IntPtr.Zero;
            }

            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        }

        if (msg == Native.WM_MOUSEWHEEL && _open && !_editing)
        {
            var delta = (short)(wParam.ToInt64() >> 16);
            if (CursorOverPreview(out _))
            {
                _previewScroll?.ScrollToVerticalOffset(_previewScroll.VerticalOffset - delta / 120.0 * 60);
            }
            else
            {
                ScrollBy(delta);
            }

            handled = true;
            return IntPtr.Zero;
        }

        if (msg == Native.WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case ToggleHotkeyId:
                    Log.Write("hotkey Ctrl+Alt+T");
                    if (!_editing)
                    {
                        ToggleManually();
                    }

                    return IntPtr.Zero;
                case QuitHotkeyId:
                    Log.Write("hotkey Ctrl+Alt+Shift+Q → quit");
                    QuitRequested?.Invoke();
                    return IntPtr.Zero;
                case AnchorHotkeyId:
                    Log.Write("hotkey Ctrl+Alt+L → anchor toggle");
                    if (!_editing)
                    {
                        ToggleAnchor();
                    }

                    return IntPtr.Zero;
                case NewHotkeyId:
                    Log.Write("hotkey Ctrl+Alt+N → new note");
                    StartNewNote();
                    return IntPtr.Zero;
                case UndoHotkeyId:
                    Log.Write("hotkey Ctrl+Alt+Z → undo delete");
                    if (!_editing)
                    {
                        UndoDelete();
                    }

                    return IntPtr.Zero;
            }
        }

        return IntPtr.Zero;
    }

    private void StartDraft()
    {
        if (_editing)
        {
            CommitCurrentEdit(restoreFocus: false);
        }

        if (_draftCard is not null)
        {
            if (ReferenceEquals(_editingCard, _draftCard))
            {
                _draftCard.FocusEditor();
            }
            else
            {
                BeginEdit(_draftCard);
            }

            return;
        }

        if (!_store.CanAdd)
        {
            _toast.ShowMessage(Lang.Format("toast.limit", Metrics.MaxNotes));
            return;
        }

        var note = NoteItem.Draft();
        var card = new CardView(note, isDraft: true);
        WireCard(card);
        _cards.Add(card);
        _draftCard = card;
        _canvas.Children.Add(card);
        Layout(animate: true);
        Log.Write($"draft created {note.Id}, notes={_store.Count}");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => BeginEdit(card)));
    }

    private void WireCard(CardView card)
    {
        card.PreviewRequested += TogglePreview;
        card.EditRequested += BeginEdit;
        card.MenuRequested += ShowCardMenu;
        card.SaveRequested += _ => CommitCurrentEdit(restoreFocus: true);
        card.CancelRequested += _ => CancelCurrentEdit(restoreFocus: true);
    }

    private void BeginEdit(CardView card)
    {
        if (card.IsRemoving)
        {
            return;
        }

        if (_editing && ReferenceEquals(_editingCard, card))
        {
            card.FocusEditor();
            return;
        }

        if (!_open)
        {
            Reveal(manual: true);
        }

        if (_editing && !ReferenceEquals(_editingCard, card))
        {
            CommitCurrentEdit(restoreFocus: false);
        }

        ClosePreview();
        _editing = true;
        _editingCard = card;
        _editReturnHwnd = Native.GetForegroundWindow();
        if (_editReturnHwnd == _hwnd)
        {
            _editReturnHwnd = IntPtr.Zero;
        }

        card.BeginEdit();
        Native.MakeActivatable(_hwnd);
        Activate();
        var setForeground = Native.SetForegroundWindow(_hwnd);

        Log.Write($"edit begin note={card.Note.Id} returnFocus={Native.DescribeWindow(_editReturnHwnd)} setForeground={setForeground} active={IsActive}");
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusEditorWithRetry(card, 1)));
    }

    private void FocusEditorWithRetry(CardView card, int attempt)
    {
        if (!_editing || !ReferenceEquals(_editingCard, card))
        {
            return;
        }

        card.FocusEditor();
        if (card.IsKeyboardFocusWithin || attempt >= 3)
        {
            if (!card.IsKeyboardFocusWithin)
            {
                Log.Write($"edit focus FAILED after {attempt} attempts, active={IsActive}");
            }

            return;
        }

        RunAfter(TimeSpan.FromMilliseconds(80), () => FocusEditorWithRetry(card, attempt + 1));
    }

    private void FinishEditFocus(bool restoreFocus)
    {
        Native.MakeNonActivating(_hwnd);
        if (restoreFocus && _editReturnHwnd != IntPtr.Zero && _editReturnHwnd != Native.GetForegroundWindow())
        {
            _suppressDeactivated = true;
            var ok = Native.SetForegroundWindow(_editReturnHwnd);
            Log.Write($"edit end restore focus to {Native.DescribeWindow(_editReturnHwnd)} ok={ok}");
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _suppressDeactivated = false));
        }

        _editReturnHwnd = IntPtr.Zero;
    }

    private void TogglePreview(CardView card)
    {
        if (_editing || card.IsRemoving)
        {
            return;
        }

        if (ReferenceEquals(_previewCard, card))
        {
            ClosePreview();
            return;
        }

        ShowPreview(card);
    }

    private void ShowPreview(CardView card)
    {
        ClosePreview(relayout: false);
        var note = card.Note;

        _previewPanel = BuildPreviewPanel(note, out var scroll);
        _previewScroll = scroll;
        _previewPanel.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ClosePreview();
        };

        _previewCard = card;
        _canvas.Children.Add(_previewPanel);
        _previewPanel.Measure(new Size(PreviewWidth, double.PositiveInfinity));
        _previewHeight = Math.Min(Math.Ceiling(_previewPanel.DesiredSize.Height), PreviewTotalMaxHeight);
        _previewPanel.Height = _previewHeight;
        Layout(animate: false);
        Log.Write($"preview shown note={note.Id} height={_previewHeight:F0} windowHeight={Height:F0}");
    }

    internal static Border BuildPreviewPanel(NoteItem note, out ScrollViewer scroll)
    {
        var header = new TextBlock
        {
            Text = Lang.Format("preview.created", note.CreatedFullLabel, Quadrants.Label(note.Quadrant)),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xF2, 0xF4, 0xF6)),
            TextWrapping = TextWrapping.Wrap,
        };
        var body = new TextBlock
        {
            Text = note.Text,
            FontSize = 13,
            LineHeight = 19,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF6)),
            TextWrapping = TextWrapping.Wrap,
        };
        scroll = new ScrollViewer
        {
            Content = body,
            MaxHeight = PreviewBodyMaxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var hint = new TextBlock
        {
            Text = Lang.T("preview.hint"),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xF2, 0xF4, 0xF6)),
            Margin = new Thickness(0, 8, 0, 0),
        };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(scroll);
        stack.Children.Add(hint);

        return new Border
        {
            Width = PreviewWidth,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1F, 0x22, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 14, 10),
            Child = stack,
            Cursor = Cursors.Hand,
        };
    }

    private void ClosePreview(bool relayout = true)
    {
        if (_previewPanel is null)
        {
            return;
        }

        _canvas.Children.Remove(_previewPanel);
        _previewPanel = null;
        _previewScroll = null;
        _previewCard = null;
        _previewHeight = 0;
        if (relayout)
        {
            Layout(animate: false);
        }
        else
        {
            Height = Metrics.BaseWindowHeight;
        }

        Log.Write("preview closed");
    }

    private void PlacePreview()
    {
        if (_previewPanel is null || _previewCard is null)
        {
            return;
        }

        var cardLeft = Canvas.GetLeft(_previewCard);
        if (double.IsNaN(cardLeft))
        {
            cardLeft = Metrics.SidePadding;
        }

        var x = cardLeft + Metrics.CardWidth / 2 - PreviewWidth / 2;
        var maxX = Math.Max(Metrics.SidePadding, Width - Metrics.SidePadding - PreviewWidth);
        x = Math.Clamp(x, Metrics.SidePadding, maxX);
        Canvas.SetLeft(_previewPanel, x);
        Canvas.SetTop(_previewPanel, Metrics.BaseWindowHeight + PreviewGap);
    }

    private bool CursorOverPreview(out Native.POINT cursor)
    {
        cursor = default;
        if (_previewPanel is null || !Native.GetCursorPos(out cursor))
        {
            return false;
        }

        var left = (Left + Canvas.GetLeft(_previewPanel)) * _scale;
        var top = (Top + Canvas.GetTop(_previewPanel)) * _scale;
        var right = left + _previewPanel.Width * _scale;
        var bottom = top + _previewHeight * _scale;
        return cursor.X >= left && cursor.X <= right && cursor.Y >= top && cursor.Y <= bottom;
    }

    private void ShowCardMenu(CardView card)
    {
        if (_editing || card.IsRemoving || card.IsDraft)
        {
            return;
        }

        Native.GetCursorPos(out var point);
        var focusBefore = Native.GetForegroundWindow();
        Native.SetForegroundWindow(_hwnd);

        var menu = Native.CreatePopupMenu();
        Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)MenuEditId, Lang.T("ctx.edit"));
        Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)MenuRemoveId, Lang.T("ctx.remove"));
        var command = Native.TrackPopupMenuEx(
            menu,
            Native.TPM_RIGHTBUTTON | Native.TPM_RETURNCMD,
            point.X,
            point.Y,
            _hwnd,
            IntPtr.Zero);
        Native.DestroyMenu(menu);
        Native.PostMessage(_hwnd, (uint)Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);

        var focusAfter = Native.GetForegroundWindow();
        Log.Write($"card menu note={card.Note.Id} command={command} focus before=0x{focusBefore.ToInt64():x} after=0x{focusAfter.ToInt64():x} unchanged={focusBefore == focusAfter}");

        switch ((uint)command)
        {
            case MenuEditId:
                BeginEdit(card);
                break;
            case MenuRemoveId:
                DeleteNote(card);
                break;
        }
    }

    private void Layout(bool animate = false)
    {
        if (_monitor.rcWork.Width == 0)
        {
            return;
        }

        var capacity = CapacityDip();
        var visibleNotes = Math.Max(1, capacity - 1);
        var slots = _cards.Count + 1;
        var shown = Math.Min(slots, capacity);
        var minWidth = Metrics.SidePadding * 2 + shown * Metrics.CardWidth + (shown - 1) * Metrics.Gap;
        var (left, width) = Zone.Span(_monitor, _scale, minWidth);

        Width = width;
        Height = _previewPanel is null
            ? Metrics.BaseWindowHeight
            : Metrics.BaseWindowHeight + PreviewGap + _previewHeight;
        Left = left;
        VisibleCount = visibleNotes;

        var maxScroll = Math.Max(0, _cards.Count - visibleNotes) * Metrics.Step;
        _scroll = Math.Clamp(_scroll, 0, maxScroll);

        _line.Width = Math.Max(0, Width - Metrics.SidePadding * 2 + 16);
        Canvas.SetLeft(_line, Metrics.SidePadding - 8);
        Canvas.SetTop(_line, Metrics.LineY - 6);

        PlaceCards(animate);
        PlacePreview();

        Top = _open ? TargetTop() : HiddenTop();
        _layoutReady = true;
    }

    /// <summary>
    /// 最新一张紧挨着绳尾空夹子；装不下时空夹子钉在最右一格、旧便签沿绳往左排，
    /// 滚轮把更早的拉回来。只摆位置，不增删画布子元素（可反复调用）。
    /// </summary>
    private void PlaceCards(bool animate)
    {
        var capacity = CapacityDip();
        var visibleNotes = Math.Max(1, capacity - 1);
        var slots = _cards.Count + 1;
        var shown = Math.Min(slots, capacity);
        var rowWidth = shown * Metrics.CardWidth + (shown - 1) * Metrics.Gap;
        var startX = (Width - rowWidth) / 2;
        var overflowing = slots > capacity;
        var addX = startX;

        if (overflowing)
        {
            addX = startX + (capacity - 1) * Metrics.Step;
            for (var index = 0; index < _cards.Count; index++)
            {
                var x = addX - (_cards.Count - index) * Metrics.Step + _scroll;
                PlaceCard(_cards[index], x, animate);
            }
        }
        else
        {
            for (var index = 0; index < _cards.Count; index++)
            {
                PlaceCard(_cards[index], startX + index * Metrics.Step, animate);
            }

            addX = startX + _cards.Count * Metrics.Step;
        }

        var oldAddLeft = Canvas.GetLeft(_addSlot);
        if (double.IsNaN(oldAddLeft))
        {
            oldAddLeft = addX;
        }

        Canvas.SetLeft(_addSlot, addX);
        Canvas.SetTop(_addSlot, Metrics.LineY);
        _addSlot.ShiftTo(oldAddLeft, addX, animate && _layoutReady);
    }

    private void PlaceCard(CardView card, double x, bool animate)
    {
        var oldLeft = Canvas.GetLeft(card);
        if (double.IsNaN(oldLeft))
        {
            oldLeft = x;
        }

        Canvas.SetLeft(card, x);
        Canvas.SetTop(card, Metrics.LineY);
        card.ShiftTo(oldLeft, x, animate && _layoutReady);

        var centre = x + Metrics.CardWidth / 2;
        var opacity = Math.Clamp(Math.Min(centre, Width - centre) / EdgeFade, 0, 1);
        card.Opacity = opacity;
        card.IsHitTestVisible = opacity > 0.5 && !card.IsRemoving;
    }

    private void ScrollBy(int delta)
    {
        var most = Math.Max(0, _cards.Count - VisibleCount) * Metrics.Step;
        if (!_open || most <= 0)
        {
            return;
        }

        var next = Math.Clamp(_scroll + delta / 120.0 * Metrics.Step / 2, 0, most);
        if (Math.Abs(next - _scroll) < 0.5)
        {
            return;
        }

        _scroll = next;
        if (_previewPanel is not null)
        {
            ClosePreview(relayout: false);
        }

        Layout(animate: false);
        Log.Write($"scroll={_scroll:F0}/{most:F0} cards={_cards.Count} visible={VisibleCount}");
    }

    private int CapacityDip() =>
        Math.Max(1, (int)((Zone.WorkWidthDip(_monitor, _scale) - 24 - Metrics.SidePadding * 2 + Metrics.Gap) / Metrics.Step));

    private double TargetTop() => _monitor.rcMonitor.Top / _scale;

    private double HiddenTop() => TargetTop() - Height - 1;

    private void ParkOffscreen()
    {
        _monitor = MonitorNearestTo(new Native.POINT { X = 0, Y = 0 });
        Layout();
        Log.Write($"G1 parked offscreen {_monitor} window=({Left:F0},{Top:F0},{Width:F0}x{Height:F0})");
    }

    private Native.MONITORINFOEX MonitorNearestTo(Native.POINT point)
    {
        var handle = Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFOEX>() };
        Native.GetMonitorInfo(handle, ref info);
        return info;
    }

    private void AnimateTop(double from, double to, int durationMs, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        };
        BeginAnimation(TopProperty, animation);
    }

    private void InsertCardSorted(CardView card)
    {
        var index = _cards.Count;
        for (var i = 0; i < _cards.Count; i++)
        {
            if (_cards[i].Note.CreatedAt > card.Note.CreatedAt)
            {
                index = i;
                break;
            }
        }

        _cards.Insert(index, card);
    }

    private void SortCards() => _cards.Sort((a, b) => a.Note.CreatedAt.CompareTo(b.Note.CreatedAt));

    private void RunAfter(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }
}
