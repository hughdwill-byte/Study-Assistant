using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Overlay.Controls;

/// <summary>
/// Base class for all HUD panels (spec §8, §10, §130).
/// Handles Ghost/Active/Edit visual state, drag, resize, and responsive layout.
///
/// Ghost:  panel is visible but hit-testing is disabled (WS_EX_TRANSPARENT handles
///         the window level; this class disables WPF hit-testing on its content).
/// Active: full interaction enabled.
/// Edit:   drag handles and resize grips appear; panel can be repositioned.
/// </summary>
public abstract class HudPanelBase : UserControl
{
    private readonly IApplicationStateService _appState;
    private readonly IThemeService _theme;

    // Drag state
    private bool _isDragging;
    private Point _dragStart;
    private Point _panelStartPos;

    // Panel identity
    public string PanelId { get; }

    protected HudPanelBase(string panelId, IApplicationStateService appState, IThemeService theme)
    {
        PanelId = panelId;
        _appState = appState;
        _theme = theme;

        // WPF visual setup
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        FocusVisualStyle = null;
        Focusable = false;

        BuildBaseVisualTree();
        ApplyCurrentState(_appState.Current);

        _appState.StateChanged += OnStateChanged;
        _theme.ThemeChanged += OnThemeChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ── Visual tree ──────────────────────────────────────────────────────────

    protected Border OuterBorder { get; private set; } = null!;
    protected Grid ContentGrid { get; private set; } = null!;
    protected Border EditHandleBar { get; private set; } = null!;
    protected Grid ResizeGrips { get; private set; } = null!;

    private void BuildBaseVisualTree()
    {
        OuterBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            ClipToBounds = true
        };

        // Edit mode drag handle at top
        EditHandleBar = new Border
        {
            Height = 20,
            Cursor = Cursors.SizeAll,
            Background = (Brush)(Application.Current.TryFindResource("Accent") ?? Brushes.DodgerBlue),
            Visibility = Visibility.Collapsed
        };
        var dragLabel = new TextBlock
        {
            Text = "≡ " + PanelTitle,
            Foreground = Brushes.White,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            IsHitTestVisible = false
        };

        // Close (✕) box — hides this panel. Reachable in Edit mode where the handle bar shows.
        var closeButton = new Button
        {
            Content = "✕",
            Width = 20,
            Height = 20,
            FontSize = 11,
            Padding = new Thickness(0),
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Hide this panel"
        };
        closeButton.Click += OnClosePanel;

        var barLayout = new DockPanel();
        DockPanel.SetDock(closeButton, Dock.Right);
        barLayout.Children.Add(closeButton);
        barLayout.Children.Add(dragLabel);
        EditHandleBar.Child = barLayout;

        EditHandleBar.MouseLeftButtonDown += OnDragStart;
        EditHandleBar.MouseMove += OnDragMove;
        EditHandleBar.MouseLeftButtonUp += OnDragEnd;

        // Main content placeholder — subclasses fill this
        ContentGrid = new Grid();

        // Stack: handle bar + content
        var stack = new DockPanel();
        DockPanel.SetDock(EditHandleBar, Dock.Top);
        stack.Children.Add(EditHandleBar);
        stack.Children.Add(ContentGrid);

        // Resize grips (SE corner)
        ResizeGrips = new Grid { Visibility = Visibility.Collapsed };
        var seGrip = new Border
        {
            Width = 10, Height = 10,
            Background = (Brush)(Application.Current.TryFindResource("Accent") ?? Brushes.DodgerBlue),
            CornerRadius = new CornerRadius(0, 0, 4, 0),
            Cursor = Cursors.SizeNWSE,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        seGrip.MouseLeftButtonDown += OnResizeStart;
        seGrip.MouseMove += OnResizeMove;
        seGrip.MouseLeftButtonUp += OnResizeEnd;
        ResizeGrips.Children.Add(seGrip);

        // Overlay panel = content stack + resize grips
        var overlay = new Grid();
        overlay.Children.Add(stack);
        overlay.Children.Add(ResizeGrips);

        OuterBorder.Child = overlay;
        Content = OuterBorder;

        // Let subclass populate ContentGrid
        PopulateContent(ContentGrid);
    }

    /// <summary>Subclasses implement this to fill the panel content area.</summary>
    protected abstract void PopulateContent(Grid contentGrid);

    /// <summary>Human-readable panel title shown in the edit drag bar.</summary>
    protected abstract string PanelTitle { get; }

    /// <summary>Called when the panel should update its responsive layout.</summary>
    protected virtual void OnResponsiveLayoutChanged(PanelResponsiveState state) { }

    // ── State changes ────────────────────────────────────────────────────────

    private void OnStateChanged(object? sender, ApplicationStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => ApplyCurrentState(e.Current));
    }

