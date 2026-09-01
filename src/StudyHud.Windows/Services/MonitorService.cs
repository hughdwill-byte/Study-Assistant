using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Models;
using StudyHud.Core.Services;
using StudyHud.Windows.Native;

namespace StudyHud.Windows.Services;

/// <summary>
/// Monitor topology service using Win32 EnumDisplayMonitors + GetMonitorInfoEx.
/// Responds to WM_DISPLAYCHANGE via a hidden message window (spec §4, §20, §84, §160).
/// </summary>
public sealed class MonitorService : IMonitorService, IDisposable
{
    private readonly ILogger<MonitorService> _logger;
    private volatile IReadOnlyList<MonitorInfo> _monitors = [];
    private bool _disposed;

    public MonitorService(ILogger<MonitorService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    public MonitorInfo? PrimaryMonitor =>
        _monitors.FirstOrDefault(m => m.IsPrimary);

    public event EventHandler<MonitorTopologyChangedEventArgs>? TopologyChanged;

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(RefreshMonitors, cancellationToken);
        _logger.LogInformation("MonitorService initialised. Found {Count} monitors.", _monitors.Count);
    }

    /// <summary>
    /// Called when WM_DISPLAYCHANGE is received (wired up by the overlay HWND).
    /// </summary>
    public void OnDisplayChange()
    {
        var previous = _monitors;
        RefreshMonitors();
        var current = _monitors;

        var addedIds = current.Select(m => m.MonitorId)
            .Except(previous.Select(m => m.MonitorId)).ToList();
        var removedIds = previous.Select(m => m.MonitorId)
            .Except(current.Select(m => m.MonitorId)).ToList();
        var changedIds = current
            .Where(c => previous.Any(p => p.MonitorId == c.MonitorId && p != c))
            .Select(m => m.MonitorId).ToList();

        _logger.LogInformation(
            "Display topology changed. Added={Added}, Removed={Removed}, Changed={Changed}",
            addedIds.Count, removedIds.Count, changedIds.Count);

        TopologyChanged?.Invoke(this, new MonitorTopologyChangedEventArgs
        {
            PreviousMonitors = previous,
            CurrentMonitors = current,
            AddedMonitorIds = addedIds,
            RemovedMonitorIds = removedIds,
            ChangedMonitorIds = changedIds
        });
    }

    private void RefreshMonitors()
    {
        var infos = new List<MonitorInfo>();

        NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT rcMonitor, IntPtr dwData) =>
            {
                var info = BuildMonitorInfo(hMonitor);
                if (info != null) infos.Add(info);
                return true;
            },
            IntPtr.Zero);

        _monitors = infos.AsReadOnly();
    }

    private MonitorInfo? BuildMonitorInfo(IntPtr hMonitor)
    {
        var mi = new NativeMethods.MONITORINFOEX();
        mi.cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>();
        if (!NativeMethods.GetMonitorInfoW(hMonitor, ref mi))
        {
            _logger.LogWarning("GetMonitorInfoEx failed for hMonitor={Handle}.", hMonitor);
            return null;
        }

        NativeMethods.GetDpiForMonitor(hMonitor, 0 /*MDT_EFFECTIVE_DPI*/, out uint dpiX, out _);
        if (dpiX == 0) dpiX = 96; // safe default

        double scaleFactor = dpiX / 96.0;

        // Build a stable ID from device name and bounds (no HMONITOR — it changes across sessions)
        var monitorId = $"{mi.szDevice}_{mi.rcMonitor.Left}_{mi.rcMonitor.Top}";

        return new MonitorInfo
        {
            MonitorId = monitorId,
            DeviceName = mi.szDevice,
            Bounds = new ScreenRect(mi.rcMonitor.Left, mi.rcMonitor.Top,
                mi.rcMonitor.Right, mi.rcMonitor.Bottom),
            WorkArea = new ScreenRect(mi.rcWork.Left, mi.rcWork.Top,
                mi.rcWork.Right, mi.rcWork.Bottom),
            ScaleFactor = scaleFactor,
            Dpi = dpiX,
            IsPrimary = (mi.dwFlags & NativeMethods.MONITORINFOEX.MONITORINFOF_PRIMARY) != 0
        };
    }

    public MonitorInfo? GetMonitorAtPoint(ScreenPoint physicalPoint)
    {
        // Prefer the monitor containing the point; fall back to nearest
        return _monitors.FirstOrDefault(m => m.Bounds.Contains(physicalPoint.X, physicalPoint.Y))
            ?? _monitors.MinBy(m =>
            {
                int cx = (m.Bounds.Left + m.Bounds.Right) / 2;
                int cy = (m.Bounds.Top + m.Bounds.Bottom) / 2;
                int dx = physicalPoint.X - cx;
                int dy = physicalPoint.Y - cy;
                return dx * dx + dy * dy;
            });
    }

    public MonitorInfo? GetMonitorById(string monitorId) =>
        _monitors.FirstOrDefault(m => m.MonitorId == monitorId);

    public ScreenRect LogicalToPhysical(
        double logicalLeft, double logicalTop,
        double logicalWidth, double logicalHeight,
        MonitorInfo monitor)
    {
        double scale = monitor.ScaleFactor;
        int physLeft = monitor.WorkArea.Left + (int)Math.Round(logicalLeft * scale);
        int physTop = monitor.WorkArea.Top + (int)Math.Round(logicalTop * scale);
        int physRight = physLeft + (int)Math.Round(logicalWidth * scale);
        int physBottom = physTop + (int)Math.Round(logicalHeight * scale);
        return new ScreenRect(physLeft, physTop, physRight, physBottom);
    }

    public (double Left, double Top, double Width, double Height) PhysicalToLogical(
        ScreenRect physical, MonitorInfo monitor)
    {
        double scale = monitor.ScaleFactor;
        double left = (physical.Left - monitor.WorkArea.Left) / scale;
        double top = (physical.Top - monitor.WorkArea.Top) / scale;
        double width = physical.Width / scale;
        double height = physical.Height / scale;
        return (left, top, width, height);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
