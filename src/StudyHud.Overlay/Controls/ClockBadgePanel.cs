using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// Always-on time + momentum badge for ADHD Mode. Makes time <em>visible</em> (wall clock + a draining
/// ring while a focus block runs + minutes studied today) and shows the day-streak so progress is felt.
/// Presentation only over <see cref="PomodoroService"/> and <see cref="MomentumService"/>.
/// </summary>
public sealed class ClockBadgePanel : HudPanelBase
{
    private readonly PomodoroService _pomodoro;
    private readonly MomentumService _momentum;

    private CountdownRing _ring = null!;
    private TextBlock _clock = null!;
    private TextBlock _today = null!;
    private TextBlock _streak = null!;
    private DispatcherTimer _clockTimer = null!;

    public ClockBadgePanel(IApplicationStateService appState, IThemeService theme,
        PomodoroService pomodoro, MomentumService momentum)
        : base("clock-badge-panel", appState, theme)
    {
        _pomodoro = pomodoro;
        _momentum = momentum;
        MinWidth = 200;
        Width = 240;
    }

    protected override string PanelTitle => "Time";

    protected override void PopulateContent(Grid contentGrid)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 12) };

        _ring = new CountdownRing(56, 6) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        row.Children.Add(_ring);

        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _clock = new TextBlock
        {
            FontSize = 26, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryText", Colors.White), FontFamily = Mono()
        };
        col.Children.Add(_clock);

        _today = new TextBlock { FontSize = 12, Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 2, 0, 0) };
        col.Children.Add(_today);

        _streak = new TextBlock { FontSize = 12, Foreground = Brush("Accent", Color.FromRgb(90, 178, 168)), Margin = new Thickness(0, 2, 0, 0) };
        col.Children.Add(_streak);

        row.Children.Add(col);
        contentGrid.Children.Add(row);

        Loaded += (_, _) =>
        {
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => UpdateUi();
            _clockTimer.Start();
            _pomodoro.Tick += OnPomodoro;
            _pomodoro.PhaseChanged += OnPomodoro;
            _momentum.Changed += OnPomodoro;
            UpdateUi();
        };
        Unloaded += (_, _) =>
        {
            _clockTimer?.Stop();
            _pomodoro.Tick -= OnPomodoro;
            _pomodoro.PhaseChanged -= OnPomodoro;
            _momentum.Changed -= OnPomodoro;
        };
    }

    private void OnPomodoro(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateUi);

    private void UpdateUi()
    {
        _clock.Text = DateTime.Now.ToString("HH:mm");

        int liveMin = _pomodoro.Phase == PomodoroPhase.Work ? (int)_pomodoro.Elapsed.TotalMinutes : 0;
        int total = _momentum.FocusMinutesToday + liveMin;
        _today.Text = total <= 0 ? "No focus yet today" : $"Studied today: {total / 60}h {total % 60}m";

        _streak.Text = _momentum.Streak > 0
            ? $"🔥 {_momentum.Streak}-day streak"
            : "Start a streak today";

        // The ring drains during a work block; otherwise it sits full and quiet.
        var accent = Brush("Accent", Color.FromRgb(90, 178, 168));
        if (_pomodoro.Phase != PomodoroPhase.Idle && _pomodoro.PhaseLength.TotalSeconds > 0)
        {
            double frac = _pomodoro.Remaining.TotalSeconds / _pomodoro.PhaseLength.TotalSeconds;
            _ring.SetFraction(frac, accent, $"{(int)_pomodoro.Remaining.TotalMinutes}");
        }
        else
        {
            _ring.SetFraction(1, new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), "");
        }
    }

    private static FontFamily Mono() =>
        Application.Current?.TryFindResource("MonoFontFamily") as FontFamily ?? new FontFamily("Cascadia Code, Consolas");

    private Brush Brush(string token, Color fallback)
        => Application.Current?.TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
