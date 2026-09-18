using System.Windows.Threading;

namespace StudyHud.Overlay;

public enum PomodoroPhase { Idle, Work, ShortBreak, LongBreak }

/// <summary>
/// A simple local Pomodoro timer for Focus Mode (spec §66 — presentation/study aid, no network).
/// Auto-advances Work → Break → Work; a long break after every <see cref="LongBreakEvery"/> work
/// intervals. Runs on the UI thread via a <see cref="DispatcherTimer"/>. Singleton so the Focus page
/// and the floating Focus HUD panel share one clock. Lives in the Overlay project so the HUD panel
/// (and the settings page, via the App reference) can both use it.
/// </summary>
public sealed class PomodoroService
{
    private readonly DispatcherTimer _timer;
    private int _workDoneInCycle;

    public int WorkMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int LongBreakEvery { get; set; } = 4;

    /// <summary>Play a chime when a phase starts and when a phase ends (spec: focus-timer audio cue).</summary>
    public bool SoundsEnabled { get; set; } = true;

    /// <summary>Nudge after this many minutes of continuous focus (ADHD hyperfocus safety). 0 = off.</summary>
    public int SessionCapMinutes { get; set; } = 0;

    /// <summary>Seconds elapsed in the current phase (for the ADHD "you've focused N min" badge).</summary>
    public TimeSpan Elapsed => PhaseLength - Remaining;

    private int _continuousFocusSeconds;
    private bool _capAnnounced;
    private int _currentWorkMinutes = 25; // length of the work block in progress (may differ from WorkMinutes)

    /// <summary>Raised when a Work block completes — carries how many minutes it was (for momentum/reward).</summary>
    public event EventHandler<int>? FocusBlockCompleted;

    /// <summary>Raised once when continuous focus crosses <see cref="SessionCapMinutes"/> (hyperfocus safety).</summary>
    public event EventHandler? SessionCapReached;

    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Idle;
    public TimeSpan Remaining { get; private set; }
    public TimeSpan PhaseLength { get; private set; }
    public bool IsRunning => _timer.IsEnabled;
    public int CompletedToday { get; private set; }

    /// <summary>Raised every second while running, and whenever the remaining time changes.</summary>
    public event EventHandler? Tick;
    /// <summary>Raised when the phase changes (work↔break, or reset).</summary>
    public event EventHandler? PhaseChanged;

    public PomodoroService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnSecond();
    }

    public void Toggle()
    {
        if (IsRunning) { _timer.Stop(); Tick?.Invoke(this, EventArgs.Empty); return; }
        if (Phase == PomodoroPhase.Idle) EnterPhase(PomodoroPhase.Work);
        _timer.Start();
        Tick?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        _timer.Stop();
        Phase = PomodoroPhase.Idle;
        Remaining = TimeSpan.Zero;
        PhaseLength = TimeSpan.Zero;
        _workDoneInCycle = 0;
        _continuousFocusSeconds = 0;
        _capAnnounced = false;
        PhaseChanged?.Invoke(this, EventArgs.Empty);
        Tick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends the current phase immediately and moves to the next.</summary>
    public void Skip() => CompletePhase();

    /// <summary>
    /// Starts an ad-hoc focus block of <paramref name="minutes"/> right now (the ADHD "5-minute start"),
    /// without changing the configured work length. Completing it advances into a break as usual.
    /// </summary>
    public void StartQuick(int minutes)
    {
        minutes = Math.Max(1, minutes);
        Phase = PomodoroPhase.Work;
        _currentWorkMinutes = minutes;
        PhaseLength = TimeSpan.FromMinutes(minutes);
        Remaining = PhaseLength;
        _continuousFocusSeconds = 0;
        _capAnnounced = false;
        if (SoundsEnabled) PomodoroSounds.PlayForPhase(PomodoroPhase.Work);
        _timer.Start();
        PhaseChanged?.Invoke(this, EventArgs.Empty);
        Tick?.Invoke(this, EventArgs.Empty);
    }

    private void OnSecond()
    {
        // Track continuous focus for the hyperfocus session cap.
        if (Phase == PomodoroPhase.Work)
        {
            _continuousFocusSeconds++;
            if (!_capAnnounced && SessionCapMinutes > 0 && _continuousFocusSeconds >= SessionCapMinutes * 60)
            {
                _capAnnounced = true;
                SessionCapReached?.Invoke(this, EventArgs.Empty);
            }
        }

        if (Remaining <= TimeSpan.FromSeconds(1)) { CompletePhase(); return; }
        Remaining -= TimeSpan.FromSeconds(1);
        Tick?.Invoke(this, EventArgs.Empty);
    }

    private void CompletePhase()
    {
        if (Phase == PomodoroPhase.Work)
        {
            CompletedToday++;
            _workDoneInCycle++;
            FocusBlockCompleted?.Invoke(this, _currentWorkMinutes);
            bool longBreak = _workDoneInCycle % LongBreakEvery == 0;
            // A long break is a genuine rest — reset the continuous-focus counter for the cap.
            if (longBreak) { _continuousFocusSeconds = 0; _capAnnounced = false; }
            EnterPhase(longBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak);
        }
        else
        {
            EnterPhase(PomodoroPhase.Work);
        }
    }

    private void EnterPhase(PomodoroPhase phase)
    {
        Phase = phase;
        int minutes = phase switch
        {
            PomodoroPhase.Work => WorkMinutes,
            PomodoroPhase.ShortBreak => ShortBreakMinutes,
            PomodoroPhase.LongBreak => LongBreakMinutes,
            _ => 0
        };
        if (phase == PomodoroPhase.Work) _currentWorkMinutes = minutes;
        PhaseLength = TimeSpan.FromMinutes(minutes);
        Remaining = PhaseLength;

        // Audible cue for the phase we just entered — a rising chime for Work, a falling one for a
        // break — so the user knows a phase started/ended without watching the timer.
        if (SoundsEnabled) PomodoroSounds.PlayForPhase(phase);

        PhaseChanged?.Invoke(this, EventArgs.Empty);
        Tick?.Invoke(this, EventArgs.Empty);
    }
}
