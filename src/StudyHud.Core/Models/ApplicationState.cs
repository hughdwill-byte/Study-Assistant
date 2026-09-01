using System.ComponentModel;

namespace StudyHud.Core.Models;

/// <summary>
/// Workspace identifiers built into the application.
/// Additional workspaces can be added in future versions.
/// </summary>
public enum WorkspaceId
{
    NoteTaking,
    QuestionFinder
}

/// <summary>
/// The three interaction states of the HUD (spec §5, §127).
/// Implemented as an explicit state machine — not scattered boolean flags.
/// </summary>
public enum HudInteractionState
{
    /// <summary>Default studying state: visible but click-through.</summary>
    Ghost,
    /// <summary>HUD is interactable (Hold-to-Interact is active or toggled).</summary>
    Active,
    /// <summary>User is editing panel layout/configuration.</summary>
    Edit
}

/// <summary>
/// Visibility state of an individual panel (spec §14, §130).
/// Separate from interaction state.
/// </summary>
public enum PanelVisibilityState
{
    Expanded,
    EdgeCollapsed,
    Hidden
}

/// <summary>
/// Responsive layout state of a panel (spec §10, §133).
/// </summary>
public enum PanelResponsiveState
{
    Compact,
    Normal,
    Expanded
}

/// <summary>
/// Docking relationship state for a panel (spec §12, §130).
/// </summary>
public enum PanelDockState
{
    Floating,
    Docked,
    MemberOfDockGroup
}

/// <summary>
/// Direction a panel collapses to its nearest monitor edge (spec §13).
/// </summary>
public enum CollapseDirection
{
    None,
    Left,
    Right,
    Top,
    Bottom
}

/// <summary>
/// Central immutable snapshot of all application-level state (spec §125).
/// Changes are raised through IApplicationStateService and propagate to all listeners.
/// </summary>
public record ApplicationState
{
    public WorkspaceId CurrentWorkspace { get; init; } = WorkspaceId.NoteTaking;
    public string? CurrentCourseId { get; init; }
    public string? CurrentMacroProfileId { get; init; }
    public HudInteractionState HudInteractionState { get; init; } = HudInteractionState.Ghost;
    public bool HudVisible { get; init; } = true;
    public bool AssessmentModeActive { get; init; } = false;
    public bool IsCaptureModeActive { get; init; } = false;
    public string? ForegroundExcludedAppName { get; init; }
    public bool IsInExcludedApplication { get; init; } = false;
}
