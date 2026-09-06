using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;
using StudyHud.Macros;

namespace StudyHud.App;

/// <summary>
/// Macros editor (spec §29, §30). Lists the user's macros with enable/delete/play/repeat, and an
/// add-macro form. Actions include the fixed built-ins, a customisable key-sequence (e.g. Ctrl+C,
/// Ctrl+V) built by typing or by recording keys, and recorded mouse clicks. A macro can also be
/// auto-repeated on a millisecond interval. Changes save and apply live through
/// <see cref="MacroManager"/>. Built in code (no XAML).
/// </summary>
public sealed class MacrosView : UserControl
{
    private readonly MacroManager _manager;
    private readonly IMouseClickRecorder _recorder;
    private readonly ILogger<MacrosView> _logger;

    private readonly StackPanel _list;
    private readonly TextBlock _status;

    // Add-form controls
    private readonly TextBox _nameBox;
    private readonly ComboBox _triggerCombo;
    private readonly TextBox _shortcutBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _argBox;
    private readonly TextBox _seqBox;
    private readonly TextBox _repeatBox;
    private readonly Button _recordKeysBtn;

    private int _capturedVk;
    private int _capturedMods;
    private bool _recordingKeys;

    // Action combo indices with a dedicated argument editor.
    private const int ActionOpenUrl = 4, ActionLaunch = 5, ActionTypeText = 6, ActionKeySequence = 7;

