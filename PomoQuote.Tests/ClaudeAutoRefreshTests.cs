using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using PomoQuote.AiUsage;
using PomoQuote.AiUsage.Claude;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>The decision of the optional automatic Claude refresh: one rule at a time, from plain values.</summary>
public sealed class ClaudeAutoRefreshDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private static AutoRefreshInputs Ready() => new(true, Interval, Now, null, null, false, false, false, null, false);

    [Fact]
    public void When_nothing_stands_in_the_way_it_runs()
    {
        Assert.Equal(AutoRefreshDecision.Run, ClaudeAutoRefresh.Decide(Ready()));
    }

    [Fact]
    public void Off_never_runs()
    {
        Assert.Equal(AutoRefreshDecision.Off, ClaudeAutoRefresh.Decide(Ready() with { Enabled = false }));
    }

    [Theory]
    [InlineData(29, "NotDue")]
    [InlineData(30, "Run")]
    [InlineData(31, "Run")]
    public void An_attempt_is_due_only_once_per_interval(int minutesAgo, string expected)
    {
        Assert.Equal(Enum.Parse<AutoRefreshDecision>(expected), ClaudeAutoRefresh.Decide(Ready() with { LastAttempt = Now.AddMinutes(-minutesAgo) }));
    }

    [Theory]
    [InlineData(14, "Fresh")] // newer than half the interval: a session is already updating the numbers
    [InlineData(15, "Run")]
    [InlineData(40, "Run")]
    public void Numbers_newer_than_half_an_interval_are_left_alone(int minutesOld, string expected)
    {
        Assert.Equal(Enum.Parse<AutoRefreshDecision>(expected), ClaudeAutoRefresh.Decide(Ready() with { NewestSnapshot = Now.AddMinutes(-minutesOld) }));
    }

    [Fact]
    public void A_window_at_100_percent_is_not_asked_again()
    {
        Assert.Equal(AutoRefreshDecision.Limited, ClaudeAutoRefresh.Decide(Ready() with { AtLimit = true }));
    }

    [Fact]
    public void A_locked_or_sleeping_pc_and_a_hidden_widget_pause_it()
    {
        Assert.Equal(AutoRefreshDecision.Paused, ClaudeAutoRefresh.Decide(Ready() with { Locked = true }));
        Assert.Equal(AutoRefreshDecision.Paused, ClaudeAutoRefresh.Decide(Ready() with { Asleep = true }));
        Assert.Equal(AutoRefreshDecision.Hidden, ClaudeAutoRefresh.Decide(Ready() with { Hidden = true }));
    }

    [Fact]
    public void A_pause_after_a_resume_holds_it_back_until_it_has_passed()
    {
        Assert.Equal(AutoRefreshDecision.NotDue, ClaudeAutoRefresh.Decide(Ready() with { NotBefore = Now.AddSeconds(10) }));
        Assert.Equal(AutoRefreshDecision.Run, ClaudeAutoRefresh.Decide(Ready() with { NotBefore = Now.AddSeconds(-1) }));
    }
}

