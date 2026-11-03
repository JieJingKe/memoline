using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MemoLine;

/// <summary>
/// 一张便签 = 纸面（可独自升降/旋转/淡出）+ 夹子（始终留在绳上）。
/// 水平补位用整卡位移，纸面的垂直动效用另一条 Transform，两者互不覆盖。
/// </summary>
internal sealed class CardView : Grid
{
    private const double BodyWidth = 166;
    private const double FontSize = 13.2;
    private const double LineHeight = 17.6;
    private const int MaxLines = 4;

    private static readonly FontFamily CardFont = new("Segoe UI");
    private static readonly Brush HintInk = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0x33, 0x22, 0x11)));
    private static readonly Brush FaintInk = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0x33, 0x22, 0x11)));
    private static readonly Brush SaveInk = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x6B, 0x4F)));
    private static readonly Brush ErrorInk = Freeze(new SolidColorBrush(Color.FromRgb(0xB3, 0x3A, 0x2B)));

    private readonly NoteItem _note;
    private readonly Grid _paperHost;
    private readonly Border _paper;
    private readonly TranslateTransform _paperMove = new();
    private readonly RotateTransform _paperTilt = new();
    private readonly TranslateTransform _layoutShift = new();
    private readonly UIElement _pin;
    private readonly Grid _displayRoot;
    private readonly TextBlock _body;
    private readonly TextBlock _time;
    private readonly System.Windows.Shapes.Ellipse _dot;
    private readonly TextBlock _categoryHint;

    private readonly Grid _editRoot;
    private readonly TextBox _editor;
    private readonly TextBlock _selectedLabel;
    private readonly TextBlock _editHint;
    private readonly TextBlock _saveText;
    private readonly TextBlock _cancelText;
    private readonly List<Border> _quadButtons = [];

    private Quadrant _selectedQuadrant;
    private bool _editing;
    private bool _removing;
    private bool _isDraft;
    private bool _errorShowing;
    private long _lastClickTick;
    private Native.POINT _lastClickPoint;
    private bool _hasLastClick;

    public CardView(NoteItem note, bool isDraft = false)
    {
        _note = note;
        _isDraft = isDraft;
        _selectedQuadrant = note.Quadrant;

        Width = Metrics.CardWidth;
        Height = Metrics.CardHeight;
        Cursor = Cursors.Hand;
        RenderTransform = _layoutShift;

        _body = new TextBlock
        {
            FontFamily = CardFont,
            FontSize = FontSize,
            LineHeight = LineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = KraftPaper.Ink,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 6, 14, 0),
            MaxHeight = LineHeight * MaxLines,
        };

        _time = new TextBlock
        {
            FontFamily = CardFont,
            FontSize = 10.5,
            Foreground = HintInk,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        _categoryHint = new TextBlock
        {
            FontFamily = CardFont,
            FontSize = 10,
            Foreground = HintInk,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0,
        };

        _dot = new System.Windows.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 2, 0),
            IsHitTestVisible = false,
        };

        var footer = new Grid
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2, 0, 2, 4),
            Height = 14,
            IsHitTestVisible = false,
        };
        footer.Children.Add(_time);
        footer.Children.Add(_categoryHint);

        _displayRoot = new Grid();
        _displayRoot.Children.Add(_body);
        _displayRoot.Children.Add(_dot);
        _displayRoot.Children.Add(footer);

        var contentHost = new Grid();
        _paper = KraftPaper.BuildPaper(contentHost);
        contentHost.Children.Add(_displayRoot);
        _paperHost = new Grid();
        _paperHost.Children.Add(KraftPaper.BuildShadow());
        _paperHost.Children.Add(_paper);

        var paperTransform = new TransformGroup();
        paperTransform.Children.Add(_paperMove);
        paperTransform.Children.Add(_paperTilt);
        _paperHost.RenderTransform = paperTransform;
        _paperHost.RenderTransformOrigin = new Point(0.5, 0);

        _pin = KraftPaper.BuildClothespin();

        _editRoot = BuildEditor(out _editor, out _selectedLabel, out _editHint, out _saveText, out _cancelText);
        _editRoot.Visibility = Visibility.Collapsed;
        contentHost.Children.Add(_editRoot);

        Children.Add(_paperHost);
        Children.Add(_pin);

        _editor.TextChanged += (_, _) =>
        {
            _errorShowing = false;
            UpdateEditHint();
        };

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += (_, e) =>
        {
            if (_editing || _removing)
            {
                return;
            }

            e.Handled = true;
            MenuRequested?.Invoke(this);
        };
        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) => SetHover(false);

        Refresh();
    }

    public event Action<CardView>? PreviewRequested;

    public event Action<CardView>? EditRequested;

    public event Action<CardView>? MenuRequested;

    public event Action<CardView>? SaveRequested;

    public event Action<CardView>? CancelRequested;

    public NoteItem Note => _note;

    public bool IsEditing => _editing;

    public bool IsDraft => _isDraft;

    public bool IsRemoving => _removing;

    public string EditText => _editor.Text;

    public Quadrant EditQuadrant => _selectedQuadrant;

    public void BeginEdit()
    {
        if (_editing)
        {
            return;
        }

        _editing = true;
        _errorShowing = false;
        _selectedQuadrant = _note.Quadrant;
        _editor.Text = _note.Text;
        _editor.CaretIndex = _editor.Text.Length;
        _editor.MaxLength = Metrics.MaxTextLength;
        UpdateQuadrantSelection();
        UpdateEditHint();
        _categoryHint.Opacity = 0;
        _paper.BorderBrush = KraftPaper.Edge;
        _displayRoot.Visibility = Visibility.Collapsed;
        _editRoot.Visibility = Visibility.Visible;
        Log.Write($"edit begin note={_note.Id} draft={_isDraft}");
    }

    /// <summary>窗口激活后再调一次；WPF 有时在第一轮布局前拿不到键盘焦点。</summary>
    public void FocusEditor()
    {
        if (!_editing)
        {
            return;
        }

        _editor.Focus();
        Keyboard.Focus(_editor);
        _editor.CaretIndex = _editor.Text.Length;
        Log.Write($"edit focus editorFocused={_editor.IsKeyboardFocused} keyboardWithin={IsKeyboardFocusWithin} windowActive={Window.GetWindow(this)?.IsActive}");
    }

    public void ShowValidationError(string message)
    {
        _errorShowing = true;
        _editHint.Text = message;
        _editHint.Foreground = ErrorInk;
    }

    internal void SetEditorTextForTest(string text)
    {
        _editor.Text = text;
        _editor.CaretIndex = text.Length;
    }

    internal void SetQuadrantForTest(Quadrant quadrant)
    {
        _selectedQuadrant = quadrant;
        UpdateQuadrantSelection();
    }

    internal bool RegisterClickForTest(long now, Native.POINT point)
    {
        var isDouble = IsDoubleClick(now, point);
        if (isDouble)
        {
            _hasLastClick = false;
        }
        else
        {
            _hasLastClick = true;
            _lastClickTick = now;
            _lastClickPoint = point;
        }

        return isDouble;
    }

    public void EndEdit()
    {
        _editing = false;
        _editRoot.Visibility = Visibility.Collapsed;
        _displayRoot.Visibility = Visibility.Visible;
        _editor.Text = string.Empty;
    }

    public void MarkSaved()
    {
        _isDraft = false;
        _note.IsDraft = false;
        Refresh();
    }

    public void Refresh()
    {
        _body.Text = TruncateToLines(_note.Text, BodyWidth, FontSize, MaxLines);
        _time.Text = _note.CreatedLabel;
        _dot.Fill = Quadrants.Brush(_note.Quadrant);
        _dot.Stroke = Quadrants.RingBrush(_note.Quadrant);
        _dot.StrokeThickness = 1;
        _categoryHint.Text = Quadrants.ShortLabel(_note.Quadrant);
        _selectedQuadrant = _note.Quadrant;
        UpdateQuadrantSelection();
        UpdateEditHint();
    }

    public void RefreshLanguage()
    {
        _categoryHint.Text = Quadrants.ShortLabel(_note.Quadrant);
        _selectedLabel.Text = Quadrants.ShortLabel(_selectedQuadrant);
        _saveText.Text = Lang.T("edit.save");
        _cancelText.Text = Lang.T("edit.cancel");
        if (!_errorShowing)
        {
            _editHint.Text = Lang.T("edit.hint");
        }

        foreach (var button in _quadButtons)
        {
            if (button.Tag is Quadrant quadrant)
            {
                button.ToolTip = Quadrants.Label(quadrant);
            }
        }
    }

    /// <summary>整卡水平补位：从旧位置滑回新位置，夹子只做水平移动。</summary>
    public void ShiftTo(double oldLeft, double newLeft, bool animate)
    {
        // 上一段补位没走完时用当前视觉偏移续接，避免卡片先跳回旧位置再重新滑动。
        var offset = _layoutShift.X;
        _layoutShift.BeginAnimation(TranslateTransform.XProperty, null);
        var delta = oldLeft + offset - newLeft;
        // 补位是布局过渡而不是装饰动画：不随系统的“控件动画”开关退化，否则增删会瞬移。
        if (!animate || Math.Abs(delta) < 0.5)
        {
            _layoutShift.X = 0;
            return;
        }

        // 基准值保持 0：动画用 FillBehavior.Stop 收尾，结束时落到 0 而不是 delta。
        _layoutShift.X = 0;
        var animation = new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(Metrics.ShiftMs))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        };
        _layoutShift.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    internal double LayoutOffsetX => _layoutShift.X;

    /// <summary>新便签从空夹子位置滑出时淡入，避免整张卡在目的地一帧内硬冒出来。</summary>
    public void PlayEnter()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, Opacity, TimeSpan.FromMilliseconds(Metrics.ShiftMs))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    /// <summary>撤销删除时把便签从下方 72 DIP 挂回绳上；夹子先在终位淡入，全程不位移。</summary>
    public void PlayHang()
    {
        _removing = false;
        IsHitTestVisible = true;
        _paperHost.Opacity = 1;
        _pin.Opacity = 1;
        _paperMove.Y = 0;
        _paperTilt.Angle = 0;

        // 关闭系统动画时仍保留一次短淡入，避免便签硬生生出现。
        var paperFadeMs = SystemParameters.ClientAreaAnimation ? 100 : 120;
        _paperHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(paperFadeMs))
        {
            FillBehavior = FillBehavior.Stop,
        });
        _pin.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
        {
            FillBehavior = FillBehavior.Stop,
        });

        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var rise = new DoubleAnimation(72, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        rise.Completed += (_, _) => StartSwing(TimeSpan.Zero);
        _paperMove.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    /// <summary>摘下：纸面向下 96 DIP 并转 3°，夹子不动；纸面消失后夹子再淡出。</summary>
    public void PlayRemove(Action completed)
    {
        if (_removing)
        {
            return;
        }

        _removing = true;
        IsHitTestVisible = false;
        _categoryHint.Opacity = 0;

        if (!SystemParameters.ClientAreaAnimation)
        {
            _paperHost.Opacity = 0;
            _pin.Opacity = 0;
            var quickFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))
            {
                FillBehavior = FillBehavior.Stop,
            };
            quickFade.Completed += (_, _) => completed();
            _paperHost.BeginAnimation(OpacityProperty, quickFade);
            _pin.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))
            {
                FillBehavior = FillBehavior.Stop,
            });
            return;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160))
        {
            BeginTime = TimeSpan.FromMilliseconds(80),
            FillBehavior = FillBehavior.Stop,
        };
        _paperHost.BeginAnimation(OpacityProperty, fade);

        var tilt = new DoubleAnimation(0, 3, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop,
        };
        _paperTilt.BeginAnimation(RotateTransform.AngleProperty, tilt);

        var fall = new DoubleAnimation(0, 96, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop,
        };
        fall.Completed += (_, _) =>
        {
            var pinFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))
            {
                FillBehavior = FillBehavior.Stop,
            };
            pinFade.Completed += (_, _) =>
            {
                _pin.Opacity = 0;
                completed();
            };
            _pin.BeginAnimation(OpacityProperty, pinFade);
        };
        _paperMove.BeginAnimation(TranslateTransform.YProperty, fall);
    }

    public void StartSwing(TimeSpan delay)
    {
        if (!SystemParameters.ClientAreaAnimation || _removing)
        {
            return;
        }

        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, BeginTime = delay };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-2.4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-0.8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
        _paperTilt.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    /// <summary>收起绳子时把挂上/摆动动画吸附到终态，避免窗口移出屏幕后动画还在跑。</summary>
    public void SnapAnimations()
    {
        if (_removing)
        {
            return;
        }

        _paperHost.BeginAnimation(OpacityProperty, null);
        _paperMove.BeginAnimation(TranslateTransform.YProperty, null);
        _paperTilt.BeginAnimation(RotateTransform.AngleProperty, null);
        _pin.BeginAnimation(OpacityProperty, null);
        _paperHost.Opacity = 1;
        _paperMove.Y = 0;
        _paperTilt.Angle = 0;
        _pin.Opacity = 1;
    }

    private void SetHover(bool hover)
    {
        if (_editing || _removing)
        {
            return;
        }

        _paper.BorderBrush = hover ? KraftPaper.EdgeHover : KraftPaper.Edge;
        _categoryHint.Opacity = hover ? 0.62 : 0;
    }

    private bool IsDoubleClick(long now, Native.POINT point)
    {
        if (!_hasLastClick)
        {
            return false;
        }

        var elapsed = now - _lastClickTick;
        if (elapsed < 0 || elapsed > Native.GetDoubleClickTime())
        {
            return false;
        }

        var halfX = Math.Max(4, Native.GetSystemMetrics(Native.SM_CXDOUBLECLK) / 2);
        var halfY = Math.Max(4, Native.GetSystemMetrics(Native.SM_CYDOUBLECLK) / 2);
        return Math.Abs(point.X - _lastClickPoint.X) <= halfX
            && Math.Abs(point.Y - _lastClickPoint.Y) <= halfY;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing || _removing)
        {
            return;
        }

        Focusable = false;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_editing || _removing)
        {
            return;
        }

        // 非激活窗口里 WPF 在 MouseUp 上给不出 2，只能按系统双击时间和距离自己判定。
        var now = Environment.TickCount64;
        Native.GetCursorPos(out var point);
        var isDoubleClick = e.ClickCount >= 2 || IsDoubleClick(now, point);
        if (isDoubleClick)
        {
            _hasLastClick = false;
        }
        else
        {
            _hasLastClick = true;
            _lastClickTick = now;
            _lastClickPoint = point;
        }

        var before = Native.GetForegroundWindow();
        if (isDoubleClick)
        {
            EditRequested?.Invoke(this);
        }
        else
        {
            PreviewRequested?.Invoke(this);
        }

        var after = Native.GetForegroundWindow();
        Log.Write($"card click note={_note.Id} count={e.ClickCount} double={isDoubleClick} focus before=0x{before.ToInt64():x} after=0x{after.ToInt64():x} unchanged={before == after}");
    }

    private Grid BuildEditor(
        out TextBox editor,
        out TextBlock selectedLabel,
        out TextBlock hint,
        out TextBlock saveText,
        out TextBlock cancelText)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
            MinHeight = 62,
        });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });

        editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxLength = Metrics.MaxTextLength,
            FontFamily = CardFont,
            FontSize = 12.5,
            Foreground = KraftPaper.Ink,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CaretBrush = KraftPaper.Ink,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x2F, 0x6B, 0x4F)),
            SpellCheck = { IsEnabled = false },
        };
        var editorShell = new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x5A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x33, 0x22, 0x11)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            Child = editor,
        };
        root.Children.Add(editorShell);

        var quadRow = new Grid { Margin = new Thickness(0, 1, 0, 1), IsHitTestVisible = true };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var quadrant in Quadrants.All)
        {
            dots.Children.Add(MakeQuadButton(quadrant));
        }

        selectedLabel = new TextBlock
        {
            FontFamily = CardFont,
            FontSize = 10,
            Foreground = HintInk,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        quadRow.Children.Add(dots);
        quadRow.Children.Add(selectedLabel);
        Grid.SetRow(quadRow, 2);
        root.Children.Add(quadRow);

        var buttonRow = new Grid();
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        saveText = new TextBlock
        {
            FontFamily = CardFont,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = SaveInk,
            Text = Lang.T("edit.save"),
        };
        cancelText = new TextBlock
        {
            FontFamily = CardFont,
            FontSize = 11,
            Foreground = HintInk,
            Text = Lang.T("edit.cancel"),
        };
        hint = new TextBlock
        {
            FontFamily = CardFont,
            FontSize = 9,
            Foreground = FaintInk,
            Text = Lang.T("edit.hint"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var saveButton = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(2, 0, 6, 0),
            Cursor = Cursors.Hand,
            Child = saveText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var cancelButton = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 0, 2, 0),
            Cursor = Cursors.Hand,
            Child = cancelText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        saveButton.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            SaveRequested?.Invoke(this);
        };
        cancelButton.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CancelRequested?.Invoke(this);
        };

        buttonRow.Children.Add(saveButton);
        Grid.SetColumn(hint, 1);
        buttonRow.Children.Add(hint);
        Grid.SetColumn(cancelButton, 2);
        buttonRow.Children.Add(cancelButton);
        Grid.SetRow(buttonRow, 3);
        root.Children.Add(buttonRow);

        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                SaveRequested?.Invoke(this);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelRequested?.Invoke(this);
            }
        };

        return root;
    }

    private Border MakeQuadButton(Quadrant quadrant)
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = Quadrants.Brush(quadrant),
            Stroke = Quadrants.RingBrush(quadrant),
            StrokeThickness = 1,
        };
        var button = new Border
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 5, 0),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Child = dot,
            Cursor = Cursors.Hand,
            Tag = quadrant,
            ToolTip = Quadrants.Label(quadrant),
        };
        ToolTipService.SetInitialShowDelay(button, 350);
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _selectedQuadrant = quadrant;
            UpdateQuadrantSelection();
        };
        _quadButtons.Add(button);
        return button;
    }

    private void UpdateQuadrantSelection()
    {
        foreach (var button in _quadButtons)
        {
            if (button.Tag is not Quadrant quadrant)
            {
                continue;
            }

            var selected = quadrant == _selectedQuadrant;
            button.BorderBrush = selected ? Quadrants.Brush(quadrant) : Brushes.Transparent;
            button.Opacity = selected ? 1 : 0.58;
        }

        if (_selectedLabel is not null)
        {
            _selectedLabel.Text = Quadrants.ShortLabel(_selectedQuadrant);
        }
    }

    private void UpdateEditHint()
    {
        if (_errorShowing || _editor is null)
        {
            return;
        }

        _editHint.Foreground = FaintInk;
        var length = _editor.Text.Length;
        _editHint.Text = length >= 1800
            ? Lang.Format("edit.counter", length, Metrics.MaxTextLength)
            : Lang.T("edit.hint");
    }

    /// <summary>
    /// WPF 的 TextBlock 在 Wrap + MaxHeight 下不保证出省略号，这里量出来手动截成 4 行。
    /// 先在完整文本上量一次，只有超行才二分查找能放下的最长前缀。
    /// </summary>
    private static string TruncateToLines(string text, double width, double fontSize, int maxLines)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var typeface = new Typeface(CardFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var limit = fontSize * 1.34 * maxLines;

        double Measure(string candidate)
        {
            var formatted = new FormattedText(
                candidate,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                1.0)
            {
                MaxTextWidth = width,
                Trimming = TextTrimming.None,
            };
            return formatted.Height;
        }

        if (Measure(text) <= limit)
        {
            return text;
        }

        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (Measure(text[..mid] + "…") <= limit)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return text[..Math.Max(0, low)].TrimEnd() + "…";
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
