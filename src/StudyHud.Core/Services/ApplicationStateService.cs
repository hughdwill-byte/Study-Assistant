using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Core.Services;

/// <summary>
/// Central application state store (spec §125, §126).
/// Thread-safe; always raises StateChanged on the calling thread
/// (callers responsible for dispatching to UI thread if needed).
/// </summary>
public sealed class ApplicationStateService : IApplicationStateService
{
    private readonly ILogger<ApplicationStateService> _logger;
    private ApplicationState _state = new();
    private readonly object _lock = new();

    public ApplicationStateService(ILogger<ApplicationStateService> logger)
    {
        _logger = logger;
    }

    public ApplicationState Current
    {
        get { lock (_lock) return _state; }
    }

    public event EventHandler<ApplicationStateChangedEventArgs>? StateChanged;

    public void Update(Func<ApplicationState, ApplicationState> transform)
    {
        ApplicationState previous, next;
        lock (_lock)
        {
            previous = _state;
            next = transform(previous);
            if (ReferenceEquals(previous, next)) return;
            _state = next;
        }

        _logger.LogTrace("State updated: workspace={W}, assessment={A}, hudState={H}",
            next.CurrentWorkspace, next.AssessmentModeActive, next.HudInteractionState);

        StateChanged?.Invoke(this, new ApplicationStateChangedEventArgs
        {
            Previous = previous,
            Current = next
        });
    }

    public async Task SwitchWorkspaceAsync(WorkspaceId workspace, CancellationToken ct = default)
    {
        _logger.LogInformation("Switching workspace to {Workspace}.", workspace);

        // Full workspace switch process (spec §134)
        // 1. Cancel incompatible transient operations (capture mode)
        Update(s => s with { IsCaptureModeActive = false });

        // 2–3. Change workspace
        Update(s => s with { CurrentWorkspace = workspace });

        // Step 4–11 (macro profile, layout) are handled by WorkspaceCoordinator
        // which listens to StateChanged — this keeps ApplicationStateService simple.

        await Task.CompletedTask;
    }

    public async Task SetCourseAsync(string courseId, CancellationToken ct = default)
    {
        _logger.LogInformation("Setting course to {CourseId}.", courseId);
        Update(s => s with { CurrentCourseId = courseId });
        await Task.CompletedTask;
    }

    public async Task SetAssessmentModeAsync(bool enabled, CancellationToken ct = default)
    {
        _logger.LogInformation("Assessment mode {State}.", enabled ? "enabled" : "disabled");
        Update(s => s with { AssessmentModeActive = enabled });
        await Task.CompletedTask;
    }

    public void SetHudInteractionState(HudInteractionState newState)
    {
        // Enforce priority model (spec §126):
        // Panic Hide overrides everything — handled by SetHudVisible.
        // Assessment mode doesn't affect HUD interaction state.
        var current = Current;

        // Ghost ↔ Active: only valid outside Edit (spec §127)
        if (newState == HudInteractionState.Ghost || newState == HudInteractionState.Active)
        {
            if (current.HudInteractionState == HudInteractionState.Edit)
            {
                // Releasing Hold-to-Interact must NOT exit Edit Mode (spec §126)
                _logger.LogTrace("Ignoring interaction state change to {New} while in Edit mode.", newState);
                return;
            }
        }

        Update(s => s with { HudInteractionState = newState });
    }

    public void SetHudVisible(bool visible)
    {
        Update(s => s with { HudVisible = visible });
        _logger.LogDebug("HUD visibility set to {Visible}.", visible);
    }

    public void SetCaptureModeActive(bool active)
    {
        Update(s => s with { IsCaptureModeActive = active });
    }
}
