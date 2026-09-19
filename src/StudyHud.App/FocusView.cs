using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Overlay;

namespace StudyHud.App;

/// <summary>
/// Focus Mode page (Focus Mode design, artboards 1b + 1f): a Pomodoro study timer with work/break
/// cycles and session stats, plus the cadence settings and the Quick-Search hotkey. Local only —
/// no network, nothing tracked off-device. Built in code (no XAML).
/// </summary>
public sealed class FocusView : UserControl
{
    private readonly PomodoroService _pomodoro;
    private readonly IApplicationStateService _appState;
    private readonly ISettingsStore _settings;

    private readonly TextBlock _time;
    private readonly TextBlock _phase;
    private readonly Button _startPause;
    private readonly TextBlock _stats;
    private readonly StackPanel _dots;

    // Progress is rendered either as a continuous bar (Default/Dark/Light) or as 16 discrete
    // segments (Retro). Exactly one of these is built and added to the tree.
    private readonly ProgressBar? _progress;
    private readonly List<Border> _segments = new();
    private readonly TextBlock _pct;

    private readonly TextBox _focusBox;
    private readonly TextBox _shortBox;
    private readonly TextBox _longBox;
    private readonly TextBox _afterBox;

    // Presentation flags/geometry read once from the active theme's tokens.
    private readonly bool _segmented;
    private readonly bool _glow;
    private readonly bool _liquid;
    private readonly bool _frosted;
    private readonly CornerRadius _radius;
    private const int SegmentCount = 16;

