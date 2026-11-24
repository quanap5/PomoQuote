namespace HelloWorldBot.Pomodoro;

internal enum PomodoroRunState
{
    /// <summary>Not started: the current phase waits for Start, showing its full duration.</summary>
    Idle,
    Running,
    Paused,
}

internal sealed class PhaseCompletedEventArgs(PomodoroPhase completed, PomodoroPhase next) : EventArgs
{
    public PomodoroPhase Completed { get; } = completed;

    public PomodoroPhase Next { get; } = next;

    /// <summary>True when a work session ran to its end (it counts towards today's total).</summary>
    public bool WorkSessionCompleted => Completed == PomodoroPhase.Work;
}

/// <summary>
/// The Pomodoro state machine: work, short break, long break after N work sessions. It has no UI and
/// no timers of its own; the caller calls <see cref="Tick"/> regularly. Time comes from an injected
/// <see cref="TimeProvider"/> as monotonic timestamps and a stored phase end, so a late or skipped
/// tick (UI stall, sleep) never makes it drift: the phase simply ends on the first tick after its end.
/// A phase that ended long ago still counts as one phase; the next one starts fresh from "now".
/// </summary>
internal sealed class PomodoroTimer
{
    private readonly TimeProvider _clock;
    private PomodoroSettings _settings;
    private long _endTimestamp;
    private TimeSpan _pausedRemaining;
    private TimeSpan _activeDuration;

    public PomodoroTimer(TimeProvider clock, PomodoroSettings settings)
    {
        _clock = clock;
        _settings = settings;
    }

    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Work;

    public PomodoroRunState State { get; private set; } = PomodoroRunState.Idle;

    /// <summary>Work sessions finished since the last long break (or reset).</summary>
    public int SessionsInCycle { get; private set; }

    public PomodoroSettings Settings => _settings;

    /// <summary>
    /// The full length of the current phase: what it started with while running or paused (so changing
    /// the options mid-phase does not distort the progress), the configured length while idle.
    /// </summary>
    public TimeSpan PhaseDuration => State == PomodoroRunState.Idle ? _settings.DurationOf(Phase) : _activeDuration;

    public TimeSpan Remaining => State switch
    {
        PomodoroRunState.Running => Max(TimeSpan.Zero, _clock.GetElapsedTime(_clock.GetTimestamp(), _endTimestamp)),
        PomodoroRunState.Paused => _pausedRemaining,
        _ => PhaseDuration,
    };

    /// <summary>How much of the current phase has elapsed, 0 to 1.</summary>
    public double Progress => PhaseDuration <= TimeSpan.Zero
        ? 0
        : Math.Clamp(1 - Remaining / PhaseDuration, 0, 1);

    /// <summary>Raised when a phase runs to its end (not when it is skipped). Handlers play the alert.</summary>
    public event EventHandler<PhaseCompletedEventArgs>? PhaseCompleted;

    /// <summary>Raised after any change of phase, state or settings.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after the user skipped the current phase (not when a phase simply ended).</summary>
    public event EventHandler? PhaseSkipped;

    /// <summary>Raised after the timer was reset to a fresh work phase.</summary>
    public event EventHandler? ResetPerformed;

    /// <summary>Starts an idle phase, or resumes a paused one.</summary>
    public void Start()
    {
        switch (State)
        {
            case PomodoroRunState.Idle:
                BeginRunning(PhaseDuration, newPhase: true);
                break;
            case PomodoroRunState.Paused:
                Resume();
                break;
        }
    }

    public void Pause()
    {
        if (State != PomodoroRunState.Running) return;
        _pausedRemaining = Remaining;
        State = PomodoroRunState.Paused;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (State != PomodoroRunState.Paused) return;
        BeginRunning(_pausedRemaining, newPhase: false);
    }

    /// <summary>Back to an idle work phase with a fresh cycle.</summary>
    public void Reset()
    {
        Phase = PomodoroPhase.Work;
        State = PomodoroRunState.Idle;
        SessionsInCycle = 0;
        Changed?.Invoke(this, EventArgs.Empty);
        ResetPerformed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves on to the next phase without an alert. A skipped work session does not count. The next
    /// phase runs if this one was running or paused and auto-start is on, otherwise it waits for Start.
    /// </summary>
    public void Skip()
    {
        var wasActive = State != PomodoroRunState.Idle;
        AdvanceFrom(Phase, workCounts: false, startNext: wasActive && _settings.AutoStartNext);
        PhaseSkipped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends the phase if its time is up. Returns true when a phase just completed.</summary>
    public bool Tick()
    {
        if (State != PomodoroRunState.Running || _clock.GetTimestamp() < _endTimestamp) return false;

        var completed = Phase;
        var next = AdvanceFrom(completed, workCounts: true, startNext: _settings.AutoStartNext);
        PhaseCompleted?.Invoke(this, new PhaseCompletedEventArgs(completed, next));
        return true;
    }

    /// <summary>
    /// Applies new durations. The phase in progress keeps its current end (or paused remainder);
    /// the new numbers apply from the next phase, or immediately while idle.
    /// </summary>
    public void ApplySettings(PomodoroSettings settings)
    {
        _settings = settings;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private PomodoroPhase AdvanceFrom(PomodoroPhase from, bool workCounts, bool startNext)
    {
        PomodoroPhase next;
        if (from == PomodoroPhase.Work)
        {
            if (workCounts) SessionsInCycle++;
            next = workCounts && SessionsInCycle >= _settings.SessionsBeforeLongBreak
                ? PomodoroPhase.LongBreak
                : PomodoroPhase.ShortBreak;
        }
        else
        {
            if (from == PomodoroPhase.LongBreak) SessionsInCycle = 0;
            next = PomodoroPhase.Work;
        }

        Phase = next;
        if (startNext)
        {
            BeginRunning(_settings.DurationOf(next), newPhase: true);
        }
        else
        {
            State = PomodoroRunState.Idle;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return next;
    }

    private void BeginRunning(TimeSpan duration, bool newPhase)
    {
        if (newPhase) _activeDuration = duration;

        _endTimestamp = _clock.GetTimestamp() + (long)(duration.TotalSeconds * _clock.TimestampFrequency);
        State = PomodoroRunState.Running;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;
}
