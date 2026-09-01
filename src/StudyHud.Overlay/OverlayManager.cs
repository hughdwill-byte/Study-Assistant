using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Theming;
using StudyHud.Windows.Services;

namespace StudyHud.Overlay;

/// <summary>
/// Creates, tracks, and manages one MonitorOverlayWindow + PanelHost per physical monitor.
/// Responds to monitor topology changes (spec §4, §160, §171).
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly IMonitorService _monitors;
    private readonly IApplicationStateService _appState;
    private readonly IThemeService _theme;
    private readonly ICaptureService _capture;
    private readonly IQuestionFinder _finder;
    private readonly IAssessmentPolicyService _policy;
    private readonly ILogger<OverlayManager> _logger;
    private readonly Dictionary<string, MonitorOverlayWindow> _overlays = new();
    private bool _disposed;

    public OverlayManager(
        IMonitorService monitors,
        IApplicationStateService appState,
        IThemeService theme,
        ICaptureService capture,
        IQuestionFinder finder,
        IAssessmentPolicyService policy,
        ILogger<OverlayManager> logger)
    {
        _monitors = monitors;
        _appState = appState;
        _theme = theme;
        _capture = capture;
        _finder = finder;
        _policy = policy;
        _logger = logger;

        _monitors.TopologyChanged += OnTopologyChanged;
    }

    public void Initialise()
    {
        foreach (var monitor in _monitors.Monitors)
            CreateOverlayForMonitor(monitor);

        _logger.LogInformation("OverlayManager initialised with {Count} monitor overlays.", _overlays.Count);
    }

    private void CreateOverlayForMonitor(MonitorInfo monitor)
    {
        if (_overlays.ContainsKey(monitor.MonitorId))
        {
            _logger.LogWarning("Overlay for monitor {Id} already exists — skipping.", monitor.MonitorId);
            return;
        }

        var overlay = new MonitorOverlayWindow(monitor, _appState, _logger);

        // Create a PanelHost and attach it to the overlay
        var host = new PanelHost(monitor, _appState, _theme, _capture, _finder, _policy);
        overlay.SetPanelHost(host);

        _overlays[monitor.MonitorId] = overlay;
        overlay.Show();
        _logger.LogDebug("Created overlay+panels for monitor {Id} ({Device}).",
            monitor.MonitorId, monitor.DeviceName);
    }

    private void DestroyOverlayForMonitor(string monitorId)
    {
        if (_overlays.Remove(monitorId, out var overlay))
        {
            overlay.Close();
            _logger.LogDebug("Removed overlay for monitor {Id}.", monitorId);
        }
    }

    private void OnTopologyChanged(object? sender, MonitorTopologyChangedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var id in e.RemovedMonitorIds)
                DestroyOverlayForMonitor(id);

            foreach (var monitor in e.CurrentMonitors.Where(m => e.AddedMonitorIds.Contains(m.MonitorId)))
                CreateOverlayForMonitor(monitor);

            foreach (var monitor in e.CurrentMonitors.Where(m => e.ChangedMonitorIds.Contains(m.MonitorId)))
            {
                if (_overlays.TryGetValue(monitor.MonitorId, out var overlay))
                    overlay.ApplyMonitorBounds();
            }

            _logger.LogInformation("Overlay topology update complete. Active overlays: {Count}.", _overlays.Count);
        });
    }

    public MonitorOverlayWindow? GetOverlayAtPoint(ScreenPoint physicalPoint)
    {
        var monitor = _monitors.GetMonitorAtPoint(physicalPoint);
        if (monitor == null) return null;
        return _overlays.TryGetValue(monitor.MonitorId, out var overlay) ? overlay : null;
    }

    public IReadOnlyCollection<MonitorOverlayWindow> AllOverlays => _overlays.Values;

    /// <summary>All active panel hosts (one per monitor overlay that has been given a host).</summary>
    public IEnumerable<PanelHost> PanelHosts =>
        _overlays.Values
            .Select(o => o.PanelHost)
            .Where(h => h is not null)
            .Select(h => h!);

    /// <summary>
    /// Aggregates the current on-screen panel layout across every monitor for the given
    /// workspace, ready to persist via <see cref="ILayoutService"/> (spec §19).
    /// </summary>
    public IReadOnlyList<PanelLayout> CollectLayouts(WorkspaceId workspace)
    {
        var all = new List<PanelLayout>();
        foreach (var host in PanelHosts)
            all.AddRange(host.GetCurrentLayouts(workspace));
        return all;
    }

    /// <summary>
    /// Applies persisted layouts to the correct monitor's panel host (spec §19). Layouts whose
    /// monitor is not present are ignored here — callers should recover them first via
    /// <see cref="ILayoutService.RecoverPanelsForCurrentMonitors"/>.
    /// </summary>
    public void ApplyLayouts(IReadOnlyList<PanelLayout> layouts)
    {
        foreach (var host in PanelHosts)
        {
            var forThisMonitor = layouts.Where(l => l.MonitorId == host.MonitorId).ToList();
            if (forThisMonitor.Count > 0)
                host.ApplyLayouts(forThisMonitor);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitors.TopologyChanged -= OnTopologyChanged;
        foreach (var overlay in _overlays.Values)
        {
            try { overlay.Close(); } catch { }
        }
        _overlays.Clear();
    }
}
