using System.Drawing;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.Theming;

/// <summary>
/// Theme token system (spec §62, §63, §157).
/// Themes change presentation only — never functionality.
/// Panels consume semantic tokens (PanelBackground, Accent, etc.),
/// never theme-name comparisons.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly ILogger<ThemeService> _logger;
    private readonly ResourceDictionary _resources;
    private string _currentThemeId = "Default";

    public event EventHandler? ThemeChanged;

    public ThemeService(ILogger<ThemeService> logger)
    {
        _logger = logger;
        _resources = new ResourceDictionary();
        ApplyTheme("Default");
    }

    public IReadOnlyList<string> AvailableThemeIds => ["Default", "Dark", "Light", "Retro"];
    public string CurrentThemeId => _currentThemeId;

    public object? GetResource(string tokenKey) =>
        _resources.Contains(tokenKey) ? _resources[tokenKey] : null;

    public void ApplyTheme(string themeId)
    {
        _currentThemeId = themeId;

        switch (themeId)
        {
            case "Dark":
                ApplyTokenSet(ThemeTokens.Dark);
                break;
            case "Light":
                ApplyTokenSet(ThemeTokens.Light);
                break;
            case "Retro":
                ApplyTokenSet(ThemeTokens.Retro);
                break;
            default: // "Default" — neutral polished dark
                ApplyTokenSet(ThemeTokens.Default);
                break;
        }

        PublishToApplication();

        _logger.LogDebug("Theme applied: {ThemeId}.", themeId);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Republishes the current token set into the live WPF application resources.</summary>
    private void PublishToApplication()
    {
        if (Application.Current == null) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Contains("__StudyHudTheme"));
            if (existing != null)
                Application.Current.Resources.MergedDictionaries.Remove(existing);

            var copy = new ResourceDictionary { { "__StudyHudTheme", true } };
            foreach (var key in _resources.Keys)
                copy[key] = _resources[key];
            Application.Current.Resources.MergedDictionaries.Add(copy);
        });
    }

    private void ApplyTokenSet(ThemeTokenSet tokens)
    {
        _resources.Clear();

        // Colours
        Set("PanelBackground", tokens.PanelBackground);
        Set("PanelBorder", tokens.PanelBorder);
        Set("SurfaceBackground", tokens.SurfaceBackground);
        Set("SecondaryBackground", tokens.SecondaryBackground);
        Set("Accent", tokens.Accent);
        Set("PrimaryText", tokens.PrimaryText);
        Set("SecondaryText", tokens.SecondaryText);
        Set("WarningColour", tokens.Warning);
        Set("ErrorColour", tokens.Error);
        Set("SuccessColour", tokens.Success);
        Set("RevealTab", tokens.RevealTab);

        // Geometry
        Set("CornerRadius", new CornerRadius(tokens.CornerRadius));
        Set("PanelPadding", new Thickness(tokens.PanelPadding));
        Set("BorderWidth", tokens.BorderWidth);
        Set("ButtonHeight", tokens.ButtonHeight);

        // Presentation primitives (theme-agnostic feature flags; false in Default/Dark/Light).
        // Panels read these to decide whether to draw corner brackets, scanlines, a segmented
        // progress bar or a phosphor glow — never a theme-name comparison (spec §63).
        Set("PanelCornerBrackets", tokens.CornerBrackets);
        Set("PanelScanlines", tokens.Scanlines);
        Set("SegmentedProgress", tokens.SegmentedProgress);
        Set("PhosphorGlow", tokens.PhosphorGlow);

        // Typography
        Set("TitleFontFamily", new FontFamily(tokens.TitleFont));
        Set("BodyFontFamily", new FontFamily(tokens.BodyFont));
        Set("MonoFontFamily", new FontFamily(tokens.MonoFont));
        Set("BodyFontSize", tokens.BodyFontSize);
        Set("SmallFontSize", tokens.SmallFontSize);
        Set("TitleFontSize", tokens.TitleFontSize);

        // Animation durations (spec §66)
        Set("SnapDuration", TimeSpan.FromMilliseconds(125));
        Set("CollapseDuration", TimeSpan.FromMilliseconds(140));
        Set("WorkspaceFadeDuration", TimeSpan.FromMilliseconds(80));
    }

    private void Set(string key, object value) => _resources[key] = value;

    /// <summary>
    /// Applies a custom accent colour with automatic contrast protection (spec §64, §158).
    /// </summary>
    public void ApplyAccentColour(System.Drawing.Color accent)
    {
        var wpfColor = System.Windows.Media.Color.FromArgb(accent.A, accent.R, accent.G, accent.B);
        var brush = new SolidColorBrush(wpfColor);
        brush.Freeze();
        _resources["Accent"] = brush;

        // Contrast protection
        var foreground = CalculateContrastColour(accent);
        var fgBrush = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(foreground.A, foreground.R, foreground.G, foreground.B));
        fgBrush.Freeze();
        _resources["AccentForeground"] = fgBrush;

        PublishToApplication(); // so DynamicResource consumers pick up the new accent live
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static System.Drawing.Color CalculateContrastColour(System.Drawing.Color bg)
    {
        // WCAG relative luminance for contrast check (spec §158)
        double r = bg.R / 255.0;
        double g = bg.G / 255.0;
        double b = bg.B / 255.0;
        double luminance = 0.2126 * Linearise(r) + 0.7152 * Linearise(g) + 0.0722 * Linearise(b);

        return luminance > 0.179 ? System.Drawing.Color.Black : System.Drawing.Color.White;
    }

    private static double Linearise(double c) =>
        c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}