/// <summary>The schedule itself: ticks, skips, back-off, pause and resume, with a hand-wound clock and a timer the test drives.</summary>
public sealed class ClaudeAutoRefreshScheduleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PomoQuoteAuto-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();
    private readonly ClaudeSnapshotStore _store;
    private bool _hidden;
    private readonly List<ManualTimer> _timers = [];
    private readonly List<RefreshOutcome> _script = [];
    private int _calls;

    public ClaudeAutoRefreshScheduleTests() => _store = new ClaudeSnapshotStore(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class ManualTimer : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private ClaudeAutoRefresh Create(Func<CancellationToken, Task<RefreshResult>>? refresh = null) => new(
        refresh ?? (_ =>
        {
            _calls++;
            var outcome = _script.Count > 0 ? _script[Math.Min(_calls - 1, _script.Count - 1)] : RefreshOutcome.Done;
            return Task.FromResult(new RefreshResult(outcome, outcome.ToString()));
        }),
        _store, _clock, () => _hidden,
        (_, _) =>
        {
            var timer = new ManualTimer();
            _timers.Add(timer);
            return timer;
        },
        watchSystemEvents: false);

    private void Snapshot(TimeSpan age, ClaudeSnapshotSource source = ClaudeSnapshotSource.StatusLine, double percent = 40)
    {
        var written = _clock.GetUtcNow() - age;
        _store.Write(new ClaudeSnapshot(written, source, "", source == ClaudeSnapshotSource.StatusLine ? "session1" : "manual", true, true,
            [new ClaudeSnapshotWindow(QuotaKind.FiveHour, percent, _clock.GetUtcNow().AddHours(2)), new ClaudeSnapshotWindow(QuotaKind.Weekly, 10, _clock.GetUtcNow().AddDays(3))]));
    }

    private void Minutes(ClaudeAutoRefresh auto, int minutes)
    {
        for (var i = 0; i < minutes; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            auto.Tick();
        }
    }

    [Fact]
    public void Off_by_default_creates_no_timer_and_never_runs()
    {
        using var auto = Create();

        auto.Configure(enabled: false, 30);
        Minutes(auto, 200);

        Assert.False(auto.Scheduled);
        Assert.Empty(_timers);
        Assert.Equal(0, _calls);
    }

    [Fact]
    public void Turning_it_on_runs_at_once_when_no_session_keeps_the_numbers_fresh_and_then_once_per_interval()
    {
        using var auto = Create();

        auto.Configure(true, 30);
        Assert.Equal(1, _calls);
        Minutes(auto, 29);
        Assert.Equal(1, _calls);
        Minutes(auto, 1);
        Assert.Equal(2, _calls);
        Minutes(auto, 30);
        Assert.Equal(3, _calls);
        Assert.True(auto.Scheduled);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(60)]
    public void The_interval_choices_are_followed(int minutes)
    {
        using var auto = Create();

        auto.Configure(true, minutes);
        Minutes(auto, minutes - 1);
        Assert.Equal(1, _calls);
        Minutes(auto, 1);
        Assert.Equal(2, _calls);
    }

    [Fact]
    public void An_interval_that_is_not_offered_becomes_thirty_minutes()
    {
        using var auto = Create();

        auto.Configure(true, 7);
        Minutes(auto, 29);
        Assert.Equal(1, _calls);
        Minutes(auto, 1);
        Assert.Equal(2, _calls);
    }

    [Fact]
    public void A_status_line_snapshot_newer_than_half_the_interval_skips_it_and_an_older_one_does_not()
    {
        Snapshot(TimeSpan.FromMinutes(5));
        using var auto = Create();

        auto.Configure(true, 30);
        Assert.Equal(0, _calls); // a session wrote the numbers 5 minutes ago
        Minutes(auto, 9);
        Assert.Equal(0, _calls); // now 14 minutes old
        Minutes(auto, 2);
        Assert.Equal(1, _calls); // 16 minutes old: older than half the interval
    }

    [Fact]
    public void A_window_at_100_percent_is_not_asked_while_it_lasts_and_the_next_look_happens_after_it_is_gone()
    {
        Snapshot(TimeSpan.FromMinutes(40), percent: 100);
        using var auto = Create();

        auto.Configure(true, 30);
        Minutes(auto, 110);
        Assert.Equal(0, _calls); // limited: nothing is spent

        Minutes(auto, 15); // the 5-hour window reset 2 hours after it was written
        Assert.Equal(1, _calls);
    }

    [Theory]
    [InlineData(RefreshOutcome.Failed)]
    [InlineData(RefreshOutcome.NotInstalled)]
    [InlineData(RefreshOutcome.TimedOut)]
    [InlineData(RefreshOutcome.NoLimits)]
    public void A_failed_refresh_is_not_retried_on_every_tick_but_on_the_next_interval(RefreshOutcome outcome)
    {
        _script.Add(outcome);
        using var auto = Create();

        auto.Configure(true, 30);
        Assert.Equal(1, _calls);
        Minutes(auto, 29);

        Assert.Equal(1, _calls); // 29 ticks, no retry
        Assert.Equal(outcome, auto.LastOutcome);
        Minutes(auto, 1);
        Assert.Equal(2, _calls); // the next interval
    }

    [Fact]
    public void A_refresh_by_hand_that_worked_lets_the_schedule_start_again()
    {
        _script.Add(RefreshOutcome.Failed);
        using var auto = Create();
        auto.Configure(true, 30);
        Minutes(auto, 10);
        Assert.Equal(1, _calls);

        auto.ForgetFailure();
        Snapshot(TimeSpan.FromMinutes(20), ClaudeSnapshotSource.ManualRefresh); // what the hand refresh left, 20 minutes ago
        auto.Tick();

        Assert.Equal(2, _calls);
    }

    [Theory]
    [InlineData(RefreshOutcome.TooSoon)]
    [InlineData(RefreshOutcome.Busy)]
    public void A_refresh_that_could_not_start_because_of_one_by_hand_is_looked_at_again_at_the_next_tick(RefreshOutcome outcome)
    {
        _script.Add(outcome);
        _script.Add(RefreshOutcome.Done);
        using var auto = Create();

        auto.Configure(true, 30);
        Assert.Equal(1, _calls);
        _clock.Advance(TimeSpan.FromMinutes(1));
        auto.Tick();

        Assert.Equal(2, _calls);
    }

    [Fact]
    public void A_hidden_widget_pauses_it_and_showing_it_again_resumes_cleanly()
    {
        _hidden = true;
        using var auto = Create();

        auto.Configure(true, 30);
        Minutes(auto, 90);
        Assert.Equal(0, _calls);

        _hidden = false;
        auto.Tick();
        Assert.Equal(1, _calls);
    }

    [Fact]
    public void A_locked_pc_pauses_it_and_the_unlock_waits_a_little_for_the_network()
    {
        using var auto = Create();
        auto.Configure(true, 30);
        Assert.Equal(1, _calls);

        auto.OnSessionSwitch(null, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock));
        Minutes(auto, 60);
        Assert.Equal(1, _calls); // locked for an hour: nothing

        auto.OnSessionSwitch(null, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock));
        _clock.Advance(TimeSpan.FromSeconds(30));
        auto.Tick();
        Assert.Equal(1, _calls); // 30 s after the unlock: still waiting for the network
        _clock.Advance(TimeSpan.FromSeconds(30));
        auto.Tick();
        Assert.Equal(2, _calls);
    }

    [Fact]
    public async Task Unlocks_signalled_from_another_thread_while_ticks_run_are_seen_without_a_torn_value()
    {
        using var auto = Create();
        auto.Configure(true, 15);
        var stop = false;
        var signals = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                auto.OnSessionSwitch(null, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock));
                auto.OnSessionSwitch(null, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock));
            }
        });

        for (var i = 0; i < 20000; i++) auto.Tick();
        Volatile.Write(ref stop, true);
        await signals;

        auto.OnSessionSwitch(null, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock));
        _clock.Advance(ClaudeAutoRefresh.ResumeDelay + TimeSpan.FromMinutes(16));
        auto.Tick();
        Assert.True(_calls >= 2); // the first look at the start, and one after the pause and the interval
    }

    [Fact]
    public void A_sleeping_pc_pauses_it_and_a_resume_waits_a_little()
    {
        using var auto = Create();
        auto.Configure(true, 30);

        auto.OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Suspend));
        _clock.Advance(TimeSpan.FromHours(5));
        auto.Tick();
        Assert.Equal(1, _calls);

        auto.OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Resume));
        auto.Tick();
        Assert.Equal(1, _calls);
        _clock.Advance(ClaudeAutoRefresh.ResumeDelay + TimeSpan.FromSeconds(1));
        auto.Tick();
        Assert.Equal(2, _calls);
    }

    [Fact]
    public void Turning_it_off_stops_the_timer_and_cancels_a_refresh_that_is_running()
    {
        var gate = new TaskCompletionSource<RefreshResult>();
        CancellationToken seen = default;
        using var auto = Create(token =>
        {
            seen = token;
            return gate.Task;
        });
        auto.Configure(true, 30);
        Assert.True(auto.Running);

        auto.Configure(false, 30);

        Assert.False(auto.Scheduled);
        Assert.True(_timers[0].Disposed);
        Assert.True(seen.IsCancellationRequested);
        gate.SetCanceled();
    }

    [Fact]
    public void Disposing_stops_the_timer_and_cancels_a_running_refresh_and_a_late_answer_changes_nothing()
    {
        var gate = new TaskCompletionSource<RefreshResult>();
        CancellationToken seen = default;
        var auto = Create(token =>
        {
            seen = token;
            return gate.Task;
        });
        auto.Configure(true, 30);

        auto.Dispose();
        gate.SetResult(new RefreshResult(RefreshOutcome.Done, "late"));

        Assert.True(seen.IsCancellationRequested);
        Assert.True(_timers[0].Disposed);
        Assert.Null(auto.LastOutcome);
        auto.Configure(true, 30); // ignored after dispose
        Assert.False(auto.Scheduled);
    }

    [Fact]
    public void Only_one_refresh_runs_at_a_time()
    {
        var gate = new TaskCompletionSource<RefreshResult>();
        var calls = 0;
        using var auto = Create(_ =>
        {
            calls++;
            return gate.Task;
        });
        auto.Configure(true, 15);

        Minutes(auto, 60);

        Assert.Equal(1, calls);
        gate.SetResult(new RefreshResult(RefreshOutcome.Done, "ok"));
    }

    [Fact]
    public void A_damaged_snapshot_folder_does_not_stop_the_schedule()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "garbage.json"), "{not json");
        using var auto = Create();

        auto.Configure(true, 30);

        Assert.Equal(1, _calls);
    }
}

