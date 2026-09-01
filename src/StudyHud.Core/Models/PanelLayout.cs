namespace StudyHud.Core.Models;

/// <summary>
/// Persisted layout data for a single HUD panel (spec §19).
/// Uses monitor-relative normalised coordinates to survive resolution/DPI changes.
/// </summary>
public record PanelLayout
{
    public required string PanelId { get; init; }
    public required WorkspaceId Workspace { get; init; }

    /// <summary>The monitor this panel belongs to.</summary>
    public required string MonitorId { get; init; }

    /// <summary>Position normalised to monitor work area: 0.0–1.0 for each edge.</summary>
    public required NormalizedRect NormalizedPosition { get; init; }

    /// <summary>Logical width/height in device-independent pixels.</summary>
    public double LogicalWidth { get; init; }
    public double LogicalHeight { get; init; }

    public PanelVisibilityState VisibilityState { get; init; } = PanelVisibilityState.Expanded;
    public PanelResponsiveState ResponsiveState { get; init; } = PanelResponsiveState.Normal;
    public PanelDockState DockState { get; init; } = PanelDockState.Floating;
    public CollapseDirection CollapseDirection { get; init; } = CollapseDirection.None;

    /// <summary>ID of the dock group this panel belongs to, if any.</summary>
    public string? DockGroupId { get; init; }

    /// <summary>Position of the reveal tab as a normalised offset from the collapse edge.</summary>
    public double RevealTabOffset { get; init; } = 0.5;

    /// <summary>Z-order relative to other panels (higher = in front).</summary>
    public int ZOrder { get; init; }
}

/// <summary>
/// A rectangle with coordinates normalised to 0.0–1.0 within a containing space (monitor work area).
/// </summary>
public record NormalizedRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
}

/// <summary>
/// Persisted dock group: defines which panels move/resize together (spec §12).
/// </summary>
public record DockGroup
{
    public required string GroupId { get; init; }
    public required List<string> PanelIds { get; init; }
    public bool CollapseGroupTogether { get; init; } = false;
}
