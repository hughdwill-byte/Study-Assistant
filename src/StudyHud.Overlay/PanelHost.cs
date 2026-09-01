using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Overlay.Controls;
using StudyHud.Theming;

namespace StudyHud.Overlay;

/// <summary>
/// Canvas that hosts HUD panels inside a MonitorOverlayWindow (spec §4, §8).
/// Panels are positioned absolutely using Canvas.Left/Top.
/// In Edit Mode: panels show drag handles.
/// Layout is saved/restored via ILayoutService.
/// </summary>
public sealed class PanelHost : Canvas
{
    private readonly MonitorInfo _monitor;
    private readonly IApplicationStateService _appState;
    private readonly IThemeService _theme;
    private readonly ISearchIndex _searchIndex;
    private readonly IAssessmentPolicyService _policy;

    private readonly List<HudPanelBase> _panels = [];
    private ControlCapsule? _capsule;

    /// <summary>The monitor this host renders panels for.</summary>
    public string MonitorId => _monitor.MonitorId;

    public PanelHost(
        MonitorInfo monitor,
        IApplicationStateService appState,
        IThemeService theme,
        ISearchIndex searchIndex,
        IAssessmentPolicyService policy)
    {
        _monitor = monitor;
        _appState = appState;
        _theme = theme;
        _searchIndex = searchIndex;
        _policy = policy;

        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;

        _appState.StateChanged += OnStateChanged;
        Loaded += OnLoaded;
        Unloaded += (_, _) => _appState.StateChanged -= OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Only populate the primary monitor or the first monitor with panels
        if (_monitor.IsPrimary)
            PopulatePanels();
    }

    private void PopulatePanels()
    {
        // Control capsule — bottom-right corner
        _capsule = new ControlCapsule(_appState, _policy);
        Canvas.SetRight(_capsule, 16);
        Canvas.SetBottom(_capsule, 16);
        Children.Add(_capsule);

        // Default panel layout based on current workspace
        SwitchWorkspacePanels(_appState.Current.CurrentWorkspace);
    }

    private void OnStateChanged(object? sender, ApplicationStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (e.Previous.CurrentWorkspace != e.Current.CurrentWorkspace)
                SwitchWorkspacePanels(e.Current.CurrentWorkspace);

            // In Ghost mode the whole window is WS_EX_TRANSPARENT — no hit testing needed here.
            // In Active/Edit mode the window is interactive.
        });
    }

    private void SwitchWorkspacePanels(WorkspaceId workspace)
    {
        // Remove existing panels (but keep capsule)
        foreach (var p in _panels.ToList())
            Children.Remove(p);
        _panels.Clear();

        switch (workspace)
        {
            case WorkspaceId.NoteTaking:
                AddPanel(new MacroPanel(_appState, _theme), left: 16, top: 100);
                break;

            case WorkspaceId.QuestionFinder:
                AddPanel(new QuestionFinderPanel(_appState, _theme, _searchIndex), left: 16, top: 80);
                break;
        }
    }

    private void AddPanel(HudPanelBase panel, double left, double top)
    {
        Canvas.SetLeft(panel, left);
        Canvas.SetTop(panel, top);
        Children.Add(panel);
        _panels.Add(panel);
    }

    /// <summary>
    /// Saves the current panel positions (called before workspace switch or app exit).
    /// </summary>
    public IReadOnlyList<PanelLayout> GetCurrentLayouts(WorkspaceId workspace)
    {
        var layouts = new List<PanelLayout>();
        double canvasW = ActualWidth > 0 ? ActualWidth : 1;
        double canvasH = ActualHeight > 0 ? ActualHeight : 1;

        foreach (var panel in _panels)
        {
            double left = Canvas.GetLeft(panel);
            double top = Canvas.GetTop(panel);
            double w = panel.ActualWidth > 0 ? panel.ActualWidth : panel.Width;
            double h = panel.ActualHeight > 0 ? panel.ActualHeight : panel.Height;

            layouts.Add(new PanelLayout
            {
                PanelId = panel.PanelId,
                Workspace = workspace,
                MonitorId = _monitor.MonitorId,
                NormalizedPosition = new NormalizedRect(
                    left / canvasW, top / canvasH,
                    (left + w) / canvasW, (top + h) / canvasH),
                LogicalWidth = w,
                LogicalHeight = h
            });
        }
        return layouts;
    }

    /// <summary>
    /// Restores panel positions from saved layouts.
    /// </summary>
    public void ApplyLayouts(IReadOnlyList<PanelLayout> layouts)
    {
        double canvasW = ActualWidth > 0 ? ActualWidth : 1920;
        double canvasH = ActualHeight > 0 ? ActualHeight : 1080;

        foreach (var layout in layouts)
        {
            var panel = _panels.FirstOrDefault(p => p.PanelId == layout.PanelId);
            if (panel == null) continue;

            Canvas.SetLeft(panel, layout.NormalizedPosition.Left * canvasW);
            Canvas.SetTop(panel, layout.NormalizedPosition.Top * canvasH);

            if (layout.LogicalWidth > 0) panel.Width = layout.LogicalWidth;
            if (layout.LogicalHeight > 0) panel.Height = layout.LogicalHeight;
        }
    }
}
