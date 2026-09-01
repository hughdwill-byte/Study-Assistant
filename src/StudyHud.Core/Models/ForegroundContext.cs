namespace StudyHud.Core.Models;

/// <summary>
/// Immutable snapshot of the currently active foreground application.
/// Built by ForegroundWindowService via SetWinEventHook (spec §167).
/// Consumed by macro/input/HUD policy layers — must never be built inside a hook callback.
/// </summary>
public record ForegroundContext
{
    public static ForegroundContext Unknown => new()
    {
        ExecutableName = string.Empty,
        ExecutablePath = null,
        ProcessId = 0,
        WindowHandle = IntPtr.Zero,
        IsElevated = false,
        IsFullscreen = false,
        IsStudyHudOwned = false,
        MacrosAllowed = true,
        CaptureAllowed = true,
        HudAllowed = true,
        Timestamp = DateTimeOffset.UtcNow
    };

    public required string ExecutableName { get; init; }
    public string? ExecutablePath { get; init; }
    public required int ProcessId { get; init; }
    public required IntPtr WindowHandle { get; init; }

    /// <summary>True if the target process is elevated and Study HUD is not (UIPI boundary).</summary>
    public required bool IsElevated { get; init; }

    /// <summary>True if the foreground window appears to be fullscreen.</summary>
    public required bool IsFullscreen { get; init; }

    /// <summary>True if this window belongs to Study HUD itself.</summary>
    public required bool IsStudyHudOwned { get; init; }

    // Pre-computed policy results so hook callbacks read these without extra work
    public required bool MacrosAllowed { get; init; }
    public required bool CaptureAllowed { get; init; }
    public required bool HudAllowed { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}
