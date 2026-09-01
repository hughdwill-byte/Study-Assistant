using StudyHud.Core.Models;

namespace StudyHud.Core.Services;

/// <summary>
/// Central application state manager (spec §125, §126).
/// All state mutations go through this service so changes propagate predictably.
/// Components subscribe to StateChanged rather than maintaining their own copies.
/// </summary>
public interface IApplicationStateService
{
    /// <summary>Current immutable state snapshot.</summary>
    ApplicationState Current { get; }

    /// <summary>Raised whenever state changes. Always raised on the UI thread.</summary>
    event EventHandler<ApplicationStateChangedEventArgs> StateChanged;

    /// <summary>
    /// Apply a transformation to the current state and publish it.
    /// The transform receives the current state and returns a new one.
    /// Thread-safe.
    /// </summary>
    void Update(Func<ApplicationState, ApplicationState> transform);

    // Convenience methods for common state transitions

    /// <summary>Switches workspace (spec §134). Coordinates workspace + layout + macro profile.</summary>
    Task SwitchWorkspaceAsync(WorkspaceId workspace, CancellationToken cancellationToken = default);

    /// <summary>Sets the active course (spec §154).</summary>
    Task SetCourseAsync(string courseId, CancellationToken cancellationToken = default);

    /// <summary>Enters or exits Assessment Mode (spec §151, §153).</summary>
    Task SetAssessmentModeAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Transitions the HUD interaction state (spec §127).</summary>
    void SetHudInteractionState(HudInteractionState newState);

    /// <summary>Sets global HUD visibility (Panic Hide — spec §7).</summary>
    void SetHudVisible(bool visible);

    /// <summary>Enters/exits capture mode (spec §139).</summary>
    void SetCaptureModeActive(bool active);
}

public class ApplicationStateChangedEventArgs : EventArgs
{
    public required ApplicationState Previous { get; init; }
    public required ApplicationState Current { get; init; }
}
