using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Macros.Models;

namespace StudyHud.Macros;

/// <summary>
/// A flat, serialisable description of a user macro (spec §30). The engine's <see cref="MacroAction"/>
/// hierarchy is polymorphic and awkward to persist, so the editor and store work with this DTO and
/// convert to a <see cref="MacroDefinition"/> via <see cref="ToDefinition"/>.
/// </summary>
public sealed record MacroSpec
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; init; } = "New macro";
    public bool Enabled { get; init; } = true;

    /// <summary>"keyboard", "mouse4" or "mouse5".</summary>
    public string TriggerKind { get; init; } = "keyboard";
    public int VirtualKey { get; init; }
    public int Modifiers { get; init; } // bitmask of Core.Services.ModifierKeys

    /// <summary>
    /// "capture", "toggle_hud", "workspace_notes", "workspace_finder", "open_url", "launch",
    /// "type_text", "key_sequence" (ActionArg = chords like "Ctrl+C; Ctrl+V; Enter"),
    /// "mouse_replay" (ActionArg = recorded clicks "x,y,btn,delay|…").
    /// </summary>
    public string ActionKind { get; init; } = "capture";
    public string? ActionArg { get; init; }

    /// <summary>
    /// When &gt; 0, the macro can be auto-repeated on this interval (milliseconds) from the editor's
    /// Repeat toggle (spec §30). 0 = no auto-repeat. Never fires on its own — the user starts it.
    /// </summary>
    public int RepeatIntervalMs { get; init; } = 0;

    public bool IsKeyboard => TriggerKind == "keyboard";

    public MacroDefinition ToDefinition()
    {
        var trigger = TriggerKind switch
        {
            "mouse4" => new MacroTrigger { Type = TriggerType.MouseSideButton, MouseButton = 4, Semantic = TriggerSemantic.Press },
            "mouse5" => new MacroTrigger { Type = TriggerType.MouseSideButton, MouseButton = 5, Semantic = TriggerSemantic.Press },
            _ => new MacroTrigger { Type = TriggerType.KeyboardShortcut, VirtualKey = VirtualKey, Modifiers = Modifiers, Semantic = TriggerSemantic.Press }
        };

        // key_sequence and mouse_replay expand to a list of actions; the rest are a single action.
        IReadOnlyList<MacroAction> actions = ActionKind switch
        {
            "key_sequence" => BuildKeySequenceActions(ActionArg),
            "mouse_replay" => BuildMouseReplayActions(ActionArg),
            _ => [SingleAction()]
        };

        return new MacroDefinition
        {
            Id = Id,
            Name = string.IsNullOrWhiteSpace(Name) ? Id : Name,
            Enabled = Enabled,
            Trigger = trigger,
            Actions = actions
        };
    }

    private MacroAction SingleAction() => ActionKind switch
    {
        "toggle_hud" => new ToggleHudAction(),
        "workspace_notes" => new SwitchWorkspaceAction { ActionType = MacroActionType.SwitchWorkspace, TargetWorkspace = WorkspaceId.NoteTaking },
        "workspace_finder" => new SwitchWorkspaceAction { ActionType = MacroActionType.SwitchWorkspace, TargetWorkspace = WorkspaceId.QuestionFinder },
        "open_url" => new OpenUrlAction { ActionType = MacroActionType.OpenUrl, Url = ActionArg ?? "" },
        "launch" => new LaunchProgramAction { ActionType = MacroActionType.LaunchProgram, Path = ActionArg ?? "" },
        "type_text" => new TypeTextAction { ActionType = MacroActionType.TypeText, Text = ActionArg ?? "" },
        _ => new CaptureRegionAction()
    };

    // ── Key-sequence macros ───────────────────────────────────────────────────

    /// <summary>
    /// Expands "Ctrl+C; Ctrl+V; Enter" into KeyPress actions separated by a short delay so the target
    /// app keeps up. Chords are separated by ';' (or a newline); parts of a chord by '+'.
    /// </summary>
    private static IReadOnlyList<MacroAction> BuildKeySequenceActions(string? arg)
    {
        var list = new List<MacroAction>();
        foreach (var (mods, vk) in ParseSequence(arg))
        {
            if (list.Count > 0)
                list.Add(new DelayAction { ActionType = MacroActionType.Delay, Milliseconds = 30 });
            list.Add(new KeyPressAction { ActionType = MacroActionType.KeyPress, VirtualKey = vk, Modifiers = mods });
        }
        return list.Count > 0 ? list : [new DelayAction { ActionType = MacroActionType.Delay, Milliseconds = 1 }];
    }

    /// <summary>Parses a chord sequence string into (modifiers, virtual-key) pairs.</summary>
    public static IReadOnlyList<(int Modifiers, int VirtualKey)> ParseSequence(string? arg)
    {
        var result = new List<(int, int)>();
        if (string.IsNullOrWhiteSpace(arg)) return result;
        foreach (var chord in arg.Split(new[] { ';', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parsed = ParseChord(chord.Trim());
            if (parsed.VirtualKey != 0) result.Add(parsed);
        }
        return result;
    }

    private static (int Modifiers, int VirtualKey) ParseChord(string chord)
    {
        int mods = 0, vk = 0;
        foreach (var raw in chord.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control" or "ctl": mods |= (int)ModifierKeys.Control; break;
                case "alt": mods |= (int)ModifierKeys.Alt; break;
                case "shift": mods |= (int)ModifierKeys.Shift; break;
                case "win" or "windows" or "meta": mods |= (int)ModifierKeys.Win; break;
                default: vk = NameToVk(part); break;
            }
        }
        return (mods, vk);
    }

    // ── Mouse-replay macros ───────────────────────────────────────────────────

    /// <summary>Serialises recorded clicks "x,y,btn,delayMs" joined by '|'.</summary>
    public static string SerializeClicks(IEnumerable<(int X, int Y, int Button, int DelayMs)> clicks) =>
        string.Join("|", clicks.Select(c => $"{c.X},{c.Y},{c.Button},{c.DelayMs}"));

    private static IReadOnlyList<MacroAction> BuildMouseReplayActions(string? arg)
    {
        var list = new List<MacroAction>();
        if (string.IsNullOrWhiteSpace(arg))
            return [new DelayAction { ActionType = MacroActionType.Delay, Milliseconds = 1 }];
        foreach (var token in arg.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = token.Split(',');
            if (p.Length < 4) continue;
            if (!int.TryParse(p[0], out var x) || !int.TryParse(p[1], out var y) ||
                !int.TryParse(p[2], out var btn) || !int.TryParse(p[3], out var delay)) continue;
            if (delay > 0)
                list.Add(new DelayAction { ActionType = MacroActionType.Delay, Milliseconds = Math.Clamp(delay, 0, 60000) });
            list.Add(new MouseClickAction { ActionType = MacroActionType.MouseClick, X = x, Y = y, Button = btn });
        }
        return list.Count > 0 ? list : [new DelayAction { ActionType = MacroActionType.Delay, Milliseconds = 1 }];
    }

    private static int ClickCount(string? arg) =>
        string.IsNullOrWhiteSpace(arg) ? 0 : arg.Split('|', StringSplitOptions.RemoveEmptyEntries).Length;

    public string TriggerText() => TriggerKind switch
    {
        "mouse4" => "Mouse button 4",
        "mouse5" => "Mouse button 5",
        _ => DescribeShortcut(Modifiers, VirtualKey)
    };

    public string ActionText() => ActionKind switch
    {
        "toggle_hud" => "Toggle HUD",
        "workspace_notes" => "Switch to Note Taking",
        "workspace_finder" => "Switch to Question Finder",
        "open_url" => $"Open URL: {ActionArg}",
        "launch" => $"Launch: {ActionArg}",
        "type_text" => $"Type: {ActionArg}",
        "key_sequence" => $"Send keys: {ActionArg}",
        "mouse_replay" => $"Replay {ClickCount(ActionArg)} mouse click(s)",
        _ => "Capture Note"
    };

    public static string DescribeShortcut(int modifiers, int vk)
    {
        var parts = new List<string>();
        if ((modifiers & (int)ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & (int)ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & (int)ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & (int)ModifierKeys.Win) != 0) parts.Add("Win");
        parts.Add(KeyName(vk));
        return string.Join(" + ", parts);
    }

    private static string KeyName(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),           // A–Z
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),           // 0–9
        >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),               // F1–F12
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1B => "Esc",
        0x08 => "Backspace",
        0x2E => "Delete",
        0x26 => "Up", 0x28 => "Down", 0x25 => "Left", 0x27 => "Right",
        0x24 => "Home", 0x23 => "End", 0x21 => "PageUp", 0x22 => "PageDown",
        0 => "(none)",
        _ => "0x" + vk.ToString("X2")
    };

    /// <summary>
    /// Maps a key name (as typed in a key-sequence macro) to a Win32 virtual-key code. Supports A–Z,
    /// 0–9, F1–F12, and the common named keys. Returns 0 for an unrecognised name.
    /// </summary>
    public static int NameToVk(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        name = name.Trim();
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        if ((name[0] is 'f' or 'F') && int.TryParse(name[1..], out var n) && n is >= 1 and <= 12)
            return 0x6F + n;
        return name.ToLowerInvariant() switch
        {
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "space" or "spacebar" => 0x20,
            "backspace" or "back" => 0x08,
            "delete" or "del" => 0x2E,
            "up" => 0x26, "down" => 0x28, "left" => 0x25, "right" => 0x27,
            "home" => 0x24, "end" => 0x23, "pageup" or "pgup" => 0x21, "pagedown" or "pgdn" => 0x22,
            _ => 0
        };
    }

    /// <summary>The built-in starter macros, as specs.</summary>
    public static IReadOnlyList<MacroSpec> Defaults() =>
    [
        new MacroSpec
        {
            Id = "capture-note", Name = "Capture Note",
            TriggerKind = "mouse4", ActionKind = "capture"
        },
        new MacroSpec
        {
            Id = "toggle-hud", Name = "Toggle HUD",
            TriggerKind = "keyboard", VirtualKey = 0x47, // G
            Modifiers = (int)(ModifierKeys.Control | ModifierKeys.Shift),
            ActionKind = "toggle_hud"
        }
    ];
}
