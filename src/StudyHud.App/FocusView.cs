using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Services;

namespace StudyHud.App;

/// <summary>
/// Focus Mode page (Focus Mode design, artboards 1b + 1f): a Pomodoro study timer with work/break
/// cycles and session stats, plus the cadence settings and the Quick-Search hotkey. Local only —
/// no network, nothing tracked off-device. Built in code (no XAML).
/// </summary>
public sealed class FocusView : UserControl
{
    private readonly PomodoroService _pomodoro;
    private readonly IApplicationStateService _appState;
    private readonly ISettingsStore _settings;

    private readonly TextBlock _time;
    private readonly TextBlock _phase;
    private readonly ProgressBar _progress;
    private readonly Button _startPause;
    private readonly TextBlock _stats;
    private readonly StackPanel _dots;

    private readonly TextBox _focusBox;
    private readonly TextBox _shortBox;
    private readonly TextBox _longBox;
    private readonly TextBox _afterBox;

    public FocusView(PomodoroService pomodoro, IApplicationStateService appState, ISettingsStore settings)
    {
        _pomodoro = pomodoro;
        _appState = appState;
        _settings = settings;

        var root = new StackPanel { Margin = new Thickness(4) };

        root.Children.Add(new TextBlock
        {
            Text = "Focus", FontSize = 20, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryText", Colors.White), Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "A Pomodoro study timer with work/break cycles. Local only — no network, nothing "
                 + "tracked off your device.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 0, 0, 16)
        });

        // ── Timer card (artboard 1b) ─────────────────────────────────────────
        var card = new Border
        {
            Background = Brush("SecondaryBackground", Color.FromArgb(180, 40, 40, 48)),
            BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var cardStack = new StackPanel { MinWidth = 340 };

        _phase = new TextBlock
        {
            Text = "READY", FontSize = 11, Opacity = 0.85,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("Accent", Color.FromRgb(0, 180, 255)),
            FontFamily = new FontFamily("Cascadia Code, Consolas")
        };
        cardStack.Children.Add(_phase);

        _time = new TextBlock
        {
            Text = "25:00", FontSize = 56, FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("PrimaryText", Colors.White),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Margin = new Thickness(0, 2, 0, 8)
        };
        cardStack.Children.Add(_time);

        _progress = new ProgressBar
        {
            Height = 6, Minimum = 0, Maximum = 100, Value = 0,
            Foreground = Brush("Accent", Color.FromRgb(0, 180, 255)),
            Background = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 10)
        };
        cardStack.Children.Add(_progress);

        _dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        cardStack.Children.Add(_dots);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _startPause = MakeButton("Start", accent: true);
        _startPause.Click += (_, _) => { _pomodoro.Toggle(); UpdateUi(); };
        buttons.Children.Add(_startPause);
        var skip = MakeButton("Skip", accent: false); skip.Margin = new Thickness(8, 0, 0, 0);
        skip.Click += (_, _) => { _pomodoro.Skip(); UpdateUi(); };
        buttons.Children.Add(skip);
        var reset = MakeButton("Reset", accent: false); reset.Margin = new Thickness(8, 0, 0, 0);
        reset.Click += (_, _) => { _pomodoro.Reset(); UpdateUi(); };
        buttons.Children.Add(reset);
        cardStack.Children.Add(buttons);

        card.Child = cardStack;
        root.Children.Add(card);

        _stats = new TextBlock
        {
            Opacity = 0.8, Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_stats);

        // ── Pomodoro lengths (artboard 1f) ───────────────────────────────────
        root.Children.Add(Header("POMODORO LENGTHS"));
        _focusBox = NumBox(); _shortBox = NumBox(); _longBox = NumBox(); _afterBox = NumBox();
        var grid = new StackPanel { Orientation = Orientation.Horizontal };
        grid.Children.Add(LabeledNum("Focus (min)", _focusBox));
        grid.Children.Add(LabeledNum("Short break", _shortBox));
        grid.Children.Add(LabeledNum("Long break", _longBox));
        grid.Children.Add(LabeledNum("Long after (cycles)", _afterBox));
        root.Children.Add(grid);
        var apply = MakeButton("Save lengths", accent: true);
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        apply.Margin = new Thickness(0, 8, 0, 0);
        apply.Click += (_, _) => SaveLengths();
        root.Children.Add(apply);

        // ── Quick-Search hotkey (artboard 1f) ────────────────────────────────
        root.Children.Add(Header("QUICK-SEARCH"));
        root.Children.Add(new TextBlock
        {
            Text = "Press  Ctrl + Shift + Space  anywhere to open the Quick-Search palette and search "
                 + "your notes. Panic-hide is Ctrl + Shift + H.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
            Foreground = Brush("SecondaryText", Colors.Gray)
        });

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _pomodoro.Tick -= OnPomodoro;
            _pomodoro.PhaseChanged -= OnPomodoro;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _focusBox.Text = _pomodoro.WorkMinutes.ToString();
        _shortBox.Text = _pomodoro.ShortBreakMinutes.ToString();
        _longBox.Text = _pomodoro.LongBreakMinutes.ToString();
        _afterBox.Text = _pomodoro.LongBreakEvery.ToString();

        _pomodoro.Tick += OnPomodoro;
        _pomodoro.PhaseChanged += OnPomodoro;
        UpdateUi();
    }