// ─── Token sets ─────────────────────────────────────────────────────────────

internal record ThemeTokenSet
{
    // Colours
    public required SolidColorBrush PanelBackground { get; init; }
    public required SolidColorBrush PanelBorder { get; init; }
    public required SolidColorBrush SurfaceBackground { get; init; }
    public required SolidColorBrush SecondaryBackground { get; init; }
    public required SolidColorBrush Accent { get; init; }
    public required SolidColorBrush PrimaryText { get; init; }
    public required SolidColorBrush SecondaryText { get; init; }
    public required SolidColorBrush Warning { get; init; }
    public required SolidColorBrush Error { get; init; }
    public required SolidColorBrush Success { get; init; }
    public required SolidColorBrush RevealTab { get; init; }

    // Typography
    public required string TitleFont { get; init; }
    public required string BodyFont { get; init; }
    public required string MonoFont { get; init; }
    public required double BodyFontSize { get; init; }
    public required double SmallFontSize { get; init; }
    public required double TitleFontSize { get; init; }

    // Geometry
    public required double CornerRadius { get; init; }
    public required double PanelPadding { get; init; }
    public required double BorderWidth { get; init; }
    public required double ButtonHeight { get; init; }

    // Presentation primitives — additive, theme-agnostic. Default false so existing
    // themes are unaffected; the Retro theme opts in to all four.
    public bool CornerBrackets { get; init; }
    public bool Scanlines { get; init; }
    public bool SegmentedProgress { get; init; }
    public bool PhosphorGlow { get; init; }
}

internal static class ThemeTokens
{
    private static SolidColorBrush B(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static ThemeTokenSet Default => new()
    {
        PanelBackground = B(22, 22, 26, 230),
        PanelBorder = B(60, 60, 70),
        SurfaceBackground = B(32, 32, 38, 240),
        SecondaryBackground = B(40, 40, 48, 220),
        Accent = B(0, 180, 255),
        PrimaryText = B(240, 240, 248),
        SecondaryText = B(160, 160, 175),
        Warning = B(255, 190, 40),
        Error = B(255, 80, 80),
        Success = B(50, 200, 100),
        RevealTab = B(0, 180, 255, 200),
        TitleFont = "Segoe UI Semibold",
        BodyFont = "Segoe UI",
        MonoFont = "Cascadia Code, Consolas",
        BodyFontSize = 12,
        SmallFontSize = 10,
        TitleFontSize = 14,
        CornerRadius = 6,
        PanelPadding = 10,
        BorderWidth = 1,
        ButtonHeight = 28
    };

    public static ThemeTokenSet Dark => Default with
    {
        PanelBackground = B(15, 15, 18, 235),
        SurfaceBackground = B(20, 20, 24, 245),
        Accent = B(99, 102, 241)
    };

    /// <summary>
    /// "Retro" — 1980s CRT-terminal look: warm monochrome amber on near-black, 1px wireframe
    /// strokes, square corners, corner brackets, scanlines and a phosphor glow. Presentation only.
    /// </summary>
    public static ThemeTokenSet Retro => new()
    {
        PanelBackground = B(14, 10, 8, 240),
        PanelBorder = B(255, 122, 26, 97),
        SurfaceBackground = B(9, 7, 6, 245),
        SecondaryBackground = B(255, 122, 26, 20),
        Accent = B(255, 122, 26),
        PrimaryText = B(255, 210, 166),
        SecondaryText = B(208, 149, 92),
        Warning = B(255, 158, 27),
        Error = B(255, 61, 32),
        Success = B(255, 216, 107),
        RevealTab = B(255, 122, 26, 140),
        TitleFont = "Cascadia Mono SemiBold, Consolas",
        BodyFont = "Cascadia Mono, Consolas",
        MonoFont = "Cascadia Mono, Consolas",
        BodyFontSize = 12,
        SmallFontSize = 10,
        TitleFontSize = 14,
        CornerRadius = 0,
        PanelPadding = 12,
        BorderWidth = 1,
        ButtonHeight = 26,
        CornerBrackets = true,
        Scanlines = true,
        SegmentedProgress = true,
        PhosphorGlow = true
    };

    public static ThemeTokenSet Light => new()
    {
        PanelBackground = B(248, 248, 252, 230),
        PanelBorder = B(200, 200, 210),
        SurfaceBackground = B(255, 255, 255, 240),
        SecondaryBackground = B(240, 240, 245, 220),
        Accent = B(99, 102, 241),
        PrimaryText = B(20, 20, 30),
        SecondaryText = B(100, 100, 115),
        Warning = B(200, 130, 0),
        Error = B(200, 30, 30),
        Success = B(30, 150, 70),
        RevealTab = B(99, 102, 241, 200),
        TitleFont = "Segoe UI Semibold",
        BodyFont = "Segoe UI",
        MonoFont = "Cascadia Code, Consolas",
        BodyFontSize = 12,
        SmallFontSize = 10,
        TitleFontSize = 14,
        CornerRadius = 6,
        PanelPadding = 10,
        BorderWidth = 1,
        ButtonHeight = 28
    };
}
