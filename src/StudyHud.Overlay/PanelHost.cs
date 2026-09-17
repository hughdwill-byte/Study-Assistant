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
    private readonly ICaptureService _capture;
    private readonly IQuestionFinder _finder;
    private readonly IAssessmentPolicyService _policy;
    private readonly PomodoroService _pomodoro;
    private readonly INotionPageReader _notionReader;
    private readonly ISettingsStore _settings;

    private readonly List<HudPanelBase> _panels = [];
    private ControlCapsule? _capsule;
    private FocusTimerPanel? _focusPanel;
    private CheatSheetPanel? _cheatPanel;

    /// <summary>The monitor this host renders panels for.</summary>
    public string MonitorId => _monitor.MonitorId;

    public PanelHost(
        MonitorInfo monitor,
        IApplicationStateService appState,
        IThemeService theme,
        ICaptureService capture,
        IQuestionFinder finder,
        IAssessmentPolicyService policy,
        PomodoroService pomodoro,
        INotionPageReader notionReader,
        ISettingsStore settings)
    {
        _monitor = monitor;
        _appState = appState;
        _theme = theme;
        _capture = capture;
        _finder = finder;
        _policy = policy;
        _pomodoro = pomodoro;
        _notionReader = notionReader;
        _settings = settings;

        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;

        _appState.StateChanged += OnStateChanged;
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _appState.StateChanged -= OnStateChanged;
            _pomodoro.PhaseChanged -= OnPomodoroPhaseChanged;
        };
    }

    private bool _populated;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The HUD lives on exactly one monitor (this host's), so always populate it — do it once.
        if (_populated) return;
        _populated = true;
        PopulatePanels();
    }

    private void PopulatePanels()
    {
        // Control capsule — bottom-right corner
        _capsule = new ControlCapsule(_appState, _policy);
        Canvas.SetRight(_capsule, 16);
        Canvas.SetBottom(_capsule, 16);
        Children.Add(_capsule);

        // Floating Focus-Mode timer — workspace-independent; shown only while a session is active,
        // so starting the timer (from here or the settings Focus page) pops it up as a HUD box.
        _focusPanel = new FocusTimerPanel(_appState, _theme, _pomodoro);
        Canvas.SetLeft(_focusPanel, 16);
        Canvas.SetTop(_focusPanel, 16);
        _pomodoro.PhaseChanged += OnPomodoroPhaseChanged;
        UpdateFocusPanelPresence();

        // Optional Cheat Sheet panel — workspace-independent; shown only when the user enables it
        // (from the control capsule) so it can be pinned to a Notion page as a topic/test reference.
        _cheatPanel = new CheatSheetPanel(_appState, _theme, _notionReader, _policy, _settings);
        Canvas.SetRight(_cheatPanel, 16);
        Canvas.SetTop(_cheatPanel, 16);
        UpdateCheatPanelPresence();

        // Default panel layout based on current workspace
        SwitchWorkspacePanels(_appState.Current.CurrentWorkspace);
    }

    /// <summary>Adds the Cheat Sheet panel while it is enabled (or while calibrating), removes it otherwise.</summary>
    private void UpdateCheatPanelPresence()
    {
        if (_cheatPanel == null) return;
        bool active = _appState.Current.CheatSheetVisible
                      || _appState.Current.HudInteractionState == HudInteractionState.Edit;
        bool present = Children.Contains(_cheatPanel);
        if (active && !present) Children.Add(_cheatPanel);
        else if (!active && present) Children.Remove(_cheatPanel);
    }

    private void OnPomodoroPhaseChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(UpdateFocusPanelPresence);

    /// <summary>Adds the Focus timer panel to the canvas while a session runs; removes it when idle.</summary>
    private void UpdateFocusPanelPresence()
    {
        if (_focusPanel == null) return;
        bool active = _pomodoro.Phase != PomodoroPhase.Idle
                      || _appState.Current.HudInteractionState == HudInteractionState.Edit;
        bool present = Children.Contains(_focusPanel);
        if (active && !present) Children.Add(_focusPanel);
        else if (!active && present) Children.Remove(_focusPanel);
    }

    private void OnStateChanged(object? sender, ApplicationStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (e.Previous.CurrentWorkspace != e.Current.CurrentWorkspace)
                SwitchWorkspacePanels(e.Current.CurrentWorkspace);

            // Entering/leaving Calibrate (Edit) shows/hides the Focus + Cheat Sheet panels so they can
            // be positioned even when not otherwise shown.
            if (e.Previous.HudInteractionState != e.Current.HudInteractionState)
            {
                UpdateFocusPanelPresence();
                UpdateCheatPanelPresence();
            }

            // The control capsule toggles the optional Cheat Sheet panel on/off.
            if (e.Previous.CheatSheetVisible != e.Current.CheatSheetVisible)
                UpdateCheatPanelPresence();

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
                AddPanel(new QuestionFinderPanel(_appState, _theme, _capture, _finder), left: 16, top: 80);
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