/// <summary>The setting: off by default, kept in usage.json, tolerant of a damaged file.</summary>
public sealed class ClaudeAutoRefreshSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PomoQuoteAutoSet-" + Guid.NewGuid().ToString("N"));

    public ClaudeAutoRefreshSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void It_is_off_by_default_with_thirty_minutes()
    {
        Assert.False(UsageSettings.Default.ClaudeAutoRefresh);
        Assert.Equal(30, UsageSettings.Default.ClaudeAutoRefreshMinutes);
        Assert.False(UsageSettings.Default.ClaudeAutoRefreshAcknowledged);
        Assert.Equal([15, 30, 60], UsageSettings.AutoRefreshChoices);
    }

    [Fact]
    public void The_choice_survives_a_save_and_a_load()
    {
        var store = new UsageSettingsStore(Path.Combine(_dir, "usage.json"));

        store.Save(UsageSettings.Default with { ClaudeAutoRefresh = true, ClaudeAutoRefreshMinutes = 60, ClaudeAutoRefreshAcknowledged = true });
        var loaded = store.Load();

        Assert.True(loaded.ClaudeAutoRefresh);
        Assert.Equal(60, loaded.ClaudeAutoRefreshMinutes);
        Assert.True(loaded.ClaudeAutoRefreshAcknowledged);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    public void A_damaged_file_gives_the_defaults_with_the_refresh_off(string content)
    {
        var path = Path.Combine(_dir, "usage.json");
        File.WriteAllText(path, content);

        var loaded = new UsageSettingsStore(path).Load();

        Assert.False(loaded.ClaudeAutoRefresh);
        Assert.Equal(30, loaded.ClaudeAutoRefreshMinutes);
    }

    [Fact]
    public void A_file_from_an_older_version_has_the_refresh_off()
    {
        var path = Path.Combine(_dir, "usage.json");
        File.WriteAllText(path, """{"EnabledProviders":["claude"],"Order":["codex","claude"],"IntervalMinutes":3,"DisplayMode":"Bars","AlertsEnabled":true,"Thresholds":[80,95]}""");

        var loaded = new UsageSettingsStore(path).Load();

        Assert.True(loaded.IsEnabled("claude"));
        Assert.False(loaded.ClaudeAutoRefresh);
        Assert.Equal(30, loaded.ClaudeAutoRefreshMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-5)]
    [InlineData(1000)]
    public void An_interval_that_is_not_offered_becomes_thirty(int minutes)
    {
        var path = Path.Combine(_dir, "usage.json");
        new UsageSettingsStore(path).Save(UsageSettings.Default with { ClaudeAutoRefresh = true, ClaudeAutoRefreshMinutes = minutes });

        var loaded = new UsageSettingsStore(path).Load();

        Assert.True(loaded.ClaudeAutoRefresh);
        Assert.Equal(30, loaded.ClaudeAutoRefreshMinutes);
    }
}

