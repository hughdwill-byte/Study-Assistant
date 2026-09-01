using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// The small persistent control capsule (spec §23, §162).
/// Always visible even in Ghost mode (uses its own interactive island HWND in production;
/// in Phase 2 it's embedded in the overlay window and becomes interactive with the HUD).
/// Shows: [workspace] [course] [assessment status]
/// </summary>
public sealed class ControlCapsule : UserControl
{
    private readonly IApplicationStateService _appState;
    private readonly IAssessmentPolicyService _policy;

    private TextBlock _workspaceLabel = null!;
    private TextBlock _courseLabel = null!;
    private Border _assessmentBadge = null!;

    public ControlCapsule(IApplicationStateService appState, IAssessmentPolicyService policy)
    {
        _appState = appState;
        _policy = policy;

        MinWidth = 160;
        Height = 28;
        SnapsToDevicePixels = true;
        FocusVisualStyle = null;

        BuildVisualTree();
        Update(_appState.Current);

        _appState.StateChanged += OnStateChanged;
        Unloaded += (_, _) => _appState.StateChanged -= OnStateChanged;
    }

    private void BuildVisualTree()
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 4, 10, 4),
            Background = new SolidColorBrush(Color.FromArgb(210, 22, 22, 26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255))
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        _workspaceLabel = new TextBlock
        {
            FontSize = 10.5,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        var separator = new TextBlock
        {
            Text = "  |  ",
            FontSize = 10.5,
            Opacity = 0.3,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        _courseLabel = new TextBlock
        {
            FontSize = 10.5,
            Opacity = 0.7,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        _assessmentBadge = new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(200, 60, 40)),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "NON-AI",
                FontSize = 8,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            }
        };

        panel.Children.Add(_workspaceLabel);
        panel.Children.Add(separator);
        panel.Children.Add(_courseLabel);
        panel.Children.Add(_assessmentBadge);

        border.Child = panel;
        Content = border;
    }

    private void OnStateChanged(object? sender, ApplicationStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => Update(e.Current));
    }

    private void Update(ApplicationState state)
    {
        _workspaceLabel.Text = state.CurrentWorkspace switch
        {
            WorkspaceId.NoteTaking => "📝 Notes",
            WorkspaceId.QuestionFinder => "🔍 Question Finder",
            _ => state.CurrentWorkspace.ToString()
        };

        _courseLabel.Text = state.CurrentCourseId ?? "No course";
        _assessmentBadge.Visibility = state.AssessmentModeActive
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