    public FocusView(PomodoroService pomodoro, IApplicationStateService appState, ISettingsStore settings)
    {
        _pomodoro = pomodoro;
        _appState = appState;
        _settings = settings;

        _segmented = TryFindResource("SegmentedProgress") is true;
        _glow = TryFindResource("PhosphorGlow") is true;
        _liquid = TryFindResource("LiquidGradient") is true;
        _frosted = TryFindResource("FrostedGlass") is true;
        _radius = TryFindResource("CornerRadius") is CornerRadius cr ? cr : new CornerRadius(6);

        var root = new StackPanel { Margin = new Thickness(4) };

        root.Children.Add(new TextBlock
        {
            Text = "Focus", FontSize = 20, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryText", Colors.White), Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "A Pomodoro study timer with work/break cycles. Local only — no network, nothing "
                 + "tracked off your device.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 0, 0, 16)
        });

        // ── Timer card (artboard 1b) ─────────────────────────────────────────
        var card = new Border
        {
            // Beer paints the card as amber "liquid", LiquidGlass as frosted blue glass; other
            // themes use the flat surface token.
            Background = _liquid ? LiquidBrush()
                : _frosted ? GlassFillBrush()
                : Brush("SecondaryBackground", Color.FromArgb(180, 40, 40, 48)),
            BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = _radius,
            Padding = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var cardStack = new StackPanel { MinWidth = 340 };

        // Beer: a foam-white band across the top of the glass.
        if (_liquid)
            cardStack.Children.Add(new Border
            {
                Height = 14, Margin = new Thickness(-20, -20, -20, 12),
                Background = FoamBrush(),
                CornerRadius = new CornerRadius(_radius.TopLeft, _radius.TopRight, 0, 0)
            });

        _phase = new TextBlock
        {
            Text = "READY", FontSize = 11, Opacity = 0.85,
            HorizontalAlignment = HorizontalAlignment.Center,
            // On the amber glass the accent has poor contrast, so Beer uses roast-brown text.
            Foreground = _liquid ? Brush("PrimaryText", Colors.White) : Brush("Accent", Color.FromRgb(0, 180, 255)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Effect = Glow(0.55, 14)
        };
        cardStack.Children.Add(_phase);

        _time = new TextBlock
        {
            Text = "25:00", FontSize = 56, FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("PrimaryText", Colors.White),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Margin = new Thickness(0, 2, 0, 8),
            Effect = Glow(0.55, 18)
        };
        cardStack.Children.Add(_time);

        if (_segmented)
        {
            // Retro: 16 discrete cells with a 3px gap, filled left-to-right.
            var seg = new UniformGrid { Columns = SegmentCount, Height = 9, Margin = new Thickness(0, 0, 0, 4) };
            for (int i = 0; i < SegmentCount; i++)
            {
                var cell = new Border
                {
                    Margin = new Thickness(0, 0, i == SegmentCount - 1 ? 0 : 3, 0),
                    BorderBrush = Brush("PanelBorder", Color.FromArgb(77, 255, 122, 26)),
                    BorderThickness = new Thickness(1),
                    Background = Brushes.Transparent
                };
                _segments.Add(cell);
                seg.Children.Add(cell);
            }
            cardStack.Children.Add(seg);
        }
        else
        {
            _progress = new ProgressBar
            {
                Height = 6, Minimum = 0, Maximum = 100, Value = 0,
                Foreground = Brush("Accent", Color.FromRgb(0, 180, 255)),
                Background = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 10)
            };
            cardStack.Children.Add(_progress);
        }

        _pct = new TextBlock
        {
            FontSize = 10, Opacity = 0.85,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 0, 0, 10),
            FontFamily = new FontFamily("Cascadia Code, Consolas")
        };
        if (_segmented) cardStack.Children.Add(_pct);

        _dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        cardStack.Children.Add(_dots);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _startPause = MakeButton("Start", accent: true);
        _startPause.Click += (_, _) => { _pomodoro.Toggle(); UpdateUi(); };
        buttons.Children.Add(_startPause);
        var skip = MakeButton("Skip", accent: false); skip.Margin = new Thickness(8, 0, 0, 0);
        skip.Click += (_, _) => { _pomodoro.Skip(); UpdateUi(); };
        buttons.Children.Add(skip);
        var reset = MakeButton("Reset", accent: false); reset.Margin = new Thickness(8, 0, 0, 0);
        reset.Click += (_, _) => { _pomodoro.Reset(); UpdateUi(); };
        buttons.Children.Add(reset);
        cardStack.Children.Add(buttons);

        card.Child = cardStack;
        root.Children.Add(card);

        _stats = new TextBlock
        {
            Opacity = 0.8, Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_stats);

        // ── Pomodoro lengths (artboard 1f) ───────────────────────────────────
        root.Children.Add(Header("POMODORO LENGTHS"));
        _focusBox = NumBox(); _shortBox = NumBox(); _longBox = NumBox(); _afterBox = NumBox();
        var grid = new StackPanel { Orientation = Orientation.Horizontal };
        grid.Children.Add(LabeledNum("Focus (min)", _focusBox));
        grid.Children.Add(LabeledNum("Short break", _shortBox));
        grid.Children.Add(LabeledNum("Long break", _longBox));
        grid.Children.Add(LabeledNum("Long after (cycles)", _afterBox));
        root.Children.Add(grid);
        var apply = MakeButton("Save lengths", accent: true);
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        apply.Margin = new Thickness(0, 8, 0, 0);
        apply.Click += (_, _) => SaveLengths();
        root.Children.Add(apply);

        // ── Sound (audible start/end cue) ────────────────────────────────────
        root.Children.Add(Header("SOUND"));
        var sound = new CheckBox
        {
            Content = "Play a chime when a focus block starts and ends",
            IsChecked = _pomodoro.SoundsEnabled,
            Foreground = Brush("PrimaryText", Colors.White),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        sound.Checked += (_, _) => SetSounds(true);
        sound.Unchecked += (_, _) => SetSounds(false);
        root.Children.Add(sound);

        // ── ADHD support layer ───────────────────────────────────────────────
        root.Children.Add(Header("ADHD FOCUS MODE"));
        root.Children.Add(new TextBlock
        {
            Text = "Adds a calm theme plus focus panels: a visible clock + streak, a one-tap Start panel, "
                 + "and a supportive companion. Toggle it any time from the ◎ button on the control capsule.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 0, 0, 6)
        });

        var adhd = AdhdCheck("Turn on ADHD focus mode", _appState.Current.AdhdMode);
        adhd.Checked += (_, _) => _appState.SetAdhdMode(true);
        adhd.Unchecked += (_, _) => _appState.SetAdhdMode(false);
        root.Children.Add(adhd);

        var shield = AdhdCheck("Focus shield — count distractions during a block", _settings.Current.FocusShieldEnabled);
        shield.Checked += (_, _) => _ = _settings.UpdateAsync(s => s with { FocusShieldEnabled = true });
        shield.Unchecked += (_, _) => _ = _settings.UpdateAsync(s => s with { FocusShieldEnabled = false });
        root.Children.Add(shield);

        var wind = AdhdCheck("Show the wind-down card when a block ends", _settings.Current.WindDownCard);
        wind.Checked += (_, _) => _ = _settings.UpdateAsync(s => s with { WindDownCard = true });
        wind.Unchecked += (_, _) => _ = _settings.UpdateAsync(s => s with { WindDownCard = false });
        root.Children.Add(wind);

        var capRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        capRow.Children.Add(new TextBlock
        {
            Text = "Break nudge after (min, 0 = off)", VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("PrimaryText", Colors.White), Margin = new Thickness(0, 0, 8, 0)
        });
        var capBox = NumBox();
        capBox.Text = _settings.Current.SessionCapMinutes.ToString();
        capBox.LostFocus += (_, _) =>
        {
            int cap = int.TryParse(capBox.Text, out var v) ? Math.Clamp(v, 0, 240) : 90;
            capBox.Text = cap.ToString();
            _pomodoro.SessionCapMinutes = cap;
            _ = _settings.UpdateAsync(s => s with { SessionCapMinutes = cap });
        };
        capRow.Children.Add(capBox);
        root.Children.Add(capRow);

        // Support strength — how soon/firm the nudges are.
        root.Children.Add(new TextBlock
        {
            Text = "Support strength", FontSize = 11, Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 10, 0, 4)
        });
        var strengthRow = new WrapPanel();
        void RebuildStrength()
        {
            strengthRow.Children.Clear();
            foreach (var level in new[] { AdhdStrength.Gentle, AdhdStrength.Standard, AdhdStrength.Strong })
            {
                var btn = MakeButton(level.ToString(), accent: _settings.Current.AdhdStrength == level);
                btn.Margin = new Thickness(0, 0, 8, 0);
                var chosen = level;
                btn.Click += (_, _) =>
                {
                    _ = _settings.UpdateAsync(s => s with { AdhdStrength = chosen });
                    RebuildStrength();
                };
                strengthRow.Children.Add(btn);
            }
        }
        RebuildStrength();
        root.Children.Add(strengthRow);
        root.Children.Add(new TextBlock
        {
            Text = "Gentle nudges late and softly; Strong nudges early with firmer (still kind) wording.",
            FontSize = 10, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 4, 0, 0)
        });

