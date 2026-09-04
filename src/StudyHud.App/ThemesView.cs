using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.App;

/// <summary>
/// Themes page (spec §62–64): pick a base theme and a custom accent colour. Applies live through
/// <see cref="IThemeService"/> and persists the choice to settings. Built in code (no XAML).
/// </summary>
public sealed class ThemesView : UserControl
{
    private readonly IThemeService _theme;
    private readonly ISettingsStore _settings;
    private readonly ILogger<ThemesView> _logger;

    private readonly Border _preview;
    private readonly TextBlock _hexLabel;
    private readonly TextBlock _status;
    private Slider _r = null!, _g = null!, _b = null!;

    private static readonly (string Name, string Hex)[] Swatches =
    [
        ("Sky", "#00B4FF"), ("Indigo", "#6366F1"), ("Violet", "#8B5CF6"), ("Teal", "#14B8A6"),
        ("Green", "#22C55E"), ("Amber", "#F59E0B"), ("Orange", "#FB7185"), ("Red", "#EF4444")
    ];

    public ThemesView(IThemeService theme, ISettingsStore settings, ILogger<ThemesView> logger)
    {
        _theme = theme;
        _settings = settings;
        _logger = logger;

        var root = new StackPanel { Margin = new Thickness(4) };

        root.Children.Add(Title("Themes"));
        root.Children.Add(Body("Change how Study HUD looks. Changes apply immediately and are saved."));

        // ── Base theme ───────────────────────────────────────────────────────
        root.Children.Add(Header("BASE THEME"));
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var id in _theme.AvailableThemeIds)
        {
            var btn = MakeButton(id, accent: id == _theme.CurrentThemeId);
            btn.Margin = new Thickness(0, 0, 8, 0);
            btn.Click += (_, _) => ApplyThemeId(id);
            themeRow.Children.Add(btn);
        }
        root.Children.Add(themeRow);

        // ── Accent colour ────────────────────────────────────────────────────
        root.Children.Add(Header("ACCENT COLOUR"));

        var swatchRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (name, hex) in Swatches)
        {
            var sw = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(FromHex(hex)),
                BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = name
            };
            sw.MouseLeftButtonUp += (_, _) => PickColour(FromHex(hex), apply: true);
            swatchRow.Children.Add(sw);
        }
        root.Children.Add(swatchRow);

        _r = MakeSlider();
        _g = MakeSlider();
        _b = MakeSlider();
        root.Children.Add(SliderRow("Red", _r));
        root.Children.Add(SliderRow("Green", _g));
        root.Children.Add(SliderRow("Blue", _b));

        _preview = new Border
        {
            Width = 60, Height = 30, CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 8, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0, 180, 255)),
            BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        _hexLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("PrimaryText", Colors.White)
        };

        var applyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        applyRow.Children.Add(_preview);
        applyRow.Children.Add(_hexLabel);
        var applyBtn = MakeButton("Apply accent", accent: true);
        applyBtn.Margin = new Thickness(16, 0, 0, 0);
        applyBtn.Click += (_, _) => PickColour(CurrentSliderColour(), apply: true);
        applyRow.Children.Add(applyBtn);
        var resetBtn = MakeButton("Reset to theme accent", accent: false);
        resetBtn.Margin = new Thickness(8, 0, 0, 0);
        resetBtn.Click += (_, _) => ResetAccent();
        applyRow.Children.Add(resetBtn);
        root.Children.Add(applyRow);

        _status = new TextBlock
        {
            Opacity = 0.75, Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_status);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

        Loaded += (_, _) => _ = InitFromSettingsAsync();
    }

    private async Task InitFromSettingsAsync()
    {
        try
        {
            var s = await _settings.LoadAsync();
            var start = !string.IsNullOrWhiteSpace(s.AccentColour)
                ? FromHex(s.AccentColour!)
                : Color.FromRgb(0, 180, 255);
            SetSliders(start);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading theme settings failed.");
            SetSliders(Color.FromRgb(0, 180, 255));
        }
    }

    private void ApplyThemeId(string id)
    {
        _theme.ApplyTheme(id);
        _ = _settings.UpdateAsync(s => s with { ThemeId = id });
        SetStatus($"Theme set to {id}.");
    }

    private void PickColour(Color c, bool apply)
    {
        SetSliders(c);
        if (!apply) return;

        _theme.ApplyAccentColour(System.Drawing.Color.FromArgb(255, c.R, c.G, c.B));
        var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        _ = _settings.UpdateAsync(s => s with { AccentColour = hex });
        SetStatus($"Accent set to {hex}.");
    }

    private void ResetAccent()
    {
        _ = _settings.UpdateAsync(s => s with { AccentColour = null });
        _theme.ApplyTheme(_theme.CurrentThemeId); // re-apply theme to restore its default accent
        SetStatus("Accent reset to the theme default.");
    }

    private Color CurrentSliderColour() =>
        Color.FromRgb((byte)_r.Value, (byte)_g.Value, (byte)_b.Value);

    private void SetSliders(Color c)
    {
        _r.Value = c.R; _g.Value = c.G; _b.Value = c.B;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var c = CurrentSliderColour();
        _preview.Background = new SolidColorBrush(c);
        _hexLabel.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    // ── UI helpers ───────────────────────────────────────────────────────────

    private Slider MakeSlider()
    {
        var s = new Slider { Minimum = 0, Maximum = 255, Width = 240, SmallChange = 1, LargeChange = 16 };
        s.ValueChanged += (_, _) => UpdatePreview();
        return s;
    }

    private UIElement SliderRow(string label, Slider slider)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        row.Children.Add(new TextBlock
        {
            Text = label, Width = 60, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("PrimaryText", Colors.White)
        });
        row.Children.Add(slider);
        return row;
    }

    private TextBlock Title(string t) => new()
    {
        Text = t, FontSize = 20, FontWeight = FontWeights.SemiBold,
        Foreground = Brush("PrimaryText", Colors.White), Margin = new Thickness(0, 0, 0, 8)
    };

    private TextBlock Body(string t) => new()
    {
        Text = t, TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
        Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 0, 0, 6)
    };

    private TextBlock Header(string t) => new()
    {
        Text = t, FontSize = 10, Opacity = 0.5,
        Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 16, 0, 6)
    };

    private void SetStatus(string msg) => _status.Text = msg;

    private Button MakeButton(string content, bool accent) => new()
    {
        Content = content,
        Padding = new Thickness(14, 4, 14, 4),
        Cursor = System.Windows.Input.Cursors.Hand,
        BorderThickness = new Thickness(0),
        Foreground = accent ? Brushes.White : Brush("SecondaryText", Colors.Gray),
        Background = accent ? Brush("Accent", Color.FromRgb(0, 180, 255)) : Brushes.Transparent
    };

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);

    private static Color FromHex(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length != 6) return Color.FromRgb(0, 180, 255);
        return Color.FromRgb(
            Convert.ToByte(h.Substring(0, 2), 16),
            Convert.ToByte(h.Substring(2, 2), 16),
            Convert.ToByte(h.Substring(4, 2), 16));
    }
}