/// <summary>The widget text, the refresh note and the Settings row.</summary>
public sealed class ClaudeAutoRefreshViewTests
{
    private static UsageStatus Status(UsageDataSource dataSource)
    {
        var now = new DateTimeOffset(2026, 11, 25, 6, 0, 0, TimeSpan.Zero);
        var snapshot = new UsageSnapshot(new UsageSource("claude", UsageEnvironment.Windows, "", ""),
            [new QuotaWindow(QuotaKind.FiveHour, 300, 20, now.AddHours(2), "claude", true), new QuotaWindow(QuotaKind.Weekly, 10080, 30, now.AddDays(3), "claude", true)],
            now, dataSource, UsageState.Ok);
        return new UsageStatus("claude", "Claude", snapshot, snapshot);
    }

    private static string Source(UsageDataSource dataSource, bool auto) =>
        UsageViewBuilder.Build(Status(dataSource), UsageSettings.Default with { ClaudeAutoRefresh = auto }, new DateTimeOffset(2026, 11, 25, 6, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc).Source;

    [Fact]
    public void Numbers_the_refresh_read_say_auto_refresh_when_the_schedule_is_on_and_refreshed_by_hand_when_it_is_off()
    {
        Assert.Equal("Windows · auto refresh", Source(UsageDataSource.ManualRefresh, auto: true));
        Assert.Equal("Windows · refreshed by hand", Source(UsageDataSource.ManualRefresh, auto: false));
    }

    [Fact]
    public void Status_line_numbers_are_unchanged()
    {
        Assert.Equal("Windows", Source(UsageDataSource.ClaudeStatusLine, auto: true));
        Assert.Equal("Windows", Source(UsageDataSource.ClaudeStatusLine, auto: false));
    }

    [Fact]
    public void The_stale_marking_is_unchanged_for_refreshed_numbers()
    {
        var view = UsageViewBuilder.Build(Status(UsageDataSource.ManualRefresh), UsageSettings.Default with { ClaudeAutoRefresh = true }, new DateTimeOffset(2026, 11, 25, 6, 0, 0, TimeSpan.Zero).AddMinutes(10), TimeZoneInfo.Utc);

        Assert.StartsWith("stale", view.Note);
    }

    [Fact]
    public void The_notes_say_plainly_that_each_refresh_counts_against_the_quota_and_when_it_does_not_run()
    {
        Assert.Contains("very small amount", ClaudeActions.AutoRefreshNote);
        Assert.Contains("quota", ClaudeActions.AutoRefreshNote);
        Assert.Contains("locked or asleep", ClaudeActions.AutoRefreshNote);
        Assert.Contains("hidden to the tray", ClaudeActions.AutoRefreshNote);
        Assert.DoesNotContain("never automatically", ClaudeActions.RefreshNote);
    }

    [Fact]
    public void The_settings_dialog_has_the_row_with_three_interval_choices_and_asks_once_before_turning_it_on()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory.Split("PomoQuote.Tests")[0], "UsageSettingsDialog.cs"));

        Assert.Contains("Refresh Claude usage automatically", source);
        Assert.Contains("UsageSettings.AutoRefreshChoices", source);
        Assert.Contains("AutoRefreshNoteSeen", source);
        Assert.Contains("ClaudeAutoRefreshMinutes", source);
        Assert.DoesNotContain("PasswordChar", source);
    }
}

