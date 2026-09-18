using StudyHud.Core.Services;

namespace StudyHud.Overlay;

/// <summary>
/// Tracks study momentum for the ADHD reward layer: a day-streak, today's focus minutes, and a
/// lifetime block count. Persisted through <see cref="ISettingsStore"/> so it survives restarts.
/// Deliberately forgiving — a missed day resets the current streak but never scolds, and "best streak"
/// is kept so a lapse doesn't erase the sense of progress.
/// </summary>
public sealed class MomentumService
{
    private readonly ISettingsStore _settings;

    public int Streak { get; private set; }
    public int BestStreak { get; private set; }
    public int FocusMinutesToday { get; private set; }
    public int TotalFocusBlocks { get; private set; }

    /// <summary>Raised (on the calling thread) whenever any momentum value changes.</summary>
    public event EventHandler? Changed;

    public MomentumService(ISettingsStore settings)
    {
        _settings = settings;
        LoadFromSettings();
    }

    /// <summary>Reads the persisted values, rolling today's minutes back to zero if the date has changed.</summary>
    public void LoadFromSettings()
    {
        var s = _settings.Current;
        var today = Today();
        Streak = s.StreakCount;
        BestStreak = s.BestStreak;
        FocusMinutesToday = s.FocusMinutesTodayDate == today ? s.FocusMinutesToday : 0;
        TotalFocusBlocks = s.TotalFocusBlocks;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records one completed focus block and advances the streak / daily total.</summary>
    public void RecordFocusBlockCompleted(int minutes)
    {
        var s = _settings.Current;
        var today = Today();
        var yesterday = DateTime.Now.Date.AddDays(-1).ToString("yyyy-MM-dd");

        // Advance the streak once per day: continue it if the last study day was yesterday, else restart.
        int streak = s.StreakCount;
        if (s.LastStudyDate != today)
            streak = s.LastStudyDate == yesterday ? s.StreakCount + 1 : 1;

        int best = Math.Max(s.BestStreak, streak);
        int todayMin = (s.FocusMinutesTodayDate == today ? s.FocusMinutesToday : 0) + Math.Max(0, minutes);
        int total = s.TotalFocusBlocks + 1;

        Streak = streak;
        BestStreak = best;
        FocusMinutesToday = todayMin;
        TotalFocusBlocks = total;

        _ = _settings.UpdateAsync(x => x with
        {
            StreakCount = streak,
            BestStreak = best,
            LastStudyDate = today,
            FocusMinutesToday = todayMin,
            FocusMinutesTodayDate = today,
            TotalFocusBlocks = total
        });

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Today() => DateTime.Now.Date.ToString("yyyy-MM-dd");
}