        // One-tap: set everything to the recommended ADHD configuration.
        var preset = MakeButton("✨  Set up for ADHD", accent: true);
        preset.HorizontalAlignment = HorizontalAlignment.Left;
        preset.Margin = new Thickness(0, 12, 0, 0);
        preset.ToolTip = "Turns on ADHD mode with the recommended strength, shorter sprints, break nudges "
                       + "and the focus shield. Tip: pick the Calm theme on the Themes page too.";
        preset.Click += (_, _) =>
        {
            var level = AdhdProfile.Recommended;
            int cap = AdhdProfile.RecommendedSessionCap(level);
            int focus = AdhdProfile.RecommendedFocusMinutes(level);

            _appState.SetAdhdMode(true);
            _pomodoro.SessionCapMinutes = cap;
            _pomodoro.WorkMinutes = focus;
            _ = _settings.UpdateAsync(s => s with
            {
                AdhdMode = true,
                AdhdStrength = level,
                FocusShieldEnabled = true,
                WindDownCard = true,
                EscalatingBreakPrompts = true,
                SessionCapMinutes = cap,
                FocusMinutes = focus
            });

            // Reflect the new values in the visible controls.
            adhd.IsChecked = true;
            shield.IsChecked = true;
            wind.IsChecked = true;
            capBox.Text = cap.ToString();
            _focusBox.Text = focus.ToString();
            RebuildStrength();
            if (_pomodoro.Phase == PomodoroPhase.Idle) UpdateUi();
        };
        root.Children.Add(preset);

        // ── Quick-Search hotkey (artboard 1f) ────────────────────────────────
        root.Children.Add(Header("QUICK-SEARCH"));
        root.Children.Add(new TextBlock
        {
            Text = "Press  Ctrl + Shift + Space  anywhere to open the Quick-Search palette and search "
                 + "your notes. Panic-hide is Ctrl + Shift + H.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
            Foreground = Brush("SecondaryText", Colors.Gray)
        });

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _pomodoro.Tick -= OnPomodoro;
            _pomodoro.PhaseChanged -= OnPomodoro;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _focusBox.Text = _pomodoro.WorkMinutes.ToString();
        _shortBox.Text = _pomodoro.ShortBreakMinutes.ToString();
        _longBox.Text = _pomodoro.LongBreakMinutes.ToString();
        _afterBox.Text = _pomodoro.LongBreakEvery.ToString();

