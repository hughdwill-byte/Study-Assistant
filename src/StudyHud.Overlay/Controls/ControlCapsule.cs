using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// The small persistent control capsule (spec §23, §162): a themed pill with a drag grip, three
/// quick-action buttons (Question Finder, toggle HUD, Note Taking) and the current workspace short-code.
/// Themed via the same semantic tokens as the panels. Actions use application state only — no new
/// behaviour or dependencies.
/// </summary>
public sealed class ControlCapsule : UserControl
{
    private readonly IApplicationStateService _appState;
    private readonly IAssessmentPolicyService _policy;

    private TextBlock _codeLabel = null!;
    private Border _assessmentBadge = null!;

    public ControlCapsule(IApplicationStateService appState, IAssessmentPolicyService policy)
    {
        _appState = appState;
        _policy = policy;

        MinWidth = 220;
        Height = 44;
        SnapsToDevicePixels = true;
        FocusVisualStyle = null;

        BuildVisualTree();
        Update(_appState.Current);

        _appState.StateChanged += OnStateChanged;
        Unloaded += (_, _) => _appState.StateChanged -= OnStateChanged;
    }

    private void BuildVisualTree()
    {
        var radius = TryFindResource("CornerRadius") is CornerRadius cr && cr.TopLeft > 0
            ? new CornerRadius(22) : new CornerRadius(22);

        var border = new Border
        {
            CornerRadius = radius,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 0, 12, 0),
            Background = Brush("SurfaceBackground", Color.FromArgb(210, 22, 22, 26)),
            BorderBrush = Brush("PanelBorder", Color.FromArgb(100, 255, 255, 255)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 22, ShadowDepth = 8, Direction = 270, Color = Colors.Black, Opacity = 0.45
            }
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        panel.Children.Add(new TextBlock
        {
            Text = "≡", FontSize = 15, Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("SecondaryText", Colors.Gray)
        });
        panel.Children.Add(Divider());
        panel.Children.Add(GlyphButton("▣", "Question Finder", () => _ = _appState.SwitchWorkspaceAsync(WorkspaceId.QuestionFinder)));
        panel.Children.Add(GlyphButton("◐", "Show / hide the HUD", () => _appState.SetHudVisible(!_appState.Current.HudVisible)));
        panel.Children.Add(GlyphButton("⧉", "Note Taking", () => _ = _appState.SwitchWorkspaceAsync(WorkspaceId.NoteTaking)));
        panel.Children.Add(Divider());

        _codeLabel = new TextBlock
        {
            FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("PrimaryText", Colors.White)
        };
        panel.Children.Add(_codeLabel);

        _assessmentBadge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(0xC8, 0x3C, 0x28)),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = new TextBlock { Text = "NON-AI", FontSize = 8, Foreground = Brushes.White, FontWeight = FontWeights.Bold }
        };
        panel.Children.Add(_assessmentBadge);

        border.Child = panel;
        Content = border;
    }

    private UIElement Divider() => new Border
    {
        Width = 1, Height = 20, Margin = new Thickness(6, 0, 6, 0),
        Background = Brush("PanelBorder", Color.FromArgb(90, 255, 255, 255)),
        VerticalAlignment = VerticalAlignment.Center
    };

    private Button GlyphButton(string glyph, string tip, Action action)
    {
        var btn = new Button
        {
            Content = glyph,
            Width = 26, Height = 26, FontSize = 14,
            Margin = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush("Accent", Color.FromRgb(0, 180, 255)),
            Cursor = Cursors.Hand,
            ToolTip = tip
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    private void OnStateChanged(object? sender, ApplicationStateChangedEventArgs e)
        => Dispatcher.BeginInvoke(() => Update(e.Current));

    private void Update(ApplicationState state)
    {
        _codeLabel.Text = state.CurrentWorkspace switch
        {
            WorkspaceId.NoteTaking => "NOTES",
            WorkspaceId.QuestionFinder => "FINDER",
            _ => state.CurrentWorkspace.ToString().ToUpperInvariant()
        };
        _assessmentBadge.Visibility = state.AssessmentModeActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
