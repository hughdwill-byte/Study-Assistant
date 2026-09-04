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

    /// <summary>"capture", "toggle_hud", "workspace_notes", "workspace_finder", "open_url", "launch", "type_text".</summary>
    public string ActionKind { get; init; } = "capture";
    public string? ActionArg { get; init; }

    public bool IsKeyboard => TriggerKind == "keyboard";

    public MacroDefinition ToDefinition()
    {
        var trigger = TriggerKind switch
        {
            "mouse4" => new MacroTrigger { Type = TriggerType.MouseSideButton, MouseButton = 4, Semantic = TriggerSemantic.Press },
            "mouse5" => new MacroTrigger { Type = TriggerType.MouseSideButton, MouseButton = 5, Semantic = TriggerSemantic.Press },
            _ => new MacroTrigger { Type = TriggerType.KeyboardShortcut, VirtualKey = VirtualKey, Modifiers = Modifiers, Semantic = TriggerSemantic.Press }
        };

        MacroAction action = ActionKind switch
        {
            "toggle_hud" => new ToggleHudAction(),
            "workspace_notes" => new SwitchWorkspaceAction { ActionType = MacroActionType.SwitchWorkspace, TargetWorkspace = WorkspaceId.NoteTaking },
            "workspace_finder" => new SwitchWorkspaceAction { ActionType = MacroActionType.SwitchWorkspace, TargetWorkspace = WorkspaceId.QuestionFinder },
            "open_url" => new OpenUrlAction { ActionType = MacroActionType.OpenUrl, Url = ActionArg ?? "" },
            "launch" => new LaunchProgramAction { ActionType = MacroActionType.LaunchProgram, Path = ActionArg ?? "" },
            "type_text" => new TypeTextAction { ActionType = MacroActionType.TypeText, Text = ActionArg ?? "" },
            _ => new CaptureRegionAction()
        };

        return new MacroDefinition
        {
            Id = Id,
            Name = string.IsNullOrWhiteSpace(Name) ? Id : Name,
            Enabled = Enabled,
            Trigger = trigger,
            Actions = [action]
        };
    }

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
        0 => "(none)",
        _ => "0x" + vk.ToString("X2")
    };

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