        _pomodoro.Tick += OnPomodoro;
        _pomodoro.PhaseChanged += OnPomodoro;
        UpdateUi();
    }

    private void SaveLengths()
    {
        _pomodoro.WorkMinutes = Clamp(_focusBox.Text, 1, 120, 25);
        _pomodoro.ShortBreakMinutes = Clamp(_shortBox.Text, 1, 60, 5);
        _pomodoro.LongBreakMinutes = Clamp(_longBox.Text, 1, 60, 15);
        _pomodoro.LongBreakEvery = Clamp(_afterBox.Text, 1, 12, 4);
        if (_pomodoro.Phase == PomodoroPhase.Idle) UpdateUi();

        _ = _settings.UpdateAsync(s => s with
        {
            FocusMinutes = _pomodoro.WorkMinutes,
            ShortBreakMinutes = _pomodoro.ShortBreakMinutes,
            LongBreakMinutes = _pomodoro.LongBreakMinutes,
            LongBreakEveryCycles = _pomodoro.LongBreakEvery
        });
    }

    private void SetSounds(bool on)
    {
        _pomodoro.SoundsEnabled = on;
        _ = _settings.UpdateAsync(s => s with { FocusSoundsEnabled = on });
    }

    private CheckBox AdhdCheck(string label, bool isChecked) => new()
    {
        Content = label,
        IsChecked = isChecked,
        Foreground = Brush("PrimaryText", Colors.White),
        Cursor = System.Windows.Input.Cursors.Hand,
        Margin = new Thickness(0, 2, 0, 0)
    };

    private void OnPomodoro(object? sender, EventArgs e) => UpdateUi();

    private void UpdateUi()
    {
        var remaining = _pomodoro.Phase == PomodoroPhase.Idle
            ? TimeSpan.FromMinutes(_pomodoro.WorkMinutes)
            : _pomodoro.Remaining;
        _time.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";

        _phase.Text = _pomodoro.Phase switch
        {
            PomodoroPhase.Work => "FOCUS",
            PomodoroPhase.ShortBreak => "SHORT BREAK",
            PomodoroPhase.LongBreak => "LONG BREAK",
            _ => "READY"
        };

        double frac = _pomodoro.PhaseLength.TotalSeconds > 0
            ? 1.0 - _pomodoro.Remaining.TotalSeconds / _pomodoro.PhaseLength.TotalSeconds
            : 0;
        frac = Math.Clamp(frac, 0, 1);
        if (_segmented)
        {
            int filled = (int)Math.Round(frac * SegmentCount);
            for (int i = 0; i < _segments.Count; i++)
            {
                bool on = i < filled;
                _segments[i].Background = on ? Brush("Accent", Color.FromRgb(0, 180, 255)) : Brushes.Transparent;
                _segments[i].BorderThickness = new Thickness(on ? 0 : 1);
                _segments[i].Effect = on ? Glow(0.4, 12) : null;
            }
            _pct.Text = $"LOADING…   {frac * 100:0}%";
        }
        else if (_progress != null)
        {
            _progress.Value = frac * 100;
        }

        _startPause.Content = _pomodoro.IsRunning
            ? "Pause"
            : _pomodoro.Phase == PomodoroPhase.Idle ? "Start" : "Resume";

        // Session dots (how many focus blocks done in the current long-break cycle).
        _dots.Children.Clear();
        int every = Math.Max(1, _pomodoro.LongBreakEvery);
        int inCycle = _pomodoro.CompletedToday % every;
        if (_glow) // Retro: square dots with glow, framed by SESSION … n/N labels.
        {
            _dots.Children.Add(SessionLabel("SESSION", new Thickness(0, 0, 8, 0)));
            for (int i = 0; i < every; i++)
                _dots.Children.Add(new Border
                {
                    Width = 9, Height = 9, CornerRadius = _radius, Margin = new Thickness(3, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    BorderBrush = Brush("PanelBorder", Color.FromArgb(115, 255, 122, 26)),
                    BorderThickness = new Thickness(i < inCycle ? 0 : 1),
                    Background = i < inCycle ? Brush("Accent", Color.FromRgb(0, 180, 255)) : Brushes.Transparent,
                    Effect = i < inCycle ? Glow(0.5, 10) : null
                });
            _dots.Children.Add(SessionLabel($"  {inCycle}/{every}", new Thickness(6, 0, 0, 0)));
        }
        else // Default / Dark / Light: unchanged round dots.
        {
            for (int i = 0; i < every; i++)
                _dots.Children.Add(new Border
                {
                    Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Margin = new Thickness(3, 0, 0, 0),
                    Background = i < inCycle ? Brush("Accent", Color.FromRgb(0, 180, 255))
                                             : Brush("PanelBorder", Color.FromRgb(58, 58, 68))
                });
        }

        _stats.Text = $"Completed today: {_pomodoro.CompletedToday} pomodoro"
                    + (_pomodoro.CompletedToday == 1 ? "" : "s")
                    + $"   •   Notes captured today: {NotesToday()}"
                    + (string.IsNullOrEmpty(_appState.Current.CurrentCourseId)
                        ? "" : $"   •   Course: {_appState.Current.CurrentCourseId}");
    }

    private static int NotesToday()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudyHud", "Notes");
            if (!Directory.Exists(dir)) return 0;
            var today = DateTime.Now.Date;
            return new DirectoryInfo(dir).EnumerateFiles("note-*.*")
                .Count(f => f.Extension is ".png" or ".jpg" or ".jpeg" && f.LastWriteTime.Date == today);
        }
        catch { return 0; }
    }

    private static int Clamp(string text, int min, int max, int fallback)
        => int.TryParse(text, out var v) ? Math.Clamp(v, min, max) : fallback;

    // ── UI helpers ───────────────────────────────────────────────────────────

    private TextBox NumBox() => new()
    {
        Width = 60, Height = 26, VerticalContentAlignment = VerticalAlignment.Center,
        FontFamily = new FontFamily("Cascadia Code, Consolas")
    };

    private UIElement LabeledNum(string label, TextBox box)
    {
        var col = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        col.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 0, 0, 4)
        });
        col.Children.Add(box);
        return col;
    }

    private TextBlock Header(string t) => new()
    {
        Text = t, FontSize = 10, Opacity = 0.5,
        Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 18, 0, 6)
    };

    private Button MakeButton(string content, bool accent) => new()
    {
        Content = content,
        Padding = new Thickness(16, 5, 16, 5),
        Cursor = System.Windows.Input.Cursors.Hand,
        // Retro secondary buttons get a 1px amber outline; primary buttons keep the accent fill
        // but switch to dark text + a glow.
        BorderThickness = new Thickness(_glow && !accent ? 1 : 0),
        BorderBrush = Brush("Accent", Color.FromRgb(255, 122, 26)),
        Foreground = accent
            ? (_glow ? new SolidColorBrush(Color.FromRgb(20, 10, 4)) : Brushes.White)
            : (_glow ? Brush("PrimaryText", Colors.White) : Brush("SecondaryText", Colors.Gray)),
        Background = accent ? Brush("Accent", Color.FromRgb(0, 180, 255)) : Brushes.Transparent,
        Effect = accent ? Glow(0.4, 12) : null
    };

    /// <summary>
    /// An accent-coloured bloom for themes that set PhosphorGlow (amber under Retro, cyan under
    /// Space), or null otherwise.
    /// </summary>
    private DropShadowEffect? Glow(double opacity, double blur)
    {
        if (!_glow) return null;
        var c = (Brush("Accent", Color.FromRgb(255, 122, 26)) as SolidColorBrush)?.Color
                ?? Color.FromRgb(255, 122, 26);
        return new DropShadowEffect { Color = c, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity };
    }

    /// <summary>Beer: the vertical amber "liquid" gradient used as the card fill.</summary>
    private static Brush LiquidBrush()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xF3, 0xBB, 0x3C), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xE5, 0xA3, 0x20), 0.32));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xC4, 0x80, 0x0F), 0.70));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xA2, 0x65, 0x0A), 1.0));
        return g;
    }

    /// <summary>Beer: the foam-white gradient used for the card's top band.</summary>
    private static Brush FoamBrush()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xF1, 0xE7, 0xCF), 1.0));
        return g;
    }

    /// <summary>LiquidGlass: the frosted blue-glass gradient used as the card fill.</summary>
    private static Brush GlassFillBrush()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.26, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(56, 255, 255, 255), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(33, 96, 140, 220), 0.45));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(140, 18, 38, 78), 1.0));
        return g;
    }

    private TextBlock SessionLabel(string text, Thickness margin) => new()
    {
        Text = text, FontSize = 10, Margin = margin,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = Brush("SecondaryText", Colors.Gray),
        FontFamily = new FontFamily("Cascadia Code, Consolas")
    };

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