/// <summary>The real chain (schedule, refresher, process): nothing starts when it is off, a process that hangs is killed with its children.</summary>
[Collection("process")]
public sealed class ClaudeAutoRefreshProcessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PomoQuoteAutoProc-" + Guid.NewGuid().ToString("N"));

    public ClaudeAutoRefreshProcessTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private sealed class CountingFactory : IClaudeSessionFactory
    {
        public int Starts { get; private set; }

        public IJsonLineSession Start(IReadOnlyList<string> arguments, string workingDirectory)
        {
            Starts++;
            return new FakeSession(_ => []).WithQueued("{\"type\":\"result\",\"is_error\":false,\"result\":\"ok\"}");
        }
    }

    private sealed class HangingScriptFactory : IClaudeSessionFactory
    {
        public IJsonLineSession Start(IReadOnlyList<string> arguments, string workingDirectory) =>
            ProcessJsonLineSession.Start("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "Start-Process ping -ArgumentList '-n','41','127.0.0.1' -WindowStyle Hidden; Start-Sleep -Seconds 120"], workingDirectory, closeInput: true);
    }

    private static readonly ManualTimerFactory Timers = new();

    private sealed class ManualTimerFactory
    {
        public IDisposable Start(TimeSpan period, Action tick) => new Stub();

        private sealed class Stub : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private ClaudeAutoRefresh Auto(IClaudeSessionFactory factory, TimeSpan? timeout = null)
    {
        var store = new ClaudeSnapshotStore(_dir);
        var refresher = new ClaudeRefresher(factory, store, TimeProvider.System, () => _dir, timeout ?? TimeSpan.FromSeconds(30));
        return new ClaudeAutoRefresh(refresher.RefreshAsync, store, TimeProvider.System, () => false, Timers.Start, watchSystemEvents: false);
    }

    [Fact]
    public void Nothing_is_started_while_the_setting_is_off()
    {
        var factory = new CountingFactory();
        using var auto = Auto(factory);

        auto.Configure(false, 30);
        for (var i = 0; i < 100; i++) auto.Tick();

        Assert.Equal(0, factory.Starts);
        Assert.False(auto.Scheduled);
    }

    [Fact]
    public async Task Turned_on_it_starts_the_one_request_refresh_exactly_once_per_attempt_through_the_same_refresher()
    {
        var factory = new CountingFactory();
        using var auto = Auto(factory);
        OnSta(() => auto.Configure(true, 30));
        for (var i = 0; i < 100 && auto.Running; i++) await Task.Delay(50);

        Assert.Equal(1, factory.Starts);
        Assert.Equal(RefreshOutcome.NoLimits, auto.LastOutcome); // the fake answered without plan limits
    }

    [Fact]
    public async Task A_hung_process_is_killed_with_its_children_when_the_schedule_is_turned_off()
    {
        var before = Process.GetProcessesByName("PING").Length;
        using var auto = Auto(new HangingScriptFactory());
        OnSta(() => auto.Configure(true, 30));
        for (var i = 0; i < 100 && ProcessJsonLineSession.LiveCount == 0; i++) await Task.Delay(50);
        Assert.Equal(1, ProcessJsonLineSession.LiveCount);

        OnSta(() => auto.Configure(false, 30));
        for (var i = 0; i < 200 && ProcessJsonLineSession.LiveCount > 0; i++) await Task.Delay(50);

        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
        await Task.Delay(500);
        Assert.True(Process.GetProcessesByName("PING").Length <= before, "a child process of the refresh is still running");
    }

    [Fact]
    public async Task A_process_that_never_answers_is_killed_at_the_refresher_time_limit_and_the_schedule_waits_an_interval()
    {
        var before = Process.GetProcessesByName("PING").Length;
        using var auto = Auto(new HangingScriptFactory(), TimeSpan.FromSeconds(3));
        var watch = Stopwatch.StartNew();
        OnSta(() => auto.Configure(true, 30));
        for (var i = 0; i < 300 && (auto.Running || auto.LastOutcome is null); i++) await Task.Delay(50);

        Assert.Equal(RefreshOutcome.TimedOut, auto.LastOutcome);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"took {watch.Elapsed}");
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
        await Task.Delay(500);
        Assert.True(Process.GetProcessesByName("PING").Length <= before);
    }

    /// <summary>The schedule is made for the UI thread (its awaits come back to it): run the calls on a short-lived STA thread with a message pump.</summary>
    private static void OnSta(Action body)
    {
        var done = new ManualResetEventSlim();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
                var until = DateTime.UtcNow.AddMilliseconds(100);
                while (DateTime.UtcNow < until) Application.DoEvents();
            }
            catch (Exception ex) { failure = ex; }
            finally { done.Set(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(done.Wait(TimeSpan.FromSeconds(30)));
        if (failure is not null) throw new InvalidOperationException(failure.Message, failure);
    }
}

/// <summary>The real window: the schedule follows the setting that is applied, and the widget layout does not change.</summary>
[Collection("process")]
public sealed class ClaudeAutoRefreshWidgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteAutoForm-" + Guid.NewGuid().ToString("N"));

    public ClaudeAutoRefreshWidgetTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    private sealed class CountingFactory : IClaudeSessionFactory
    {
        public int Starts { get; private set; }

        public IJsonLineSession Start(IReadOnlyList<string> arguments, string workingDirectory)
        {
            Starts++;
            return new FakeSession(_ => []).WithQueued("{\"type\":\"result\",\"is_error\":false,\"result\":\"ok\"}");
        }
    }

    private MainForm NewForm(CountingFactory factory, UsageSettings settings)
    {
        var usagePath = Path.Combine(_root, "usage.json");
        new UsageSettingsStore(usagePath).Save(settings);
        var store = new ClaudeSnapshotStore(Path.Combine(_root, "usage"));
        var setup = new ClaudeSetup(
            new ClaudeBridgeInstaller(Path.Combine(_root, "claude-settings.json"), Path.Combine(_root, "state"), () => Path.Combine(_root, "PomoQuote.exe")),
            new ClaudeRefresher(factory, store, TimeProvider.System, () => _root),
            store);
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        return new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            new QuoteSettingsStore(Path.Combine(_root, "quotes.json")), new BundledQuoteProvider(), new Random(1),
            new UsageSettingsStore(usagePath), new UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            [], setup, new WeatherEffectSettingsStore(Path.Combine(_root, "weather-effects.json")))
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000),
            WorkAreaProvider = () => [new Rectangle(-6000, -6000, 4000, 4000)],
        };
    }

    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(120)), "the form test did not finish");
        if (failure is not null) throw new InvalidOperationException("The form test failed: " + failure.Message, failure);
    }

    private static void Pump(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    [Fact]
    public void Off_by_default_nothing_is_scheduled_and_no_process_is_started_and_the_layout_is_the_same()
    {
        OnStaThread(() =>
        {
            var factory = new CountingFactory();
            using var form = NewForm(factory, UsageSettings.Default);
            form.Show();
            Pump(TimeSpan.FromMilliseconds(500));

            Assert.False(form.AutoRefreshForTests.Scheduled);
            Assert.Equal(0, factory.Starts);
            Assert.Equal(280, UsageGeometry.WidgetWidth);
        });
    }

    [Fact]
    public void Applying_the_setting_starts_and_stops_the_schedule_and_the_saved_choice_starts_it_at_the_next_start()
    {
        OnStaThread(() =>
        {
            var factory = new CountingFactory();
            using (var form = NewForm(factory, UsageSettings.Default))
            {
                form.Show();
                Pump(TimeSpan.FromMilliseconds(300));

                form.UsageForTests.Apply(form.UsageForTests.Settings with { ClaudeAutoRefresh = true, ClaudeAutoRefreshMinutes = 15 });
                Pump(TimeSpan.FromMilliseconds(800));
                Assert.True(form.AutoRefreshForTests.Scheduled);
                Assert.Equal(1, factory.Starts);

                form.UsageForTests.Apply(form.UsageForTests.Settings with { ClaudeAutoRefresh = false });
                Pump(TimeSpan.FromMilliseconds(300));
                Assert.False(form.AutoRefreshForTests.Scheduled);
            }

            var second = new CountingFactory();
            new UsageSettingsStore(Path.Combine(_root, "usage.json")).Save(UsageSettings.Default with { ClaudeAutoRefresh = true });
            using var next = NewForm(second, UsageSettings.Default with { ClaudeAutoRefresh = true });
            next.Show();
            Pump(TimeSpan.FromMilliseconds(800));
            Assert.True(next.AutoRefreshForTests.Scheduled);
        });
    }

    [Fact]
    public void Closing_the_window_leaves_no_timer_and_no_process()
    {
        OnStaThread(() =>
        {
            var factory = new CountingFactory();
            var form = NewForm(factory, UsageSettings.Default with { ClaudeAutoRefresh = true });
            form.Show();
            Pump(TimeSpan.FromMilliseconds(800));
            var auto = form.AutoRefreshForTests;

            form.Dispose();

            Assert.False(auto.Scheduled);
            Assert.Equal(0, ProcessJsonLineSession.LiveCount);
        });
    }
}
