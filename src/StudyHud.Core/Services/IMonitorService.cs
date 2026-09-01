using StudyHud.Core.Models;

namespace StudyHud.Core.Services;

/// <summary>
/// Abstracts all monitor/display topology information (spec §4, §20, §84).
/// Monitors for topology changes and raises events so the overlay can respond correctly.
/// </summary>
public interface IMonitorService
{
    /// <summary>Current list of connected monitors.</summary>
    IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>The primary monitor.</summary>
    MonitorInfo? PrimaryMonitor { get; }

    /// <summary>Raised when the monitor topology changes (connect/disconnect/resolution/DPI).</summary>
    event EventHandler<MonitorTopologyChangedEventArgs> TopologyChanged;

    /// <summary>
    /// Returns the monitor that contains the given physical screen point,
    /// or the nearest monitor if no monitor directly contains it.
    /// </summary>
    MonitorInfo? GetMonitorAtPoint(ScreenPoint physicalPoint);

    /// <summary>
    /// Returns the monitor that most closely matches the given monitor ID.
    /// Used to re-anchor panels after a topology change (spec §160).
    /// </summary>
    MonitorInfo? GetMonitorById(string monitorId);

    /// <summary>
    /// Converts device-independent WPF units to physical pixels for the given monitor.
    /// </summary>
    ScreenRect LogicalToPhysical(double logicalLeft, double logicalTop,
        double logicalWidth, double logicalHeight, MonitorInfo monitor);

    /// <summary>
    /// Converts a physical pixel rectangle on a monitor back to device-independent units.
    /// </summary>
    (double Left, double Top, double Width, double Height) PhysicalToLogical(
        ScreenRect physical, MonitorInfo monitor);

    /// <summary>Initialise monitoring. Call once at startup.</summary>
    Task InitialiseAsync(CancellationToken cancellationToken = default);
}

public class MonitorTopologyChangedEventArgs : EventArgs
{
    public required IReadOnlyList<MonitorInfo> PreviousMonitors { get; init; }
    public required IReadOnlyList<MonitorInfo> CurrentMonitors { get; init; }
    public required IReadOnlyList<string> AddedMonitorIds { get; init; }
    public required IReadOnlyList<string> RemovedMonitorIds { get; init; }
    public required IReadOnlyList<string> ChangedMonitorIds { get; init; }
}
