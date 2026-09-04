using StudyHud.Core.Services;

namespace StudyHud.Core.Models;

/// <summary>
/// The kind of input that drives the Hold-to-Interact trigger (spec §6).
/// Kept in Core (framework-neutral) so persisted settings do not depend on the
/// Windows input layer. The Windows layer maps this to its own trigger config.
/// </summary>
public enum HoldTriggerType
{
    KeyboardKey,
    MouseButton
}

/// <summary>
/// A persisted global hotkey binding (spec §7, §22). Modifier bits use
/// <see cref="ModifierKeys"/>. VirtualKey is a Win32 virtual-key code.
/// </summary>
public record HotkeyBinding
{
    public ModifierKeys Modifiers { get; init; } = ModifierKeys.None;
    public int VirtualKey { get; init; }
}

/// <summary>
/// Configuration for the Hold-to-Interact trigger (spec §6). Persisted so the
/// user's chosen trigger survives restarts. Default is Caps Lock, matching the
/// runtime default in <c>HoldToInteractService</c>.
/// </summary>
public record HoldTriggerSettings
{
    public HoldTriggerType Type { get; init; } = HoldTriggerType.KeyboardKey;

    /// <summary>Win32 virtual-key code used when <see cref="Type"/> is KeyboardKey. 0x14 = Caps Lock.</summary>
    public int VirtualKey { get; init; } = 0x14;

    /// <summary>Side-mouse-button number (4 or 5) used when <see cref="Type"/> is MouseButton.</summary>
    public int MouseButton { get; init; } = 5;
}

/// <summary>
/// The complete set of user-configurable settings persisted locally (spec §71 local-first).
/// This record is serialised to <c>%LOCALAPPDATA%\StudyHud\settings.json</c> by
/// <c>ISettingsStore</c>. It never contains secrets — the Notion token lives in the
/// Windows credential store, not here (spec §46).
///
/// All members have safe defaults so a missing or partial file still yields a usable
/// configuration (spec §69 graceful recovery).
/// </summary>
public record StudyHudSettings
{
    /// <summary>Schema version for forward-compatible migration of the settings file.</summary>
    public int SchemaVersion { get; init; } = 1;

    // ── HUD interaction (spec §6, §7, §5.3) ──────────────────────────────────
    public HoldTriggerSettings HoldToInteract { get; init; } = new();

    /// <summary>Panic / hide-HUD shortcut. Default Ctrl+Shift+H (H = 0x48).</summary>
    public HotkeyBinding PanicHideHotkey { get; init; } = new()
    {
        Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
        VirtualKey = 0x48
    };

    /// <summary>Toggle Edit Mode shortcut. Default Ctrl+Shift+E (E = 0x45).</summary>
    public HotkeyBinding ToggleEditModeHotkey { get; init; } = new()
    {
        Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
        VirtualKey = 0x45
    };

    // ── Presentation (spec §21, §62) ─────────────────────────────────────────
    public string ThemeId { get; init; } = "Default";

    /// <summary>Custom accent colour as "#RRGGBB" (spec §64), or null to use the theme's accent.</summary>
    public string? AccentColour { get; init; }

    /// <summary>Snap distance in logical pixels (spec §11, default ~15).</summary>
    public double SnapDistancePixels { get; init; } = 15;

    /// <summary>Whether the small control capsule is shown (spec §23).</summary>
    public bool ShowControlCapsule { get; init; } = true;

    // ── Focus Mode / Pomodoro ────────────────────────────────────────────────
    public int FocusMinutes { get; init; } = 25;
    public int ShortBreakMinutes { get; init; } = 5;
    public int LongBreakMinutes { get; init; } = 15;
    public int LongBreakEveryCycles { get; init; } = 4;

    // ── Session context (spec §22, §43) ──────────────────────────────────────
    public WorkspaceId CurrentWorkspace { get; init; } = WorkspaceId.NoteTaking;
    public string? CurrentCourseId { get; init; }

    /// <summary>
    /// Whether Assessment Mode should be active on startup (spec §41). Assessment
    /// Mode is not silently persisted-off: if the user left it on it stays on.
    /// </summary>
    public bool AssessmentModeActive { get; init; } = false;

    // ── Coordination (spec §29, §134) ────────────────────────────────────────
    /// <summary>
    /// Maps a workspace to the macro profile that should activate when it becomes
    /// current (spec §29). Empty means "do not auto-switch profile".
    /// </summary>
    public Dictionary<WorkspaceId, string> WorkspaceMacroProfiles { get; init; } = new();

    /// <summary>Whether switching workspace also switches macro profile (spec §29, user can disable).</summary>
    public bool AutoSwitchMacroProfile { get; init; } = true;

    // ── Exclusions / background (spec §37, §68) ──────────────────────────────
    /// <summary>Process names (without .exe) where the HUD hides and macros are disabled (spec §37).</summary>
    public List<string> ExcludedApplications { get; init; } = new();

    /// <summary>Hide the HUD automatically in fullscreen applications (spec §37).</summary>
    public bool HideHudInFullscreen { get; init; } = true;

    /// <summary>Pause heavy OCR/index work while on battery / Battery Saver (spec §68).</summary>
    public bool PauseHeavyIndexingOnBattery { get; init; } = true;
}