    public MacrosView(MacroManager manager, IMouseClickRecorder recorder, ILogger<MacrosView> logger)
    {
        _manager = manager;
        _recorder = recorder;
        _logger = logger;

        var root = new StackPanel { Margin = new Thickness(4) };

        root.Children.Add(Title("Macros"));
        root.Children.Add(new TextBlock
        {
            Text = "A macro runs an action from a trigger. Keyboard shortcuts work everywhere; mouse "
                 + "side-button macros run while another app is focused. Macros can send keystrokes, "
                 + "replay recorded mouse clicks, and auto-repeat on an interval.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 0, 0, 14)
        });

        // ── Existing macros ──────────────────────────────────────────────────
        root.Children.Add(Header("YOUR MACROS"));
        _list = new StackPanel();
        root.Children.Add(_list);

        var rowButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var restore = MakeButton("Restore defaults", accent: false);
        restore.Click += (_, _) => { _manager.SaveAndApply(MacroSpec.Defaults()); Refresh(); };
        rowButtons.Children.Add(restore);

        var recordMouse = MakeButton("● Record mouse clicks", accent: false);
        recordMouse.Margin = new Thickness(8, 0, 0, 0);
        recordMouse.Click += OnRecordMouse;
        rowButtons.Children.Add(recordMouse);
        root.Children.Add(rowButtons);

        // ── Add a macro ──────────────────────────────────────────────────────
        root.Children.Add(Header("ADD A MACRO"));

        _nameBox = new TextBox { Width = 260, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        root.Children.Add(Field("Name", _nameBox));

        _triggerCombo = new ComboBox { Width = 260 };
        _triggerCombo.Items.Add("Keyboard shortcut");
        _triggerCombo.Items.Add("Mouse button 4");
        _triggerCombo.Items.Add("Mouse button 5");
        _triggerCombo.SelectedIndex = 0;
        _triggerCombo.SelectionChanged += (_, _) => UpdateFieldVisibility();
        root.Children.Add(Field("Trigger", _triggerCombo));

        _shortcutBox = new TextBox
        {
            Width = 260, Height = 26, IsReadOnly = true, Focusable = true,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "Click here and press keys…"
        };
        _shortcutBox.PreviewKeyDown += OnShortcutKey;
        _shortcutBox.GotKeyboardFocus += (_, _) => { if (_capturedVk == 0) _shortcutBox.Text = "Press keys…"; };
        _shortcutRow = Field("Shortcut", _shortcutBox);
        root.Children.Add(_shortcutRow);

        _actionCombo = new ComboBox { Width = 260 };
        foreach (var a in new[]
        {
            "Capture Note", "Toggle HUD", "Switch to Note Taking", "Switch to Question Finder",
            "Open URL", "Launch program", "Type text", "Send keys (Ctrl+C, Ctrl+V…)"
        }) _actionCombo.Items.Add(a);
        _actionCombo.SelectedIndex = 0;
        _actionCombo.SelectionChanged += (_, _) => UpdateFieldVisibility();
        root.Children.Add(Field("Action", _actionCombo));

        _argBox = new TextBox { Width = 260, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        _argRow = Field("URL / program / text", _argBox);
        root.Children.Add(_argRow);

        // Key-sequence editor: a "Record keys" start button, quick presets, and a free-text box.
        _seqBox = new TextBox
        {
            Width = 360, Height = 52, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        _seqBox.PreviewKeyDown += OnSequenceKey;
        _recordKeysBtn = MakeButton("Record keys", accent: false);
        _recordKeysBtn.Click += (_, _) => ToggleKeyRecording();

        var presets = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (label, chord) in new[]
        {
            ("Copy", "Ctrl+C"), ("Paste", "Ctrl+V"), ("Cut", "Ctrl+X"), ("Save", "Ctrl+S"),
            ("Select all", "Ctrl+A"), ("Undo", "Ctrl+Z"), ("Enter", "Enter"), ("Tab", "Tab")
        })
        {
            var b = MakeButton(label, accent: false);
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Click += (_, _) => AppendChord(chord);
            presets.Children.Add(b);
        }

        var seqStack = new StackPanel();
        var seqTop = new StackPanel { Orientation = Orientation.Horizontal };
        seqTop.Children.Add(_recordKeysBtn);
        seqTop.Children.Add(new TextBlock
        {
            Text = "  — click, then press each shortcut; click again to stop.",
            VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, FontSize = 11,
            Foreground = Brush("SecondaryText", Colors.Gray)
        });
        seqStack.Children.Add(seqTop);
        seqStack.Children.Add(presets);
        seqStack.Children.Add(_seqBox);
        _seqRow = Field("Key sequence", seqStack);
        root.Children.Add(_seqRow);

        _repeatBox = new TextBox
        {
            Width = 120, Height = 26, Text = "0", VerticalContentAlignment = VerticalAlignment.Center
        };
        root.Children.Add(Field("Repeat every (ms)", _repeatBox));

        var addBtn = MakeButton("Add macro", accent: true);
        addBtn.HorizontalAlignment = HorizontalAlignment.Left;
        addBtn.Margin = new Thickness(110, 6, 0, 0);
        addBtn.Click += OnAddMacro;
        root.Children.Add(addBtn);

        _status = new TextBlock
        {
            Opacity = 0.75, Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_status);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

        Loaded += (_, _) => { Refresh(); UpdateFieldVisibility(); };
    }

    private readonly UIElement _shortcutRow;
    private readonly UIElement _argRow;
    private readonly UIElement _seqRow;

    private void UpdateFieldVisibility()
    {
        _shortcutRow.Visibility = _triggerCombo.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        int action = _actionCombo.SelectedIndex;
        _argRow.Visibility = action is ActionOpenUrl or ActionLaunch or ActionTypeText
            ? Visibility.Visible : Visibility.Collapsed;
        _seqRow.Visibility = action == ActionKeySequence ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Trigger shortcut capture ──────────────────────────────────────────────

    private void OnShortcutKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifier(key)) return;

        _capturedVk = KeyInterop.VirtualKeyFromKey(key);
        _capturedMods = (int)(Keyboard.Modifiers & WpfMods);
        _shortcutBox.Text = MacroSpec.DescribeShortcut(_capturedMods, _capturedVk);
    }

    // ── Key-sequence recording ────────────────────────────────────────────────

    private void ToggleKeyRecording()
    {
        _recordingKeys = !_recordingKeys;
        _recordKeysBtn.Content = _recordingKeys ? "Stop recording" : "Record keys";
        if (_recordingKeys)
        {
            _seqBox.Focus();
            SetStatus("Recording keys — press each shortcut; click Stop when done.");
        }
        else SetStatus("Key recording stopped.");
    }

    private void OnSequenceKey(object sender, KeyEventArgs e)
    {
        if (!_recordingKeys) return; // let the box be edited by hand when not recording
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { ToggleKeyRecording(); return; }
        if (IsModifier(key)) return;

        int vk = KeyInterop.VirtualKeyFromKey(key);
        int mods = (int)(Keyboard.Modifiers & WpfMods);
        AppendChord(MacroSpec.DescribeShortcut(mods, vk));
    }

    private void AppendChord(string chord)
    {
        var text = _seqBox.Text.TrimEnd();
        if (text.Length > 0 && !text.EndsWith(";")) text += ";";
        _seqBox.Text = text.Length > 0 ? $"{text} {chord}" : chord;
        _seqBox.CaretIndex = _seqBox.Text.Length;
    }

    // The WPF modifier set we capture. Fully qualified because StudyHud.Core.Services also
    // defines a ModifierKeys enum (used by the macro engine), which would make it ambiguous here.
    private const System.Windows.Input.ModifierKeys WpfMods =
        System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt
        | System.Windows.Input.ModifierKeys.Shift | System.Windows.Input.ModifierKeys.Windows;

    private static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    // ── Mouse-click recording ─────────────────────────────────────────────────

    private async void OnRecordMouse(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording) return;
        var window = Window.GetWindow(this);
        SetStatus("Recording mouse clicks — click what you want, then press Esc to finish.");

        if (window != null) window.WindowState = WindowState.Minimized;
        IReadOnlyList<MouseClickSample> samples;
        try
        {
            samples = await _recorder.RecordUntilEscapeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mouse recording failed.");
            samples = System.Array.Empty<MouseClickSample>();
        }
        if (window != null) { window.WindowState = WindowState.Normal; window.Activate(); }

        if (samples.Count == 0) { SetStatus("No clicks recorded."); return; }

        var arg = MacroSpec.SerializeClicks(samples.Select(s => (s.X, s.Y, s.Button, s.DelayMs)));
        var spec = new MacroSpec
        {
            Name = $"Mouse macro ({samples.Count} click{(samples.Count == 1 ? "" : "s")})",
            Enabled = true,
            TriggerKind = "keyboard",
            VirtualKey = 0,   // no hotkey — run it with Play / Repeat, or give it a trigger later
            Modifiers = 0,
            ActionKind = "mouse_replay",
            ActionArg = arg
        };
        var specs = _manager.Specs.ToList();
        specs.Add(spec);
        _manager.SaveAndApply(specs);
        Refresh();
        SetStatus($"Saved “{spec.Name}”. Use its Play button, or set a Repeat interval and Start it.");
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    private void OnAddMacro(object sender, RoutedEventArgs e)
    {
        var triggerKind = _triggerCombo.SelectedIndex switch { 1 => "mouse4", 2 => "mouse5", _ => "keyboard" };
        int action = _actionCombo.SelectedIndex;
        var actionKind = action switch
        {
            1 => "toggle_hud",
            2 => "workspace_notes",
            3 => "workspace_finder",
            ActionOpenUrl => "open_url",
            ActionLaunch => "launch",
            ActionTypeText => "type_text",
            ActionKeySequence => "key_sequence",
            _ => "capture"
        };

        if (triggerKind == "keyboard" && _capturedVk == 0)
        {
            SetStatus("Click the Shortcut box and press a key combination first.");
            return;
        }

        string? arg;
        if (action == ActionKeySequence)
        {
            var seq = _seqBox.Text.Trim();
            if (MacroSpec.ParseSequence(seq).Count == 0)
            {
                SetStatus("Enter or record at least one key or shortcut (e.g. Ctrl+C; Ctrl+V).");
                return;
            }
            arg = seq;
        }
        else
        {
            arg = _argBox.Text.Trim();
            if (action is ActionOpenUrl or ActionLaunch or ActionTypeText && string.IsNullOrWhiteSpace(arg))
            {
                SetStatus("This action needs a URL / program path / text in the field.");
                return;
            }
            arg = string.IsNullOrWhiteSpace(arg) ? null : arg;
        }

        int repeatMs = int.TryParse(_repeatBox.Text.Trim(), out var r) ? Math.Clamp(r, 0, 3_600_000) : 0;

        var spec = new MacroSpec
        {
            Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? _actionCombo.SelectedItem?.ToString() ?? "Macro" : _nameBox.Text.Trim(),
            Enabled = true,
            TriggerKind = triggerKind,
            VirtualKey = triggerKind == "keyboard" ? _capturedVk : 0,
            Modifiers = triggerKind == "keyboard" ? _capturedMods : 0,
            ActionKind = actionKind,
            ActionArg = arg,
            RepeatIntervalMs = repeatMs
        };

        var specs = _manager.Specs.ToList();
        specs.Add(spec);
        _manager.SaveAndApply(specs);

        // Reset the form
        _nameBox.Clear();
        _argBox.Clear();
        _seqBox.Clear();
        _repeatBox.Text = "0";
        _capturedVk = 0;
        _capturedMods = 0;
        _shortcutBox.Text = "Click here and press keys…";
        SetStatus($"Added “{spec.Name}”.");
        Refresh();
    }

    private void Refresh()
    {
        _list.Children.Clear();
        var specs = _manager.Specs;

        if (specs.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "No macros. Add one below, or Restore defaults.",
                Opacity = 0.6,
                Foreground = Brush("SecondaryText", Colors.Gray)
            });
            return;
        }

