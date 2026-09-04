using System.Windows.Threading;

namespace StudyHud.App;

public enum PomodoroPhase { Idle, Work, ShortBreak, LongBreak }

/// <summary>
/// A simple local Pomodoro timer for Focus Mode (spec §66 — presentation/study aid, no network).
/// Auto-advances Work → Break → Work; a long break after every <see cref="LongBreakEvery"/> work
/// intervals. Runs on the UI thread via a <see cref="DispatcherTimer"/>. Singleton so the Focus page
/// and any HUD element share one clock.
/// </summary>
public sealed class PomodoroService
{
    private readonly DispatcherTimer _timer;
    private int _workDoneInCycle;

    public int WorkMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int LongBreakEvery { get; set; } = 4;

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
        PhaseChanged?.Invoke(this, EventArgs.Empty);
        Tick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends the current phase immediately and moves to the next.</summary>
    public void Skip() => CompletePhase();

    private void OnSecond()
    {
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
            EnterPhase(_workDoneInCycle % LongBreakEvery == 0 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak);
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
        PhaseLength = TimeSpan.FromMinutes(minutes);
        Remaining = PhaseLength;
        PhaseChanged?.Invoke(this, EventArgs.Empty);
        Tick?.Invoke(this, EventArgs.Empty);
    }
}
