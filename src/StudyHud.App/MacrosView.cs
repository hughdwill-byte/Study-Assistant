using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using StudyHud.Macros;

namespace StudyHud.App;

/// <summary>
/// Macros editor (spec §29, §30): lists the user's macros with enable/delete, and an add-macro form
/// (trigger = a captured keyboard shortcut or a mouse side button; action = a fixed set). Changes are
/// saved and applied live through <see cref="MacroManager"/>. Built in code (no XAML).
/// </summary>
public sealed class MacrosView : UserControl
{
    private readonly MacroManager _manager;
    private readonly ILogger<MacrosView> _logger;

    private readonly StackPanel _list;
    private readonly TextBlock _status;

    // Add-form controls
    private readonly TextBox _nameBox;
    private readonly ComboBox _triggerCombo;
    private readonly TextBox _shortcutBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _argBox;

    private int _capturedVk;
    private int _capturedMods;

    public MacrosView(MacroManager manager, ILogger<MacrosView> logger)
    {
        _manager = manager;
        _logger = logger;

        var root = new StackPanel { Margin = new Thickness(4) };

        root.Children.Add(Title("Macros"));
        root.Children.Add(new TextBlock
        {
            Text = "A macro runs an action from a trigger. Keyboard shortcuts work everywhere; mouse "
                 + "side-button macros run while another app is focused.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Foreground = Brush("SecondaryText", Colors.Gray),
            Margin = new Thickness(0, 0, 0, 14)
        });

        // ── Existing macros ──────────────────────────────────────────────────
        root.Children.Add(Header("YOUR MACROS"));
        _list = new StackPanel();
        root.Children.Add(_list);

        var restore = MakeButton("Restore defaults", accent: false);
        restore.HorizontalAlignment = HorizontalAlignment.Left;
        restore.Margin = new Thickness(0, 6, 0, 0);
        restore.Click += (_, _) =>
        {
            _manager.SaveAndApply(MacroSpec.Defaults());
            Refresh();
        };
        root.Children.Add(restore);

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
            "Open URL", "Launch program", "Type text"
        }) _actionCombo.Items.Add(a);
        _actionCombo.SelectedIndex = 0;
        _actionCombo.SelectionChanged += (_, _) => UpdateFieldVisibility();
        root.Children.Add(Field("Action", _actionCombo));

        _argBox = new TextBox { Width = 260, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        _argRow = Field("URL / program / text", _argBox);
        root.Children.Add(_argRow);

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

    private void UpdateFieldVisibility()
    {
        _shortcutRow.Visibility = _triggerCombo.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Argument only matters for Open URL / Launch / Type text (indexes 4,5,6).
        _argRow.Visibility = _actionCombo.SelectedIndex >= 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnShortcutKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return; // wait for a non-modifier key

        _capturedVk = KeyInterop.VirtualKeyFromKey(key);
        _capturedMods = (int)(Keyboard.Modifiers &
            (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows));
        _shortcutBox.Text = MacroSpec.DescribeShortcut(_capturedMods, _capturedVk);
    }

    private void OnAddMacro(object sender, RoutedEventArgs e)
    {
        var triggerKind = _triggerCombo.SelectedIndex switch { 1 => "mouse4", 2 => "mouse5", _ => "keyboard" };
        var actionKind = _actionCombo.SelectedIndex switch
        {
            1 => "toggle_hud",
            2 => "workspace_notes",
            3 => "workspace_finder",
            4 => "open_url",
            5 => "launch",
            6 => "type_text",
            _ => "capture"
        };

        if (triggerKind == "keyboard" && _capturedVk == 0)
        {
            SetStatus("Click the Shortcut box and press a key combination first.");
            return;
        }
        var arg = _argBox.Text.Trim();
        if (_actionCombo.SelectedIndex >= 4 && string.IsNullOrWhiteSpace(arg))
        {
            SetStatus("This action needs a URL / program path / text in the field.");
            return;
        }

        var spec = new MacroSpec
        {
            Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? _actionCombo.SelectedItem?.ToString() ?? "Macro" : _nameBox.Text.Trim(),
            Enabled = true,
            TriggerKind = triggerKind,
            VirtualKey = triggerKind == "keyboard" ? _capturedVk : 0,
            Modifiers = triggerKind == "keyboard" ? _capturedMods : 0,
            ActionKind = actionKind,
            ActionArg = string.IsNullOrWhiteSpace(arg) ? null : arg
        };

        var specs = _manager.Specs.ToList();
        specs.Add(spec);
        _manager.SaveAndApply(specs);

        // Reset the form
        _nameBox.Clear();
        _argBox.Clear();
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
        text.Children.Add(new TextBlock
        {
            Text = $"{spec.TriggerText()}  →  {spec.ActionText()}",
            Opacity = 0.6,
            FontSize = 11,
            Foreground = Brush("SecondaryText", Colors.Gray),
            TextWrapping = TextWrapping.Wrap
        });
        dock.Children.Add(text);

        card.Child = dock;
        return card;
    }

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
            Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center,
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