    private void ApplyCurrentState(ApplicationState state)
    {
        var interactionState = state.HudInteractionState;
        bool hudVisible = state.HudVisible;

        Visibility = hudVisible ? Visibility.Visible : Visibility.Collapsed;
        if (!hudVisible) return;

        switch (interactionState)
        {
            case HudInteractionState.Ghost:
                IsHitTestVisible = false;
                OuterBorder.Opacity = 0.85;
                EditHandleBar.Visibility = Visibility.Collapsed;
                ResizeGrips.Visibility = Visibility.Collapsed;
                break;

            case HudInteractionState.Active:
                IsHitTestVisible = true;
                OuterBorder.Opacity = 1.0;
                EditHandleBar.Visibility = Visibility.Collapsed;
                ResizeGrips.Visibility = Visibility.Collapsed;
                break;

            case HudInteractionState.Edit:
                IsHitTestVisible = true;
                OuterBorder.Opacity = 1.0;
                EditHandleBar.Visibility = Visibility.Visible;
                ResizeGrips.Visibility = Visibility.Visible;
                break;
        }

        UpdateThemeResources();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateThemeResources);
    }

    private void UpdateThemeResources()
    {
        var bg = Application.Current.TryFindResource("PanelBackground") as Brush
                 ?? new SolidColorBrush(Color.FromArgb(220, 22, 22, 26));
        var border = Application.Current.TryFindResource("PanelBorder") as Brush
                     ?? new SolidColorBrush(Color.FromRgb(60, 60, 70));

        OuterBorder.Background = bg;
        OuterBorder.BorderBrush = border;
    }

    // ── Drag (Edit Mode) ─────────────────────────────────────────────────────

    /// <summary>Hides this panel. It reappears next launch (a hide, not a permanent removal).</summary>
    private void OnClosePanel(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return; // clicking the ✕ close box must not begin a drag
        if (_appState.Current.HudInteractionState != HudInteractionState.Edit) return;
        _isDragging = true;
        _dragStart = e.GetPosition(null);
        var parent = Parent as Canvas ?? VisualTreeHelper.GetParent(this) as Canvas;
        _panelStartPos = parent != null
            ? new Point(Canvas.GetLeft(this), Canvas.GetTop(this))
            : new Point(0, 0);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var current = e.GetPosition(null);
        var delta = current - _dragStart;
        var parent = Parent as Canvas ?? VisualTreeHelper.GetParent(this) as Canvas;
        if (parent != null)
        {
            double newLeft = Math.Max(0, _panelStartPos.X + delta.X);
            double newTop = Math.Max(0, _panelStartPos.Y + delta.Y);
            Canvas.SetLeft(this, newLeft);
            Canvas.SetTop(this, newTop);
        }
        e.Handled = true;
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    // ── Resize (Edit Mode) ───────────────────────────────────────────────────

    private bool _isResizing;
    private Point _resizeStart;
    private Size _resizeStartSize;

    private void OnResizeStart(object sender, MouseButtonEventArgs e)
    {
        _isResizing = true;
        _resizeStart = e.GetPosition(null);
        _resizeStartSize = new Size(ActualWidth, ActualHeight);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnResizeMove(object sender, MouseEventArgs e)
    {
        if (!_isResizing) return;
        var current = e.GetPosition(null);
        var delta = current - _resizeStart;
        double newWidth = Math.Max(MinWidth > 0 ? MinWidth : 120, _resizeStartSize.Width + delta.X);
        double newHeight = Math.Max(MinHeight > 0 ? MinHeight : 60, _resizeStartSize.Height + delta.Y);
        Width = newWidth;
        Height = newHeight;

        // Update responsive state
        var responsive = newWidth < 200 ? PanelResponsiveState.Compact
                       : newWidth > 400 ? PanelResponsiveState.Expanded
                       : PanelResponsiveState.Normal;
        OnResponsiveLayoutChanged(responsive);

        e.Handled = true;
    }

    private void OnResizeEnd(object sender, MouseButtonEventArgs e)
    {
        _isResizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateThemeResources();
        ApplyCurrentState(_appState.Current);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _appState.StateChanged -= OnStateChanged;
        _theme.ThemeChanged -= OnThemeChanged;
    }
}
