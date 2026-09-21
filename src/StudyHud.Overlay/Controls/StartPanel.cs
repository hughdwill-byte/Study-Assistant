using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// Task-initiation panel for ADHD Mode. Its only job is beating the activation barrier: name one tiny
/// next step, then a big "5-minute start" (permission to stop, which paradoxically gets you going) and
/// a one-tap ritual that starts focus and opens your cheat sheet at once. Micro-step + artificial
/// urgency + zero decisions — the three evidence-based initiation levers.
/// </summary>
public sealed class StartPanel : HudPanelBase
{
    private readonly PomodoroService _pomodoro;
    private readonly ISettingsStore _settings;
    private readonly IApplicationStateService _state;

    private TextBox _step = null!;
    private int _quickMinutes = 5;

    public StartPanel(IApplicationStateService appState, IThemeService theme,
        PomodoroService pomodoro, ISettingsStore settings)
        : base("start-panel", appState, theme)
    {
        _pomodoro = pomodoro;
        _settings = settings;
        _state = appState;
        MinWidth = 240;
        MinHeight = 190;
        Width = 300;
        Height = 260;
    }

    protected override string PanelTitle => "Start";

    protected override void PopulateContent(Grid contentGrid)
    {
        var stack = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };

        stack.Children.Add(new TextBlock
        {
            Text = "Your next tiny step",
            FontSize = 11, Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 0, 0, 4)
        });

        _step = new TextBox
        {
            MinHeight = 30, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _step.LostFocus += (_, _) => _ = _settings.UpdateAsync(s => s with { StartNextStep = _step.Text });
        stack.Children.Add(_step);

        var start5 = MakeButton($"▶  Start {_quickMinutes} min", accent: true);
        start5.FontSize = 15;
        start5.Padding = new Thickness(14, 8, 14, 8);
        start5.Click += (_, _) => _pomodoro.StartQuick(_quickMinutes);
        stack.Children.Add(start5);

        var ritual = MakeButton("▶  Focus + open my notes", accent: false);
        ritual.Margin = new Thickness(0, 8, 0, 0);
        ritual.Click += (_, _) =>
        {
            _state.SetCheatSheetVisible(true);
            if (_pomodoro.Phase == PomodoroPhase.Idle) _pomodoro.Toggle();
        };
        stack.Children.Add(ritual);

        stack.Children.Add(new TextBlock
        {
            Text = "You can stop after 5 minutes. Starting is the whole win.",
            FontSize = 10, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 10, 0, 0)
        });

        contentGrid.Children.Add(stack);

        Loaded += async (_, _) =>
        {
            try
            {
                var s = await _settings.LoadAsync();
                _quickMinutes = s.QuickStartMinutes > 0 ? s.QuickStartMinutes : 5;
                start5.Content = $"▶  Start {_quickMinutes} min";
                if (!string.IsNullOrEmpty(s.StartNextStep)) _step.Text = s.StartNextStep;
            }
            catch { /* defaults are fine */ }
        };
    }

    private Button MakeButton(string content, bool accent) => new()
    {
        Content = content,
        Padding = new Thickness(12, 6, 12, 6),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Cursor = System.Windows.Input.Cursors.Hand,
        BorderThickness = new Thickness(accent ? 0 : 1),
        BorderBrush = Brush("Accent", Color.FromRgb(90, 178, 168)),
        Foreground = accent ? Brushes.White : Brush("PrimaryText", Colors.White),
        Background = accent ? Brush("Accent", Color.FromRgb(90, 178, 168)) : Brushes.Transparent
    };

    private Brush Brush(string token, Color fallback)
        => Application.Current?.TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
