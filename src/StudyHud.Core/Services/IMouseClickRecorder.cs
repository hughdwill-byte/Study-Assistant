namespace StudyHud.Core.Services;

/// <summary>One recorded mouse click: an absolute screen point, a button (1 = left, 2 = right),
/// and the delay in milliseconds since the previous click (0 for the first).</summary>
public readonly record struct MouseClickSample(int X, int Y, int Button, int DelayMs);

/// <summary>
/// Records the user's mouse clicks (position + timing) until Escape is pressed, so a sequence of clicks
/// can be replayed as a macro (spec §36). Recording never suppresses the clicks — the user really clicks
/// the things they want the macro to click.
/// </summary>
public interface IMouseClickRecorder
{
    bool IsRecording { get; }

    /// <summary>
    /// Starts recording and completes when the user presses Escape, returning the captured clicks
    /// (empty if none were made).
    /// </summary>
    Task<IReadOnlyList<MouseClickSample>> RecordUntilEscapeAsync();
}
