using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;

namespace StudyHud.Overlay;

/// <summary>
/// Coordinates the pieces of state that must move together when the active workspace changes
/// (spec §22, §29, §134): it persists the outgoing workspace's panel layout, restores the
/// incoming workspace's layout, and (optionally) switches the associated macro profile.
///
/// This is the service the rest of the codebase already assumed existed — see the comment in
/// <c>ApplicationStateService.SwitchWorkspaceAsync</c>. Keeping the coordination here keeps
/// <c>ApplicationStateService</c> a pure state store with no knowledge of overlays, layouts,
/// or macros.
///
/// It depends only on Core abstractions (<see cref="ILayoutService"/>, <see cref="ISettingsStore"/>,
/// <see cref="IMacroProfileSwitcher"/>) plus the same-assembly <see cref="OverlayManager"/>, so the
/// overlay layer never has to reference the macro engine directly.
/// </summary>
public sealed class WorkspaceCoordinator : IDisposable
{
    private readonly IApplicationStateService _appState;
    private readonly OverlayManager _overlays;
    private readonly ILayoutService _layouts;
    private readonly ISettingsStore _settings;
    private readonly IMonitorService _monitors;
    private readonly IMacroProfileSwitcher _profiles;
    private readonly ILogger<WorkspaceCoordinator> _logger;

    private bool _started;

    public WorkspaceCoordinator(
        IApplicationStateService appState,
        OverlayManager overlays,
        ILayoutService layouts,
        ISettingsStore settings,
        IMonitorService monitors,
        IMacroProfileSwitcher profiles,
        ILogger<WorkspaceCoordinator> logger)
    {
        _appState = appState;
        _overlays = overlays;
        _layouts = layouts;
        _settings = settings;
        _monitors = monitors;
        _profiles = profiles;
        _logger = logger;
    }

    /// <summary>
    /// Begins coordinating. Applies the layout + macro profile for the current workspace, then
    /// listens for future workspace changes. Call once, after overlays are initialised.
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        _appState.StateChanged += OnStateChanged;

        var current = _appState.Current.CurrentWorkspace;

        // Defer the initial restore until panels have been laid out at least once, otherwise
        // ActualWidth/Height are still 0 and normalised positions cannot be resolved.
        Dispatch(() => { _ = ApplyWorkspaceAsync(current); }, DispatcherPriority.Loaded);
        ApplyMacroProfileFor(current);

        _logger.LogInformation("WorkspaceCoordinator started (workspace={Workspace}).", current);
    }

    private void OnStateChanged(object? sender, ApplicationStateChangedEventArgs e)
    {
        if (e.Previous.CurrentWorkspace == e.Current.CurrentWorkspace) return;

        var previous = e.Previous.CurrentWorkspace;
        var next = e.Current.CurrentWorkspace;

        // Persist the layout we are leaving, then restore the one we are entering.
        Dispatch(async () =>
        {
            await SaveWorkspaceAsync(previous).ConfigureAwait(true);
            await ApplyWorkspaceAsync(next).ConfigureAwait(true);
            ApplyMacroProfileFor(next);
        });
    }

    /// <summary>Persists the current on-screen layout for <paramref name="workspace"/>.</summary>
    public async Task SaveWorkspaceAsync(WorkspaceId workspace)
    {
        try
        {
            var layouts = _overlays.CollectLayouts(workspace);
            if (layouts.Count == 0) return;
            await _layouts.SaveLayoutAsync(LayoutIdFor(workspace), layouts).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save layout for workspace {Workspace}.", workspace);
        }
    }

    /// <summary>Saves whatever workspace is currently active. Call on app shutdown.</summary>
    public Task SaveCurrentAsync() => SaveWorkspaceAsync(_appState.Current.CurrentWorkspace);

    private async Task ApplyWorkspaceAsync(WorkspaceId workspace)
    {
        try
        {
            var stored = await _layouts.LoadLayoutAsync(LayoutIdFor(workspace)).ConfigureAwait(true);
            if (stored.Count == 0) return;

            // Move any panels whose saved monitor is gone onto a monitor that still exists (spec §160).
            var recovered = _layouts.RecoverPanelsForCurrentMonitors(stored, _monitors.Monitors);
            _overlays.ApplyLayouts(recovered);
            _logger.LogDebug("Applied {Count} saved panels for workspace {Workspace}.",
                recovered.Count, workspace);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply layout for workspace {Workspace}.", workspace);
        }
    }

    private void ApplyMacroProfileFor(WorkspaceId workspace)
    {
        var settings = _settings.Current;
        if (!settings.AutoSwitchMacroProfile) return;
        if (settings.WorkspaceMacroProfiles.TryGetValue(workspace, out var profileId)
            && !string.IsNullOrWhiteSpace(profileId))
        {
            _profiles.SetActiveProfile(profileId);
            _logger.LogDebug("Auto-switched macro profile to '{Profile}' for workspace {Workspace}.",
                profileId, workspace);
        }
    }

    private static string LayoutIdFor(WorkspaceId workspace) => $"workspace-{workspace}";

    private static void Dispatch(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action, priority);
    }

    private static void Dispatch(Func<Task> asyncAction, DispatcherPriority priority = DispatcherPriority.Normal)
        => Dispatch(() => { _ = asyncAction(); }, priority);

    public void Dispose()
    {
        if (!_started) return;
        _appState.StateChanged -= OnStateChanged;
        _started = false;
    }
}
