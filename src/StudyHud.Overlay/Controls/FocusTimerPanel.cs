using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// Floating Focus-Mode timer panel (spec §66). Shares the singleton <see cref="PomodoroService"/> with
/// the settings Focus page, so starting the timer anywhere shows it here as a HUD box. Inherits the
/// themed frame (corner brackets / scanlines / glass) from <see cref="HudPanelBase"/> like every other
/// panel. Presentation only — the clock logic lives entirely in <see cref="PomodoroService"/>.
/// </summary>
public sealed class FocusTimerPanel : HudPanelBase
{
    private readonly PomodoroService _pomodoro;

    private TextBlock _phase = null!;
    private TextBlock _time = null!;
    private ProgressBar _progress = null!;
    private StackPanel _dots = null!;
    private Button _startPause = null!;

    public FocusTimerPanel(IApplicationStateService appState, IThemeService theme, PomodoroService pomodoro)
        : base("focus-timer-panel", appState, theme)
    {
        _pomodoro = pomodoro;
        MinWidth = 200;
        MinHeight = 150;
        Width = 300;
    }

    protected override string PanelTitle => "Focus Timer";

    protected override void PopulateContent(Grid contentGrid)
    {
        bool glow = Application.Current?.TryFindResource("PhosphorGlow") is true;
        var accent = Brush("Accent", Color.FromRgb(0, 180, 255));

        var stack = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };

        _phase = new TextBlock
        {
            Text = "READY", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = accent, FontFamily = Mono(), Effect = glow ? Glow(accent, 0.5, 14) : null
        };
        stack.Children.Add(_phase);

        _time = new TextBlock
        {
            Text = "25:00", FontSize = 44, FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("PrimaryText", Colors.White),
            FontFamily = Mono(), Margin = new Thickness(0, 2, 0, 8), Effect = glow ? Glow(accent, 0.5, 16) : null
        };
        stack.Children.Add(_time);

        _progress = new ProgressBar
        {
            Height = 6, Minimum = 0, Maximum = 100, Value = 0,
            Foreground = accent, Background = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 10)
        };
        stack.Children.Add(_progress);

        _dots = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        stack.Children.Add(_dots);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _startPause = MakeButton("Start", accent: true);
        _startPause.Click += (_, _) => { _pomodoro.Toggle(); UpdateUi(); };
        buttons.Children.Add(_startPause);
        var skip = MakeButton("Skip", accent: false); skip.Margin = new Thickness(6, 0, 0, 0);
        skip.Click += (_, _) => { _pomodoro.Skip(); UpdateUi(); };
        buttons.Children.Add(skip);
        var reset = MakeButton("Reset", accent: false); reset.Margin = new Thickness(6, 0, 0, 0);
        reset.Click += (_, _) => { _pomodoro.Reset(); UpdateUi(); };
        buttons.Children.Add(reset);
        stack.Children.Add(buttons);

        contentGrid.Children.Add(stack);

        Loaded += (_, _) =>
        {
            _pomodoro.Tick += OnPomodoro;
            _pomodoro.PhaseChanged += OnPomodoro;
            UpdateUi();
        };
        Unloaded += (_, _) =>
        {
            _pomodoro.Tick -= OnPomodoro;
            _pomodoro.PhaseChanged -= OnPomodoro;
        };
    }

    private void OnPomodoro(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateUi);

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
    }

    private static FontFamily Mono() =>
        Application.Current?.TryFindResource("MonoFontFamily") as FontFamily
        ?? new FontFamily("Cascadia Code, Consolas");

    private static DropShadowEffect Glow(Brush accent, double opacity, double blur)
    {
        var c = (accent as SolidColorBrush)?.Color ?? Color.FromRgb(0, 180, 255);
        return new DropShadowEffect { Color = c, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity };
    }

    private Button MakeButton(string content, bool accent) => new()
    {
        Content = content,
        Padding = new Thickness(12, 4, 12, 4),
        Cursor = System.Windows.Input.Cursors.Hand,
        BorderThickness = new Thickness(0),
        Foreground = accent ? Brushes.White : Brush("SecondaryText", Colors.Gray),
        Background = accent ? Brush("Accent", Color.FromRgb(0, 180, 255)) : Brushes.Transparent
    };

    private static Brush Brush(string token, Color fallback) =>
        Application.Current?.TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