    private void SaveLengths()
    {
        _pomodoro.WorkMinutes = Clamp(_focusBox.Text, 1, 120, 25);
        _pomodoro.ShortBreakMinutes = Clamp(_shortBox.Text, 1, 60, 5);
        _pomodoro.LongBreakMinutes = Clamp(_longBox.Text, 1, 60, 15);
        _pomodoro.LongBreakEvery = Clamp(_afterBox.Text, 1, 12, 4);
        if (_pomodoro.Phase == PomodoroPhase.Idle) UpdateUi();

        _ = _settings.UpdateAsync(s => s with
        {
            FocusMinutes = _pomodoro.WorkMinutes,
            ShortBreakMinutes = _pomodoro.ShortBreakMinutes,
            LongBreakMinutes = _pomodoro.LongBreakMinutes,
            LongBreakEveryCycles = _pomodoro.LongBreakEvery
        });
    }

    private void OnPomodoro(object? sender, EventArgs e) => UpdateUi();

    private void UpdateUi()
    {
        var remaining = _pomodoro.Phase == PomodoroPhase.Idle
            ? TimeSpan.FromMinutes(_pomodoro.WorkMinutes)
            : _pomodoro.Remaining;
        _time.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";

        _phase.Text = _pomodoro.Phase switch
        {
            PomodoroPhase.Work => "FOCUS",
            PomodoroPhase.ShortBreak => "SHORT BREAK",
            PomodoroPhase.LongBreak => "LONG BREAK",
            _ => "READY"
        };

        _progress.Value = _pomodoro.PhaseLength.TotalSeconds > 0
            ? 100.0 * (1.0 - _pomodoro.Remaining.TotalSeconds / _pomodoro.PhaseLength.TotalSeconds)
            : 0;

        _startPause.Content = _pomodoro.IsRunning
            ? "Pause"
            : _pomodoro.Phase == PomodoroPhase.Idle ? "Start" : "Resume";

        // Session dots (how many focus blocks done in the current long-break cycle).
        _dots.Children.Clear();
        int every = Math.Max(1, _pomodoro.LongBreakEvery);
        int inCycle = _pomodoro.CompletedToday % every;
        for (int i = 0; i < every; i++)
            _dots.Children.Add(new Border
            {
                Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Margin = new Thickness(3, 0, 0, 0),
                Background = i < inCycle ? Brush("Accent", Color.FromRgb(0, 180, 255))
                                         : Brush("PanelBorder", Color.FromRgb(58, 58, 68))
            });

        _stats.Text = $"Completed today: {_pomodoro.CompletedToday} pomodoro"
                    + (_pomodoro.CompletedToday == 1 ? "" : "s")
                    + $"   •   Notes captured today: {NotesToday()}"
                    + (string.IsNullOrEmpty(_appState.Current.CurrentCourseId)
                        ? "" : $"   •   Course: {_appState.Current.CurrentCourseId}");
    }

    private static int NotesToday()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudyHud", "Notes");
            if (!Directory.Exists(dir)) return 0;
            var today = DateTime.Now.Date;
            return new DirectoryInfo(dir).GetFiles("*.png").Count(f => f.LastWriteTime.Date == today);
        }
        catch { return 0; }
    }

    private static int Clamp(string text, int min, int max, int fallback)
        => int.TryParse(text, out var v) ? Math.Clamp(v, min, max) : fallback;

    // ── UI helpers ───────────────────────────────────────────────────────────

    private TextBox NumBox() => new()
    {
        Width = 60, Height = 26, VerticalContentAlignment = VerticalAlignment.Center,
        FontFamily = new FontFamily("Cascadia Code, Consolas")
    };

    private UIElement LabeledNum(string label, TextBox box)
    {
        var col = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        col.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 0, 0, 4)
        });
        col.Children.Add(box);
        return col;
    }

    private TextBlock Header(string t) => new()
    {
        Text = t, FontSize = 10, Opacity = 0.5,
        Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 18, 0, 6)
    };

    private Button MakeButton(string content, bool accent) => new()
    {
        Content = content,
        Padding = new Thickness(16, 5, 16, 5),
        Cursor = System.Windows.Input.Cursors.Hand,
        BorderThickness = new Thickness(0),
        Foreground = accent ? Brushes.White : Brush("SecondaryText", Colors.Gray),
        Background = accent ? Brush("Accent", Color.FromRgb(0, 180, 255)) : Brushes.Transparent
    };

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