        foreach (var spec in specs)
            _list.Children.Add(BuildRow(spec));
    }

    private UIElement BuildRow(MacroSpec spec)
    {
        var card = new Border
        {
            Background = Brush("SecondaryBackground", Color.FromArgb(180, 40, 40, 48)),
            BorderBrush = Brush("PanelBorder", Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var dock = new DockPanel();

        // Right-side action buttons (added right-to-left).
        var del = MakeButton("Delete", accent: false);
        del.Click += (_, _) =>
        {
            var specs = _manager.Specs.Where(s => s.Id != spec.Id).ToList();
            _manager.SaveAndApply(specs);
            SetStatus($"Deleted “{spec.Name}”.");
            Refresh();
        };
        DockPanel.SetDock(del, Dock.Right);
        dock.Children.Add(del);

        var repeat = MakeButton(_manager.IsRepeating(spec) ? "Stop" : RepeatLabel(spec), accent: false);
        repeat.Margin = new Thickness(0, 0, 6, 0);
        repeat.Click += (_, _) =>
        {
            bool on = _manager.ToggleRepeat(spec);
            repeat.Content = on ? "Stop" : RepeatLabel(spec);
            SetStatus(on ? $"Repeating “{spec.Name}”." : $"Stopped “{spec.Name}”.");
        };
        DockPanel.SetDock(repeat, Dock.Right);
        dock.Children.Add(repeat);

        var play = MakeButton("Play", accent: false);
        play.Margin = new Thickness(0, 0, 6, 0);
        play.Click += (_, _) => { _manager.PlayOnce(spec); SetStatus($"Ran “{spec.Name}”."); };
        DockPanel.SetDock(play, Dock.Right);
        dock.Children.Add(play);

        var enabled = new CheckBox
        {
            IsChecked = spec.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Foreground = Brush("PrimaryText", Colors.White),
            ToolTip = "Enable / disable this macro"
        };
        enabled.Click += (_, _) =>
        {
            var specs = _manager.Specs
                .Select(s => s.Id == spec.Id ? s with { Enabled = enabled.IsChecked == true } : s)
                .ToList();
            _manager.SaveAndApply(specs);
            SetStatus($"{(enabled.IsChecked == true ? "Enabled" : "Disabled")} “{spec.Name}”.");
        };
        DockPanel.SetDock(enabled, Dock.Left);
        dock.Children.Add(enabled);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = spec.Name,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryText", Colors.White)
        });
        var detail = spec.VirtualKey == 0 && spec.TriggerKind == "keyboard"
            ? spec.ActionText()                                   // recorded / no-trigger macro
            : $"{spec.TriggerText()}  →  {spec.ActionText()}";
        if (spec.RepeatIntervalMs > 0) detail += $"   ·   every {spec.RepeatIntervalMs} ms";
        text.Children.Add(new TextBlock
        {
            Text = detail,
            Opacity = 0.6,
            FontSize = 11,
            Foreground = Brush("SecondaryText", Colors.Gray),
            TextWrapping = TextWrapping.Wrap
        });
        dock.Children.Add(text);

        card.Child = dock;
        return card;
    }

    private static string RepeatLabel(MacroSpec spec) =>
        spec.RepeatIntervalMs > 0 ? $"Repeat {spec.RepeatIntervalMs}ms" : "Repeat 1s";

    // ── UI helpers ───────────────────────────────────────────────────────────

    private TextBlock Title(string t) => new()
    {
        Text = t, FontSize = 20, FontWeight = FontWeights.SemiBold,
        Foreground = Brush("PrimaryText", Colors.White), Margin = new Thickness(0, 0, 0, 8)
    };

    private TextBlock Header(string t) => new()
    {
        Text = t, FontSize = 10, Opacity = 0.5,
        Foreground = Brush("SecondaryText", Colors.Gray), Margin = new Thickness(0, 16, 0, 6)
    };

    private UIElement Field(string label, UIElement control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(new TextBlock
        {
            Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0),
            Opacity = 0.85, Foreground = Brush("PrimaryText", Colors.White)
        });
        row.Children.Add(control);
        return row;
    }

    private void SetStatus(string msg) => _status.Text = msg;

    private Button MakeButton(string content, bool accent) => new()
    {
        Content = content,
        Padding = new Thickness(12, 4, 12, 4),
        Cursor = Cursors.Hand,
        BorderThickness = new Thickness(0),
        Foreground = accent ? Brushes.White : Brush("SecondaryText", Colors.Gray),
        Background = accent ? Brush("Accent", Color.FromRgb(0, 180, 255)) : Brushes.Transparent
    };

    private Brush Brush(string token, Color fallback)
        => TryFindResource(token) as Brush ?? new SolidColorBrush(fallback);
}
