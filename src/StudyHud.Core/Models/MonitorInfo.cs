namespace StudyHud.Core.Models;

/// <summary>
/// Represents a single physical monitor and its relevant properties (spec §4, §20, §84).
/// All rectangle values are in physical screen pixels unless noted.
/// </summary>
public record MonitorInfo
{
    /// <summary>Stable identity derived from HMONITOR and device name.</summary>
    public required string MonitorId { get; init; }

    /// <summary>GDI device name, e.g. \\.\DISPLAY1</summary>
    public required string DeviceName { get; init; }

    /// <summary>Full monitor bounds in virtual desktop coordinates (physical pixels).</summary>
    public required ScreenRect Bounds { get; init; }

    /// <summary>Work area (excluding taskbar) in virtual desktop coordinates (physical pixels).</summary>
    public required ScreenRect WorkArea { get; init; }

    /// <summary>DPI scaling factor, e.g. 1.25 for 125%.</summary>
    public required double ScaleFactor { get; init; }

    /// <summary>Raw DPI value (typically 96 * ScaleFactor).</summary>
    public required uint Dpi { get; init; }

    public bool IsPrimary { get; init; }

    /// <summary>Friendly name when available from Windows API.</summary>
    public string? FriendlyName { get; init; }
}

/// <summary>
/// A rectangle in screen-space (physical pixels or device-independent units depending on context).
/// </summary>
public record ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public bool Contains(int x, int y) =>
        x >= Left && x < Right && y >= Top && y < Bottom;

    public static ScreenRect Empty => new(0, 0, 0, 0);
}

/// <summary>
/// A point in screen-space (physical pixels).
/// </summary>
public record ScreenPoint(int X, int Y);
