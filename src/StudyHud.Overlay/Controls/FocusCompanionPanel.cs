using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// The ADHD "focus companion": a small, warm presence that reacts to the study session. It provides a
/// gentle sense of body-doubling, an encouraging (never shaming) voice, a light distraction mirror
/// during focus (the Focus Shield), a hyperfocus break nudge at the session cap, and a decision-free
/// wind-down card when a block ends. All presentation over existing services.
/// </summary>
public sealed class FocusCompanionPanel : HudPanelBase
{
    private readonly PomodoroService _pomodoro;
    private readonly MomentumService _momentum;
    private readonly IForegroundWindowService _foreground;
    private readonly ISettingsStore _settings;

    private TextBlock _mood = null!;
    private TextBlock _message = null!;
    private TextBlock _sub = null!;
    private Button _action = null!;

    private int _distractions;
    private bool _capActive;
    private bool _shieldEnabled = true;
    private bool _windDownEnabled = true;

    public FocusCompanionPanel(IApplicationStateService appState, IThemeService theme,
        PomodoroService pomodoro, MomentumService momentum,
        IForegroundWindowService foreground, ISettingsStore settings)
        : base("focus-companion-panel", appState, theme)
    {
        _pomodoro = pomodoro;
        _momentum = momentum;
        _foreground = foreground;
        _settings = settings;
        MinWidth = 240;
        Width = 300;
    }

    protected override string PanelTitle => "Companion";

    protected override void PopulateContent(Grid contentGrid)
    {
        var stack = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };

        _mood = new TextBlock { FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(_mood);

        _message = new TextBlock
        {
            FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            Foreground = Brush("PrimaryText", Colors.White), Margin = new Thickness(0, 6, 0, 0)
        };
        stack.Children.Add(_message);

        _sub = new TextBlock
        {
            FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            Opacity = 0.75, Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed
        };
        stack.Children.Add(_sub);

        _action = new Button
        {
            Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(0),
            Foreground = Brushes.White, Background = Brush("Accent", Color.FromRgb(90, 178, 168)),
            Visibility = Visibility.Collapsed
        };
        stack.Children.Add(_action);

        contentGrid.Children.Add(stack);

        Loaded += async (_, _) =>
        {
            try
            {
                var s = await _settings.LoadAsync();
                _shieldEnabled = s.FocusShieldEnabled;
                _windDownEnabled = s.WindDownCard;
            }
            catch { /* defaults fine */ }

            _pomodoro.PhaseChanged += OnPhaseChanged;
            _pomodoro.FocusBlockCompleted += OnFocusBlockCompleted;
            _pomodoro.SessionCapReached += OnSessionCap;
            _momentum.Changed += OnAny;
            _foreground.ContextChanged += OnForeground;
            RefreshUi();
        };
        Unloaded += (_, _) =>
        {
            _pomodoro.PhaseChanged -= OnPhaseChanged;
            _pomodoro.FocusBlockCompleted -= OnFocusBlockCompleted;
            _pomodoro.SessionCapReached -= OnSessionCap;
            _momentum.Changed -= OnAny;
            _foreground.ContextChanged -= OnForeground;
        };
    }

    private void OnAny(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshUi);

    private void OnPhaseChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_pomodoro.Phase == PomodoroPhase.Work) { _distractions = 0; _capActive = false; }
        RefreshUi();
    });

    private void OnSessionCap(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        _capActive = true;
        RefreshUi();
    });

    private void OnFocusBlockCompleted(object? sender, int minutes) => Dispatcher.BeginInvoke(RefreshUi);

    private void OnForeground(object? sender, ForegroundContextChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        // Count app switches away from Study HUD during a focus block — a gentle mirror, not surveillance.
        if (!_shieldEnabled || _pomodoro.Phase != PomodoroPhase.Work) return;
        if (!e.Current.IsStudyHudOwned && e.Current.ExecutableName != e.Previous.ExecutableName)
        {
            _distractions++;
            RefreshUi();
        }
    });

    private void RefreshUi()
    {
        var accent = Brush("Accent", Color.FromRgb(90, 178, 168));
        _action.Visibility = Visibility.Collapsed;
        _sub.Visibility = Visibility.Collapsed;

        // Hyperfocus cap takes priority — surface a gentle break nudge.
        if (_capActive && _pomodoro.Phase == PomodoroPhase.Work)
        {
            _mood.Text = "⏳";
            _message.Text = $"You've been focused a while — a short break helps it stick.";
            _action.Content = "Take a break";
            _action.Background = accent;
            _action.Visibility = Visibility.Visible;
            _action.Tag = "break";
            WireAction();
            return;
        }

        switch (_pomodoro.Phase)
        {
            case PomodoroPhase.Work:
                _mood.Text = "🎯";
                _message.Text = "Locked in — I'm right here with you.";
                if (_shieldEnabled && _distractions > 0)
                {
                    _sub.Text = $"Switched away {_distractions}× — come back when you're ready 💚";
                    _sub.Visibility = Visibility.Visible;
                }
                break;

            case PomodoroPhase.ShortBreak:
            case PomodoroPhase.LongBreak:
                _mood.Text = "🌿";
                _message.Text = _momentum.Streak > 0
                    ? $"Nice block! That's {_momentum.Streak} day{(_momentum.Streak == 1 ? "" : "s")} in a row."
                    : "Nice block — that counts.";
                if (_windDownEnabled)
                {
                    _sub.Text = "Wind down: stretch · water · note where you stopped.";
                    _sub.Visibility = Visibility.Visible;
                }
                _action.Content = "Back to it";
                _action.Background = accent;
                _action.Visibility = Visibility.Visible;
                _action.Tag = "resume";
                WireAction();
                break;

            default: // Idle
                _mood.Text = "🙂";
                _message.Text = "Ready when you are. Start small.";
                break;
        }
    }

    private void WireAction()
    {
        _action.Click -= OnActionClick;
        _action.Click += OnActionClick;
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        switch (_action.Tag as string)
        {
            case "break": _pomodoro.Skip(); break;   // end the work block, go to the break
            case "resume": _pomodoro.Skip(); break;  // end the break, back to focus
        }
        RefreshUi();
    }

    private Brush Brush(string token, Color fallback)
        => Application.Current?.TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
