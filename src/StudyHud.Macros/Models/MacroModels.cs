using StudyHud.Core.Models;

namespace StudyHud.Macros.Models;

// ─── Trigger ─────────────────────────────────────────────────────────────────

public enum TriggerType
{
    KeyboardShortcut,
    FunctionKey,
    MouseSideButton,
    MouseButtonHold,
    MouseButtonPress,
    KeyChord
}

public enum TriggerSemantic { Press, Hold, Release }

public record MacroTrigger
{
    public required TriggerType Type { get; init; }
    public int VirtualKey { get; init; }
    public int Modifiers { get; init; }          // bitmask of Core.Services.ModifierKeys
    public int MouseButton { get; init; }         // 4 or 5 for side buttons
    public TriggerSemantic Semantic { get; init; } = TriggerSemantic.Press;
}

// ─── Conditions ──────────────────────────────────────────────────────────────

public record MacroConditions
{
    public WorkspaceId? RequiredWorkspace { get; init; }
    public IReadOnlyList<string>? AllowedApplications { get; init; }
    public IReadOnlyList<string>? BlockedApplications { get; init; }
    public string? RequiredProfileId { get; init; }
    public bool AllowInCapture { get; init; } = false;
}

// ─── Actions ─────────────────────────────────────────────────────────────────

public enum MacroActionType
{
    KeyDown, KeyUp, KeyPress, Shortcut,
    TypeText, Delay, OpenUrl, LaunchProgram, RunCommand,
    CaptureRegion, CopyToClipboard, Paste,
    CollapsePanel, ExpandPanel, TogglePanelCollapse,
    SwitchWorkspace, SwitchMacroProfile,
    HideHud, ShowHud, ToggleHud
}

public enum MacroFailureBehaviour { StopMacro, Continue }

public abstract record MacroAction
{
    public required MacroActionType ActionType { get; init; }
}

public record KeyDownAction : MacroAction
{
    public KeyDownAction() => ActionType = MacroActionType.KeyDown;
    public required int VirtualKey { get; init; }
}

public record KeyUpAction : MacroAction
{
    public KeyUpAction() => ActionType = MacroActionType.KeyUp;
    public required int VirtualKey { get; init; }
}

public record KeyPressAction : MacroAction
{
    public KeyPressAction() => ActionType = MacroActionType.KeyPress;
    public required int VirtualKey { get; init; }
    public int Modifiers { get; init; }
}

public record TypeTextAction : MacroAction
{
    public TypeTextAction() => ActionType = MacroActionType.TypeText;
    public required string Text { get; init; }
    public bool UseClipboardForUnicode { get; init; } = false;
}

public record DelayAction : MacroAction
{
    public DelayAction() => ActionType = MacroActionType.Delay;
    public required int Milliseconds { get; init; }
}

public record CaptureRegionAction : MacroAction
{
    public CaptureRegionAction() => ActionType = MacroActionType.CaptureRegion;
}

public record SwitchWorkspaceAction : MacroAction
{
    public SwitchWorkspaceAction() => ActionType = MacroActionType.SwitchWorkspace;
    public required WorkspaceId TargetWorkspace { get; init; }
}

public record SwitchMacroProfileAction : MacroAction
{
    public SwitchMacroProfileAction() => ActionType = MacroActionType.SwitchMacroProfile;
    public required string ProfileId { get; init; }
}

public record TogglePanelCollapseAction : MacroAction
{
    public TogglePanelCollapseAction() => ActionType = MacroActionType.TogglePanelCollapse;
    public string? PanelId { get; init; } // null = all edge-attached panels
}

public record ToggleHudAction : MacroAction
{
    public ToggleHudAction() => ActionType = MacroActionType.ToggleHud;
}

public record OpenUrlAction : MacroAction
{
    public OpenUrlAction() => ActionType = MacroActionType.OpenUrl;
    public required string Url { get; init; }
}

public record LaunchProgramAction : MacroAction
{
    public LaunchProgramAction() => ActionType = MacroActionType.LaunchProgram;
    public required string Path { get; init; }
    public string? Arguments { get; init; }
}

// ─── Macro Definition ─────────────────────────────────────────────────────────

public record MacroDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; } = true;
    public required MacroTrigger Trigger { get; init; }
    public MacroConditions Conditions { get; init; } = new();
    public required IReadOnlyList<MacroAction> Actions { get; init; }
    public int CooldownMs { get; init; } = 0;
    public MacroFailureBehaviour OnFailure { get; init; } = MacroFailureBehaviour.StopMacro;
    public string? Description { get; init; }
}

// ─── Macro Profile ────────────────────────────────────────────────────────────

public record MacroProfile
{
    public required string ProfileId { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; } = true;
    public required IReadOnlyList<string> MacroIds { get; init; }
    public WorkspaceId? AssociatedWorkspace { get; init; }
    public string? Description { get; init; }
}
