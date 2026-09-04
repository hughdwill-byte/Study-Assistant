using StudyHud.Core.Services;
using StudyHud.Macros.Models;

namespace StudyHud.Macros;

/// <summary>
/// Built-in starter macros so the macro system does something out of the box (spec §29, §30).
/// A full macro editor to add/edit your own is the next step; these are loaded at startup.
/// </summary>
public static class DefaultMacros
{
    public const string ProfileId = "default";

    // Virtual-key codes
    private const int VK_G = 0x47;

    public static IReadOnlyList<MacroDefinition> All() =>
    [
        new MacroDefinition
        {
            Id = "capture-note",
            Name = "Capture Note — mouse button 4",
            Description = "Draw a box on screen; the shot is copied to the clipboard and saved to "
                        + "%LOCALAPPDATA%\\StudyHud\\Notes.",
            Trigger = new MacroTrigger
            {
                Type = TriggerType.MouseSideButton,
                MouseButton = 4,
                Semantic = TriggerSemantic.Press
            },
            Actions = [new CaptureRegionAction()]
        },
        new MacroDefinition
        {
            Id = "toggle-hud",
            Name = "Toggle HUD — Ctrl + Shift + G",
            Description = "Show or hide the whole HUD overlay.",
            Trigger = new MacroTrigger
            {
                Type = TriggerType.KeyboardShortcut,
                VirtualKey = VK_G,
                Modifiers = (int)(ModifierKeys.Control | ModifierKeys.Shift),
                Semantic = TriggerSemantic.Press
            },
            Actions = [new ToggleHudAction()]
        }
    ];

    public static MacroProfile Profile() => new()
    {
        ProfileId = ProfileId,
        Name = "Default",
        MacroIds = All().Select(m => m.Id).ToList()
    };
}
