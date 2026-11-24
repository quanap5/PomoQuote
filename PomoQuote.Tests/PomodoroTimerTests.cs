using PomoQuote.Pomodoro;

namespace PomoQuote.Tests;

public class PomodoroTimerTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly FakeClock _clock = new();
    private readonly List<PhaseCompletedEventArgs> _completed = [];

    private PomodoroTimer Create(PomodoroSettings? settings = null)
    {
        var timer = new PomodoroTimer(_clock, settings ?? PomodoroSettings.Default);
        timer.PhaseCompleted += (_, e) => _completed.Add(e);
        return timer;
    }

    /// <summary>Runs the current phase to its end with a tick exactly when it is due.</summary>
    private void FinishPhase(PomodoroTimer timer)
    {
        _clock.Advance(timer.Remaining);
        Assert.True(timer.Tick());
    }

    [Fact]
    public void New_timer_waits_idle_on_a_full_work_phase()
    {
        var timer = Create();

        Assert.Equal(PomodoroPhase.Work, timer.Phase);
        Assert.Equal(PomodoroRunState.Idle, timer.State);
        Assert.Equal(25 * Minute, timer.Remaining);
        Assert.Equal(0, timer.Progress);
    }

    [Fact]
    public void Idle_timer_does_not_run_or_complete()
    {
        var timer = Create();

        _clock.Advance(TimeSpan.FromHours(2));

        Assert.False(timer.Tick());
        Assert.Equal(25 * Minute, timer.Remaining);
        Assert.Empty(_completed);
    }

    [Fact]
    public void Running_timer_counts_down_and_reports_progress()
    {
        var timer = Create();
        timer.Start();

        _clock.Advance(5 * Minute);

        Assert.Equal(PomodoroRunState.Running, timer.State);
        Assert.Equal(20 * Minute, timer.Remaining);
        Assert.Equal(0.2, timer.Progress, 3);
        Assert.False(timer.Tick());
    }

    [Fact]
    public void Work_then_short_break_then_work()
    {
        var timer = Create();
        timer.Start();

        FinishPhase(timer);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(5 * Minute, timer.Remaining);
        Assert.Equal(PomodoroRunState.Running, timer.State);

        FinishPhase(timer);
        Assert.Equal(PomodoroPhase.Work, timer.Phase);
        Assert.Equal(25 * Minute, timer.Remaining);

        Assert.Equal(2, _completed.Count);
        Assert.Equal(PomodoroPhase.Work, _completed[0].Completed);
        Assert.True(_completed[0].WorkSessionCompleted);
        Assert.Equal(PomodoroPhase.ShortBreak, _completed[0].Next);
        Assert.Equal(PomodoroPhase.ShortBreak, _completed[1].Completed);
        Assert.False(_completed[1].WorkSessionCompleted);
    }

    [Fact]
    public void Long_break_comes_after_four_work_sessions_then_the_cycle_restarts()
    {
        var timer = Create();
        timer.Start();

        var phases = new List<PomodoroPhase>();
        for (var i = 0; i < 9; i++)
        {
            phases.Add(timer.Phase);
            FinishPhase(timer);
        }

        Assert.Equal(
            [
                PomodoroPhase.Work, PomodoroPhase.ShortBreak,
                PomodoroPhase.Work, PomodoroPhase.ShortBreak,
                PomodoroPhase.Work, PomodoroPhase.ShortBreak,
                PomodoroPhase.Work, PomodoroPhase.LongBreak,
                PomodoroPhase.Work,
            ],
            phases);
        Assert.Equal(1, timer.SessionsInCycle); // the new cycle's first work session is done
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(PomodoroPhase.LongBreak, _completed[6].Next);
    }

    [Fact]
    public void Long_break_lasts_the_configured_long_break_time()
    {
        var timer = Create(new PomodoroSettings(SessionsBeforeLongBreak: 1));
        timer.Start();

        FinishPhase(timer);

        Assert.Equal(PomodoroPhase.LongBreak, timer.Phase);
        Assert.Equal(15 * Minute, timer.Remaining);
    }

    [Fact]
    public void Without_auto_start_the_next_phase_waits_for_Start()
    {
        var timer = Create(new PomodoroSettings(AutoStartNext: false));
        timer.Start();

        FinishPhase(timer);

        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(PomodoroRunState.Idle, timer.State);
        Assert.Equal(5 * Minute, timer.Remaining);

        _clock.Advance(10 * Minute);
        Assert.False(timer.Tick());
        timer.Start();
        Assert.Equal(PomodoroRunState.Running, timer.State);
        Assert.Equal(5 * Minute, timer.Remaining);
    }

    [Fact]
    public void Pause_freezes_the_remaining_time_and_Resume_continues_from_it()
    {
        var timer = Create();
        timer.Start();
        _clock.Advance(10 * Minute);

        timer.Pause();
        _clock.Advance(TimeSpan.FromHours(3));

        Assert.Equal(PomodoroRunState.Paused, timer.State);
        Assert.Equal(15 * Minute, timer.Remaining);
        Assert.False(timer.Tick());

        timer.Resume();
        _clock.Advance(5 * Minute);
        Assert.Equal(10 * Minute, timer.Remaining);
    }

    [Fact]
    public void Start_on_a_paused_timer_resumes_it()
    {
        var timer = Create();
        timer.Start();
        _clock.Advance(Minute);
        timer.Pause();

        timer.Start();

        Assert.Equal(PomodoroRunState.Running, timer.State);
        Assert.Equal(24 * Minute, timer.Remaining);
    }

    [Fact]
    public void Pause_and_Resume_are_ignored_in_the_wrong_state()
    {
        var timer = Create();

        timer.Pause();
        timer.Resume();

        Assert.Equal(PomodoroRunState.Idle, timer.State);
    }

    [Fact]
    public void Reset_returns_to_an_idle_work_phase_and_forgets_the_cycle()
    {
        var timer = Create();
        timer.Start();
        FinishPhase(timer);
        FinishPhase(timer);
        FinishPhase(timer);
        Assert.Equal(2, timer.SessionsInCycle);

        timer.Reset();

        Assert.Equal(PomodoroPhase.Work, timer.Phase);
        Assert.Equal(PomodoroRunState.Idle, timer.State);
        Assert.Equal(0, timer.SessionsInCycle);
        Assert.Equal(25 * Minute, timer.Remaining);
    }

    [Fact]
    public void Skip_moves_to_the_next_phase_without_an_alert_and_does_not_count_the_work()
    {
        var timer = Create();
        timer.Start();
        _clock.Advance(Minute);

        timer.Skip();

        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(PomodoroRunState.Running, timer.State);
        Assert.Equal(5 * Minute, timer.Remaining);
        Assert.Equal(0, timer.SessionsInCycle);
        Assert.Empty(_completed);
    }

    [Fact]
    public void Skipping_a_break_goes_back_to_work()
    {
        var timer = Create();
        timer.Start();
        FinishPhase(timer);

        timer.Skip();

        Assert.Equal(PomodoroPhase.Work, timer.Phase);
        Assert.Equal(1, timer.SessionsInCycle);
    }

    [Fact]
    public void Skipping_work_sessions_never_earns_a_long_break()
    {
        var timer = Create();
        timer.Start();

        for (var i = 0; i < 10; i++)
        {
            timer.Skip(); // work -> break
            Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
            timer.Skip(); // break -> work
        }
    }

    [Fact]
    public void Skipping_the_long_break_restarts_the_cycle()
    {
        var timer = Create(new PomodoroSettings(SessionsBeforeLongBreak: 1));
        timer.Start();
        FinishPhase(timer);
        Assert.Equal(PomodoroPhase.LongBreak, timer.Phase);

        timer.Skip();

        Assert.Equal(PomodoroPhase.Work, timer.Phase);
        Assert.Equal(0, timer.SessionsInCycle);
    }

    [Fact]
    public void Skip_while_idle_only_changes_the_phase()
    {
        var timer = Create();

        timer.Skip();

        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(PomodoroRunState.Idle, timer.State);
    }

    [Fact]
    public void A_stall_longer_than_a_phase_completes_exactly_one_phase_and_restarts_fresh()
    {
        var timer = Create();
        timer.Start();

        _clock.Advance(TimeSpan.FromHours(5)); // UI stall or system sleep
        Assert.True(timer.Tick());

        Assert.Single(_completed);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(5 * Minute, timer.Remaining); // counted from now, not from the missed end
        Assert.False(timer.Tick());
    }

    [Fact]
    public void Ticks_are_irrelevant_to_accuracy_only_the_clock_matters()
    {
        var timer = Create();
        timer.Start();

        // No ticks at all for 24 minutes 59 seconds, then one big jump to exactly the end.
        _clock.Advance(24 * Minute + TimeSpan.FromSeconds(59));
        Assert.False(timer.Tick());
        Assert.Equal(TimeSpan.FromSeconds(1), timer.Remaining);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(timer.Tick());
    }

    [Fact]
    public void A_wall_clock_change_does_not_move_the_countdown()
    {
        var timer = Create();
        timer.Start();
        _clock.Advance(10 * Minute);

        _clock.SetCalendar(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)); // user changes the date

        Assert.Equal(15 * Minute, timer.Remaining);
        Assert.False(timer.Tick());
    }

    [Fact]
    public void Remaining_never_goes_negative()
    {
        var timer = Create();
        timer.Start();

        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.Zero, timer.Remaining);
        Assert.Equal(1.0, timer.Progress);
    }

    [Fact]
    public void New_settings_apply_to_the_next_phase_and_not_to_the_running_one()
    {
        var timer = Create();
        timer.Start();
        _clock.Advance(Minute);

        timer.ApplySettings(new PomodoroSettings(WorkMinutes: 50, ShortBreakMinutes: 10));

        Assert.Equal(24 * Minute, timer.Remaining);
        FinishPhase(timer);
        Assert.Equal(10 * Minute, timer.Remaining);
    }

    [Fact]
    public void Changing_durations_mid_phase_keeps_remaining_time_and_progress_consistent()
    {
        var timer = Create();
        timer.Start();
        _clock.Advance(10 * Minute); // 10 of 25 minutes done

        timer.ApplySettings(new PomodoroSettings(WorkMinutes: 5, ShortBreakMinutes: 2));

        Assert.Equal(15 * Minute, timer.Remaining);
        Assert.Equal(25 * Minute, timer.PhaseDuration);
        Assert.Equal(0.4, timer.Progress, 3);

        _clock.Advance(5 * Minute);
        Assert.Equal(0.6, timer.Progress, 3);

        timer.Pause();
        timer.ApplySettings(new PomodoroSettings(WorkMinutes: 90));
        Assert.Equal(10 * Minute, timer.Remaining);
        Assert.Equal(0.6, timer.Progress, 3);

        timer.Resume();
        Assert.Equal(0.6, timer.Progress, 3);
        _clock.Advance(10 * Minute);
        Assert.True(timer.Tick());
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(5 * Minute, timer.Remaining); // the break length is still the new setting's default 5
    }

    [Fact]
    public void Next_phase_after_a_mid_phase_change_uses_the_new_durations_and_fresh_progress()
    {
        var timer = Create();
        timer.Start();
        _clock.Advance(10 * Minute);
        timer.ApplySettings(new PomodoroSettings(WorkMinutes: 5, ShortBreakMinutes: 2));

        FinishPhase(timer);

        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(2 * Minute, timer.Remaining);
        Assert.Equal(2 * Minute, timer.PhaseDuration);
        Assert.Equal(0, timer.Progress, 3);

        FinishPhase(timer);
        Assert.Equal(5 * Minute, timer.Remaining); // the next work phase
        Assert.Equal(5 * Minute, timer.PhaseDuration);
    }

    [Fact]
    public void New_settings_change_an_idle_phase_immediately()
    {
        var timer = Create();

        timer.ApplySettings(new PomodoroSettings(WorkMinutes: 50));

        Assert.Equal(50 * Minute, timer.Remaining);
    }

    [Fact]
    public void Changed_is_raised_for_every_state_change()
    {
        var timer = Create();
        var changes = 0;
        timer.Changed += (_, _) => changes++;

        timer.Start();
        timer.Pause();
        timer.Resume();
        timer.Skip();
        timer.Reset();

        Assert.Equal(5, changes);
    }

    [Theory]
    [InlineData(25, 5, 15, 4, true)]
    [InlineData(1, 1, 1, 1, true)]
    [InlineData(180, 180, 180, 12, true)]
    [InlineData(0, 5, 15, 4, false)]
    [InlineData(181, 5, 15, 4, false)]
    [InlineData(25, 0, 15, 4, false)]
    [InlineData(25, 5, 181, 4, false)]
    [InlineData(25, 5, 15, 0, false)]
    [InlineData(25, 5, 15, 13, false)]
    public void Settings_ranges_are_enforced(int work, int shortBreak, int longBreak, int sessions, bool valid)
    {
        Assert.Equal(valid, new PomodoroSettings(work, shortBreak, longBreak, sessions).IsValid);
    }

    [Fact]
    public void Default_settings_are_25_5_15_4_with_sound_and_auto_start()
    {
        Assert.Equal(new PomodoroSettings(25, 5, 15, 4, true, true), PomodoroSettings.Default);
    }
}
