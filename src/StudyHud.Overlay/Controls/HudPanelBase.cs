using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
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

    // Silhouette shaping: content is clipped to a shape that scales with the panel; the rim stroke and
    // resize grip live OUTSIDE the clip so the outline draws fully and the grip stays reachable.
    private enum Silhouette { Rounded, Pint, Chamfer }
    private Silhouette _shape = Silhouette.Rounded;
    private Grid _clipHost = null!;
    private double _clipRadius;
    private System.Windows.Shapes.Path? _rimPath;

    private void BuildBaseVisualTree()
    {
        OuterBorder = new Border
        {
            CornerRadius = Res("CornerRadius") is CornerRadius cr ? cr : new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            ClipToBounds = true
        };
        // Every depth theme floats the panel with a drop shadow (shadow only — never a blur on a
        // Border with text children). One Effect per element, so this is the panel's single Effect.
        OuterBorder.Effect =
            Res("FrostedGlass") is true ? Shadow(48, 20, Color.FromRgb(4, 14, 42), 0.5)
            : Res("CrtBezel") is true ? Shadow(44, 12, Colors.Black, 0.55)
            : Res("MfdFrame") is true ? Shadow(30, 18, Color.FromRgb(0, 8, 20), 0.6)
            : Res("PintSilhouette") is true ? Shadow(26, 16, Color.FromRgb(60, 34, 4), 0.45)
            : null;

        // Panel header / drag handle at top. Always shown (spec: the header bar is part of the design);
        // dragging still only acts in Edit mode.
        EditHandleBar = new Border
        {
            Height = 22,
            Cursor = Cursors.SizeAll,
            Background = (Brush)(Application.Current.TryFindResource("Accent") ?? Brushes.DodgerBlue)
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

        // Beer: let the foam head behind the header show through, with dark text on the foam.
        if (Res("PintSilhouette") is true)
        {
            EditHandleBar.Background = Brushes.Transparent;
            var dark = Res("PrimaryText") as Brush ?? Brushes.Black;
            dragLabel.Foreground = dark;
            closeButton.Foreground = dark;
        }

        // Dragging works from anywhere on the panel in Edit mode (not just the thin handle bar),
        // so it's easy to grab. Clicks on buttons/inputs are excluded in OnDragStart.
        OuterBorder.MouseLeftButtonDown += OnDragStart;
        OuterBorder.MouseMove += OnDragMove;
        OuterBorder.MouseLeftButtonUp += OnDragEnd;

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
            Width = 18, Height = 18,
            Background = (Brush)(Application.Current.TryFindResource("Accent") ?? Brushes.DodgerBlue),
            CornerRadius = new CornerRadius(0, 0, 4, 0),
            Cursor = Cursors.SizeNWSE,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            ToolTip = "Drag to resize"
        };
        seGrip.MouseLeftButtonDown += OnResizeStart;
        seGrip.MouseMove += OnResizeMove;
        seGrip.MouseLeftButtonUp += OnResizeEnd;
        ResizeGrips.Children.Add(seGrip);

        // Everything that should take the panel's silhouette goes in _clipHost (fill + content +
        // decorative overlays). The rim stroke and resize grip go OUTSIDE it.
        _shape = Res("PintSilhouette") is true ? Silhouette.Pint
               : Res("MfdFrame") is true ? Silhouette.Chamfer
               : Silhouette.Rounded;
        _clipRadius = Res("CornerRadius") is CornerRadius rcr ? rcr.TopLeft : 6;

        _clipHost = new Grid();
        // Background depth layers sit UNDER the content.
        if (Res("GlassSlab") is true) { _clipHost.Children.Add(BuildInnerBloom()); _clipHost.Children.Add(BuildCornerRefraction()); }
        if (Res("PintSilhouette") is true) _clipHost.Children.Add(BuildFoamHead());
        _clipHost.Children.Add(stack);
        // Foreground depth layers sit OVER the content (all non-hit-testable).
        if (Res("GlassSheen") is true) _clipHost.Children.Add(BuildGlassSheen());
        if (Res("PintSilhouette") is true) _clipHost.Children.Add(BuildBaseShade());
        if (Res("MfdFrame") is true) { _clipHost.Children.Add(BuildTopLight()); _clipHost.Children.Add(BuildHudTicks()); }
        if (Res("CrtBezel") is true) { _clipHost.Children.Add(BuildVignette()); _clipHost.Children.Add(BuildGlassReflection()); }
        if (Res("GlassSlab") is true) { _clipHost.Children.Add(BuildBottomHairline()); }
        if (Res("FrostedGlass") is true) _clipHost.Children.Add(BuildSpecularLine());
        if (Res("PanelScanlines") is true) _clipHost.Children.Add(BuildScanline());
        if (Res("PanelCornerBrackets") is true) _clipHost.Children.Add(BuildCornerBrackets(13));

        var overlay = new Grid();
        overlay.Children.Add(_clipHost);

        // Rim stroke follows the silhouette outline (Pint = white rim, Chamfer = cyan edge).
        if (_shape != Silhouette.Rounded)
        {
            _rimPath = new System.Windows.Shapes.Path
            {
                Fill = null,
                Stroke = _shape == Silhouette.Pint
                    ? new SolidColorBrush(Color.FromArgb(153, 255, 255, 255))
                    : (Res("PanelBorder") as Brush ?? new SolidColorBrush(Color.FromArgb(115, 55, 224, 255))),
                StrokeThickness = _shape == Silhouette.Pint ? 3 : 1.5,
                IsHitTestVisible = false
            };
            overlay.Children.Add(_rimPath);
        }

        // Retro screws / Space rivets sit on the frame, above the clip.
        if (Res("CrtBezel") is true) overlay.Children.Add(BuildCornerDots(7, ScrewBrush(), 8));
        else if (Res("MfdFrame") is true) overlay.Children.Add(BuildCornerDots(4, RivetBrush(), 7));

        overlay.Children.Add(ResizeGrips);

        OuterBorder.Child = overlay;
        Content = OuterBorder;

        SizeChanged += (_, _) => UpdateSilhouette();

        // Let subclass populate ContentGrid
        PopulateContent(ContentGrid);
    }

    private static object? Res(string key) => Application.Current?.TryFindResource(key);

    /// <summary>Accent brush for the retro overlays, falling back to a hot amber.</summary>
    private static Brush AccentBrush() =>
        Res("Accent") as Brush ?? new SolidColorBrush(Color.FromRgb(255, 122, 26));

    /// <summary>Beer: the vertical amber "liquid" gradient used to fill panels.</summary>
    private static Brush LiquidBrush()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xF3, 0xBB, 0x3C), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xE5, 0xA3, 0x20), 0.32));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xC4, 0x80, 0x0F), 0.70));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xA2, 0x65, 0x0A), 1.0));
        return g;
    }

    /// <summary>
    /// Beer: a wet-glass sheen — a bright specular strip down the left inset and a thinner refraction
    /// strip at the right edge. Non-hit-testable; only added when the theme sets <c>GlassSheen</c>.
    /// </summary>
    private static Grid BuildGlassSheen()
    {
        var grid = new Grid { IsHitTestVisible = false };

        var left = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        left.GradientStops.Add(new GradientStop(Color.FromArgb(140, 255, 255, 255), 0.0));
        left.GradientStops.Add(new GradientStop(Color.FromArgb(12, 255, 255, 255), 1.0));
        grid.Children.Add(new Rectangle
        {
            Width = 24, HorizontalAlignment = HorizontalAlignment.Left, Fill = left, IsHitTestVisible = false
        });

        var right = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        right.GradientStops.Add(new GradientStop(Color.FromArgb(46, 120, 70, 10), 0.0));
        right.GradientStops.Add(new GradientStop(Color.FromArgb(76, 255, 255, 255), 1.0));
        grid.Children.Add(new Rectangle
        {
            Width = 12, HorizontalAlignment = HorizontalAlignment.Right, Fill = right, IsHitTestVisible = false
        });
        return grid;
    }

    /// <summary>LiquidGlass: the 165° frosted blue-glass fill layered over the flat panel tint.</summary>
    private static Brush GlassFillBrush()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.26, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(56, 255, 255, 255), 0.0));   // white 0.22
        g.GradientStops.Add(new GradientStop(Color.FromArgb(33, 96, 140, 220), 0.45));   // blue 0.13
        g.GradientStops.Add(new GradientStop(Color.FromArgb(140, 18, 38, 78), 1.0));     // navy 0.55
        return g;
    }

    /// <summary>
    /// LiquidGlass: a 1px specular highlight inset from each end of the panel's top edge. Non-hit-testable.
    /// </summary>
    private static Rectangle BuildSpecularLine()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.1));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(242, 255, 255, 255), 0.5));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.9));
        return new Rectangle
        {
            Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 2, 12, 0),
            Fill = b, IsHitTestVisible = false
        };
    }

    // ── Depth-theme overlays (all non-hit-testable) ──────────────────────────

    private static DropShadowEffect Shadow(double blur, double depth, Color color, double opacity) =>
        new() { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Color = color, Opacity = opacity };

    // ── Silhouette geometry (scales with the panel; rebuilt on SizeChanged) ──

    private void UpdateSilhouette()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 1 || h <= 1) return;
        var clip = ShapeGeometry(w, h);
        _clipHost.Clip = clip;
        if (_rimPath != null) _rimPath.Data = ShapeGeometry(w, h);
    }

    private Geometry ShapeGeometry(double w, double h) => _shape switch
    {
        Silhouette.Pint => PintGeometry(w, h),
        Silhouette.Chamfer => ChamferGeometry(w, h, 14),
        _ => new RectangleGeometry(new Rect(0, 0, w, h), _clipRadius, _clipRadius)
    };

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A tapered pint-glass outline, generated from the design's proportional control points.</summary>
    private static Geometry PintGeometry(double w, double h)
    {
        string d =
            $"M0,{N(.063 * h)} Q0,{N(.016 * h)} {N(.038 * w)},{N(.016 * h)} " +
            $"L{N(.962 * w)},{N(.016 * h)} Q{N(w)},{N(.016 * h)} {N(w)},{N(.063 * h)} " +
            $"C{N(w)},{N(.476 * h)} {N(.968 * w)},{N(.722 * h)} {N(.890 * w)},{N(.944 * h)} " +
            $"Q{N(.884 * w)},{N(.984 * h)} {N(.855 * w)},{N(.984 * h)} " +
            $"L{N(.145 * w)},{N(.984 * h)} Q{N(.116 * w)},{N(.984 * h)} {N(.110 * w)},{N(.944 * h)} " +
            $"C{N(.032 * w)},{N(.722 * h)} 0,{N(.476 * h)} 0,{N(.063 * h)} Z";
        return Geometry.Parse(d);
    }

    /// <summary>A chamfered rectangle (top-left, top-right, bottom-left corners cut) for the MFD frame.</summary>
    private static Geometry ChamferGeometry(double w, double h, double notch)
    {
        double n = Math.Min(notch, Math.Min(w, h) / 3);
        string d = $"M{N(n)},0 L{N(w - n)},0 L{N(w)},{N(n)} L{N(w)},{N(h)} L{N(n)},{N(h)} L0,{N(h - n)} L0,{N(n)} Z";
        return Geometry.Parse(d);
    }

    // ── Frame brushes / decorations ──────────────────────────────────────────

    private static Brush BezelBrush()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x4B, 0x44, 0x3D), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x33, 0x2D, 0x28), 0.46));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x1D, 0x18, 0x15), 1.0));
        return g;
    }

    private static Brush ScrewBrush()
    {
        var g = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.30), Center = new Point(0.5, 0.5), RadiusX = 0.6, RadiusY = 0.6 };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B, 0x83, 0x78), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x38, 0x32, 0x2C), 1.0));
        return g;
    }

    private static Brush ScreenWellBrush()
    {
        var g = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.75, RadiusY = 0.75 };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x1B, 0x12, 0x0C), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x0E, 0x0A, 0x08), 0.62));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x07, 0x05, 0x04), 1.0));
        return g;
    }

    private static Brush PlateBrush()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(240, 9, 28, 48), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(245, 6, 14, 26), 1.0));
        return g;
    }

    private static Brush RivetBrush()
    {
        var g = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.4, 0.35), RadiusX = 0.6, RadiusY = 0.6 };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x9F, 0xB6, 0xC6), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x2B, 0x3B, 0x4A), 1.0));
        return g;
    }

    /// <summary>Four small round dots (Retro screws / Space rivets) pinned near each corner.</summary>
    private static Grid BuildCornerDots(double size, Brush fill, double inset)
    {
        var grid = new Grid { IsHitTestVisible = false };
        foreach (var (h, v) in new[]
        {
            (HorizontalAlignment.Left, VerticalAlignment.Top), (HorizontalAlignment.Right, VerticalAlignment.Top),
            (HorizontalAlignment.Left, VerticalAlignment.Bottom), (HorizontalAlignment.Right, VerticalAlignment.Bottom)
        })
            grid.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = size, Height = size, HorizontalAlignment = h, VerticalAlignment = v,
                Margin = new Thickness(inset), Fill = fill, IsHitTestVisible = false
            });
        return grid;
    }

    /// <summary>Beer: a foam-white head band across the top of the glass (clipped to the pint shape).</summary>
    private static Border BuildFoamHead()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFE, 0xFA), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0xE6, 0xCD), 1.0));
        return new Border
        {
            Height = 30, VerticalAlignment = VerticalAlignment.Top, Background = g, IsHitTestVisible = false
        };
    }

    /// <summary>Retro: a radial vignette that darkens the panel edges for the recessed-CRT read.</summary>
    private static Rectangle BuildVignette()
    {
        var rg = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.78, RadiusY = 0.78 };
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.52));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(140, 0, 0, 0), 1.0));
        return new Rectangle { Fill = rg, IsHitTestVisible = false };
    }

    /// <summary>Retro: a faint diagonal glass reflection across the top-left of the screen.</summary>
    private static Rectangle BuildGlassReflection()
    {
        var lg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.7) };
        lg.GradientStops.Add(new GradientStop(Color.FromArgb(28, 255, 255, 255), 0.0));
        lg.GradientStops.Add(new GradientStop(Color.FromArgb(8, 255, 255, 255), 0.25));
        lg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.4));
        return new Rectangle { Fill = lg, IsHitTestVisible = false };
    }

    /// <summary>Space: a 1px top-lit edge highlight along the frame's top.</summary>
    private static Rectangle BuildTopLight()
    {
        var lg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        lg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 190, 225, 245), 0.0));
        lg.GradientStops.Add(new GradientStop(Color.FromArgb(191, 190, 225, 245), 0.5));
        lg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 190, 225, 245), 1.0));
        return new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 1, 2, 0), Fill = lg, IsHitTestVisible = false };
    }

    /// <summary>Space: thin cyan HUD-tick strips at the top and bottom of the plate.</summary>
    private static Grid BuildHudTicks()
    {
        DrawingBrush Ticks(byte a)
        {
            var line = new GeometryDrawing
            {
                Brush = new SolidColorBrush(Color.FromArgb(a, 55, 224, 255)),
                Geometry = new RectangleGeometry(new Rect(0, 0, 1, 5))
            };
            return new DrawingBrush(line) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 9, 5), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        }
        var grid = new Grid { IsHitTestVisible = false };
        grid.Children.Add(new Rectangle { Height = 5, VerticalAlignment = VerticalAlignment.Top, Fill = Ticks(115), IsHitTestVisible = false });
        grid.Children.Add(new Rectangle { Height = 5, VerticalAlignment = VerticalAlignment.Bottom, Fill = Ticks(77), IsHitTestVisible = false });
        return grid;
    }

    /// <summary>Beer: darker sediment shading at the base of the glass.</summary>
    private static Rectangle BuildBaseShade()
    {
        var lg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        lg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 90, 54, 4), 0.0));
        lg.GradientStops.Add(new GradientStop(Color.FromArgb(87, 90, 54, 4), 1.0));
        return new Rectangle { Height = 46, VerticalAlignment = VerticalAlignment.Bottom, Fill = lg, IsHitTestVisible = false };
    }

    /// <summary>LiquidGlass: refraction highlights pinned to the top-left and bottom-right corners.</summary>
    private static Grid BuildCornerRefraction()
    {
        Rectangle Corner(HorizontalAlignment h, VerticalAlignment v, byte a)
        {
            var rg = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(a, 255, 255, 255), 0.0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
            return new Rectangle { Width = 80, Height = 80, HorizontalAlignment = h, VerticalAlignment = v, Fill = rg, IsHitTestVisible = false };
        }
        var grid = new Grid { IsHitTestVisible = false };
        grid.Children.Add(Corner(HorizontalAlignment.Left, VerticalAlignment.Top, 87));
        grid.Children.Add(Corner(HorizontalAlignment.Right, VerticalAlignment.Bottom, 41));
        return grid;
    }

    /// <summary>LiquidGlass: a soft inner light bloom behind the content.</summary>
    private static Rectangle BuildInnerBloom()
    {
        var rg = new RadialGradientBrush { Center = new Point(0.5, 0.55), GradientOrigin = new Point(0.5, 0.55), RadiusX = 0.6, RadiusY = 0.6 };
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(46, 150, 190, 255), 0.0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 150, 190, 255), 1.0));
        return new Rectangle { Fill = rg, IsHitTestVisible = false };
    }

    /// <summary>LiquidGlass: a dark 1px hairline along the bottom edge that sells the glass thickness.</summary>
    private static Rectangle BuildBottomHairline() => new()
    {
        Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 1),
        Fill = new SolidColorBrush(Color.FromArgb(153, 4, 10, 26)), IsHitTestVisible = false
    };

    /// <summary>
    /// A non-hit-testable scanline overlay: a 3×3 tile painting one 3×1 faint-amber line, giving
    /// the CRT look. Only added when the theme sets <c>PanelScanlines</c>.
    /// </summary>
    private static Rectangle BuildScanline()
    {
        var line = new GeometryDrawing
        {
            Brush = new SolidColorBrush(Color.FromArgb(14, 255, 122, 26)), // ~5.5% alpha
            Geometry = new RectangleGeometry(new Rect(0, 0, 3, 1))
        };
        var brush = new DrawingBrush(line)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 3, 3),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        return new Rectangle { Fill = brush, IsHitTestVisible = false };
    }

    /// <summary>
    /// Four L-shaped 1px corner brackets drawn on top of the frame, inset by -1px so they overlap
    /// the border. Only added when the theme sets <c>PanelCornerBrackets</c>.
    /// </summary>
    private static Grid BuildCornerBrackets(double size)
    {
        var accent = AccentBrush();
        var grid = new Grid { IsHitTestVisible = false };

        Border Corner(HorizontalAlignment h, VerticalAlignment v, Thickness edges) => new()
        {
            Width = size,
            Height = size,
            HorizontalAlignment = h,
            VerticalAlignment = v,
            Margin = new Thickness(-1),
            BorderBrush = accent,
            BorderThickness = edges,
            IsHitTestVisible = false
        };

        grid.Children.Add(Corner(HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(1, 1, 0, 0)));
        grid.Children.Add(Corner(HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, 1, 1, 0)));
        grid.Children.Add(Corner(HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(1, 0, 0, 1)));
        grid.Children.Add(Corner(HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 1, 1)));
        return grid;
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
                OuterBorder.Cursor = null;
                ResizeGrips.Visibility = Visibility.Collapsed;
                break;

            case HudInteractionState.Active:
                IsHitTestVisible = true;
                OuterBorder.Opacity = 1.0;
                OuterBorder.Cursor = null;
                ResizeGrips.Visibility = Visibility.Collapsed;
                break;

            case HudInteractionState.Edit:
                IsHitTestVisible = true;
                OuterBorder.Opacity = 1.0;
                OuterBorder.Cursor = Cursors.SizeAll; // whole panel is draggable while calibrating
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

        // The panel fill lives on the clipped host so it takes the silhouette; Beer paints amber
        // "liquid", LiquidGlass a frosted blue-glass gradient, Retro a recessed screen-well, Space a
        // plate gradient; every other theme uses the flat panel token.
        _clipHost.Background = Res("LiquidGradient") is true ? LiquidBrush()
            : Res("FrostedGlass") is true ? GlassFillBrush()
            : Res("CrtBezel") is true ? ScreenWellBrush()
            : Res("MfdFrame") is true ? PlateBrush()
            : bg;

        if (Res("CrtBezel") is true)
        {
            // A chunky CRT bezel frame wraps the recessed screen well.
            OuterBorder.Background = Brushes.Transparent;
            OuterBorder.BorderBrush = BezelBrush();
            OuterBorder.BorderThickness = new Thickness(10);
            OuterBorder.CornerRadius = new CornerRadius(16);
        }
        else if (_shape != Silhouette.Rounded)
        {
            // Pint / chamfer: the rim path is the outline, so drop the rectangular border.
            OuterBorder.Background = Brushes.Transparent;
            OuterBorder.BorderThickness = new Thickness(0);
        }
        else
        {
            OuterBorder.Background = Brushes.Transparent;
            OuterBorder.BorderBrush = border;
            OuterBorder.BorderThickness = new Thickness(1);
        }

        UpdateSilhouette();
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
        if (_appState.Current.HudInteractionState != HudInteractionState.Edit) return;
        if (IsInteractive(e.OriginalSource)) return; // clicks on buttons/inputs act, not drag
        _isDragging = true;
        _dragStart = e.GetPosition(null);
        var parent = Parent as Canvas ?? VisualTreeHelper.GetParent(this) as Canvas;
        _panelStartPos = parent != null
            ? new Point(Canvas.GetLeft(this), Canvas.GetTop(this))
            : new Point(0, 0);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    /// <summary>True if the click landed on an interactive control, so a drag should not start.</summary>
    private static bool IsInteractive(object? source)
    {
        for (var d = source as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is ButtonBase or TextBoxBase or ComboBox or Slider or System.Windows.Controls.Primitives.ScrollBar)
                return true;
        return false;
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
