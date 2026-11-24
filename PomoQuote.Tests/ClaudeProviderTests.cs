using System.Diagnostics;
using PomoQuote.AiUsage;
using PomoQuote.AiUsage.Claude;

namespace PomoQuote.Tests;

public sealed class ClaudeProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private ClaudeSnapshotStore Store => new(_dir);

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 24, 6, 0, 0, TimeSpan.Zero);

    private ClaudeSnapshotProvider Provider(StatusLineState state, MutableClock? clock = null) =>
        new(Store, () => state, clock ?? new MutableClock(T0));

    private void Save(DateTimeOffset at, double five = 18, double week = 28, bool limits = true, bool response = true, string session = "aaaaaaaaaaaa", ClaudeSnapshotSource source = ClaudeSnapshotSource.StatusLine, DateTimeOffset? fiveReset = null, DateTimeOffset? weekReset = null)
    {
        var windows = limits
            ? new List<ClaudeSnapshotWindow> { new(QuotaKind.FiveHour, five, fiveReset ?? T0.AddHours(3)), new(QuotaKind.Weekly, week, weekReset ?? T0.AddDays(3)) }
            : [];
        Store.Write(new ClaudeSnapshot(at, source, "2.1.331", session, response, limits, windows));
    }

    [Fact]
    public async Task Real_numbers_become_an_ok_snapshot_with_both_windows_percent_used_and_the_time_they_were_written()
    {
        Save(T0.AddMinutes(-1));

        var snapshot = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.Equal(T0.AddMinutes(-1), snapshot.ObservedAt);
        Assert.Equal(UsageDataSource.ClaudeStatusLine, snapshot.DataSource);
        Assert.Equal([QuotaKind.FiveHour, QuotaKind.Weekly], snapshot.Windows.Select(w => w.Kind).ToArray());
        Assert.Equal(18, snapshot.Windows[0].PercentUsed);
        Assert.Equal(300, snapshot.Windows[0].DurationMinutes);
        Assert.Equal(10080, snapshot.Windows[1].DurationMinutes);
        Assert.True(snapshot.Windows[0].IsResetReal);
        Assert.Equal("claude", snapshot.Source.ProviderId);
    }

    [Fact]
    public async Task The_newest_session_wins_when_several_run_at_once()
    {
        Save(T0.AddMinutes(-10), five: 10, session: "old");
        Save(T0.AddMinutes(-2), five: 55, session: "new");

        var snapshot = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);

        Assert.Equal(55, snapshot.Windows[0].PercentUsed);
    }

    [Fact]
    public async Task A_window_whose_reset_has_passed_is_dropped_and_the_other_one_stays()
    {
        Save(T0.AddMinutes(-1), fiveReset: T0.AddMinutes(-5));

        var snapshot = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);

        Assert.Equal(QuotaKind.Weekly, Assert.Single(snapshot.Windows).Kind);
    }

    [Fact]
    public async Task When_every_window_has_reset_it_waits_for_the_next_reply_instead_of_showing_old_numbers()
    {
        Save(T0.AddMinutes(-1), fiveReset: T0.AddMinutes(-5), weekReset: T0.AddMinutes(-1));

        var snapshot = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Waiting, snapshot.State);
    }

    [Fact]
    public async Task Numbers_that_are_hours_old_are_still_returned_and_the_view_marks_them_stale_with_their_time()
    {
        Save(T0.AddMinutes(-30), fiveReset: T0.AddHours(4), weekReset: T0.AddDays(3));
        var provider = Provider(StatusLineState.Bridged);

        var snapshot = await provider.ReadAsync(CancellationToken.None);
        var view = UsageViewBuilder.Build(new UsageStatus("claude", "Claude", snapshot, snapshot), UsageSettings.Default.WithEnabled("claude", true), T0, TimeZoneInfo.Utc);

        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.True(view.IsProblem);
        Assert.Equal("stale, from 05:30", view.Note);
        Assert.Equal(2, view.Rows.Count);
    }

    [Theory]
    [InlineData(StatusLineState.None)]
    [InlineData(StatusLineState.Custom)]
    public async Task Without_the_bridge_and_without_any_snapshot_it_is_not_set_up(StatusLineState state)
    {
        var snapshot = await Provider(state).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NotSetUp, snapshot.State);
        Assert.Contains("Settings > AI Usage", snapshot.ErrorText);
    }

    [Fact]
    public async Task Unreadable_settings_and_no_snapshot_say_so()
    {
        var snapshot = await Provider(StatusLineState.Unreadable).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NotSetUp, snapshot.State);
        Assert.Contains("cannot be read", snapshot.ErrorText);
    }

    [Fact]
    public async Task A_manual_refresh_snapshot_is_used_even_when_the_bridge_is_not_installed()
    {
        Save(T0.AddMinutes(-1), source: ClaudeSnapshotSource.ManualRefresh, session: "manual");

        var snapshot = await Provider(StatusLineState.None).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.Equal(UsageDataSource.ManualRefresh, snapshot.DataSource);
    }

    [Fact]
    public async Task Bridged_with_no_snapshot_waits_for_a_session()
    {
        var snapshot = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Waiting, snapshot.State);
        Assert.Contains("waiting for first reply", snapshot.ErrorText);
    }

    [Fact]
    public async Task A_session_that_has_not_had_its_first_reply_is_waiting_not_an_error()
    {
        Save(T0.AddSeconds(-5), limits: false, response: false);

        var snapshot = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Waiting, snapshot.State);
        Assert.Contains("first reply", snapshot.ErrorText);
    }

    [Fact]
    public async Task A_reply_without_rate_limits_means_this_login_has_no_plan_limits()
    {
        Save(T0.AddSeconds(-5), limits: false, response: true);

        var snapshot = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NoPlanLimits, snapshot.State);
    }

    [Fact]
    public async Task Last_numbers_survive_a_later_session_without_limits()
    {
        Save(T0.AddMinutes(-20), five: 33, session: "a");
        Save(T0.AddMinutes(-1), limits: false, response: false, session: "b");

        var snapshot = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.Equal(33, snapshot.Windows[0].PercentUsed);
    }

    [Fact]
    public async Task Damaged_snapshot_files_do_not_break_the_provider()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "claude-windows-bad.json"), "{{{");

        var snapshot = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Waiting, snapshot.State);
    }

    [Fact]
    public void The_provider_polls_often_because_it_only_reads_small_files()
    {
        IUsageProvider provider = Provider(StatusLineState.Bridged);

        Assert.Equal(TimeSpan.FromSeconds(10), provider.PollInterval);
    }

    [Fact]
    public async Task The_view_text_for_every_claude_state_is_short_and_clear()
    {
        string Note(UsageSnapshot s) => UsageViewBuilder.Build(new UsageStatus("claude", "Claude", s, null), UsageSettings.Default.WithEnabled("claude", true), T0, TimeZoneInfo.Utc).Note;
        var notSetUp = await Provider(StatusLineState.None).ReadAsync(CancellationToken.None);
        var waiting = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);
        Save(T0.AddSeconds(-5), limits: false, response: true);
        var noLimits = await Provider(StatusLineState.Bridged).ReadAsync(CancellationToken.None);

        Assert.Equal("not set up", Note(notSetUp));
        Assert.Contains("waiting for first reply", Note(waiting));
        Assert.Equal("no plan limits", Note(noLimits));
    }
}

[Collection("process")]
public sealed class ClaudeRefresherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    public ClaudeRefresherTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeFactory(Func<FakeSession> create) : IClaudeSessionFactory
    {
        public List<IReadOnlyList<string>> Starts { get; } = [];

        public string? WorkingDirectory { get; private set; }

        public FakeSession? Last { get; private set; }

        public IJsonLineSession Start(IReadOnlyList<string> arguments, string workingDirectory)
        {
            Starts.Add(arguments);
            WorkingDirectory = workingDirectory;
            return Last = create();
        }
    }

    private sealed class MissingFactory : IClaudeSessionFactory
    {
        public IJsonLineSession Start(IReadOnlyList<string> arguments, string workingDirectory) => throw new FileNotFoundException();
    }

    private const string System1 = "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"private-session\",\"cwd\":\"C:/Users/someone/proj\",\"email\":\"someone@example.com\"}";
    private const string Assistant = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}}";
    private const string Event = "{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"resetsAt\":1795500600,\"rateLimitType\":\"five_hour\",\"unifiedWindows\":{\"five_hour\":{\"utilization\":0.184,\"resetsAt\":1795500600,\"status\":\"allowed\"},\"seven_day\":{\"utilization\":0.28,\"resetsAt\":1795896000,\"status\":\"allowed\"}}},\"session_id\":\"private-session\"}";
    private const string Result = "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"ok\",\"total_cost_usd\":0.001}";

    private ClaudeRefresher Refresher(IClaudeSessionFactory factory, MutableClock? clock = null, TimeSpan? timeout = null) =>
        new(factory, new ClaudeSnapshotStore(_dir), clock ?? new MutableClock(new DateTimeOffset(2026, 11, 24, 6, 0, 0, TimeSpan.Zero)), () => _dir, timeout ?? TimeSpan.FromSeconds(5));

    private static FakeFactory Replies(params string[] lines) => new(() => new FakeSession(_ => [], hangWhenEmpty: false).WithQueued(lines));

    [Fact]
    public async Task A_good_answer_is_converted_to_percent_used_and_saved_as_a_manual_refresh_snapshot()
    {
        var factory = Replies(System1, Assistant, Event, Result);

        var result = await Refresher(factory).RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.Done, result.Outcome);
        var windows = result.Snapshot!.Windows;
        Assert.Equal(18.4, windows[0].PercentUsed, 3);
        Assert.Equal(28, windows[1].PercentUsed, 3);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795500600), windows[0].ResetsAt);
        var saved = Assert.Single(new ClaudeSnapshotStore(_dir).ReadAll());
        Assert.Equal(ClaudeSnapshotSource.ManualRefresh, saved.Source);
        Assert.Equal("manual", saved.SessionHash);
        Assert.True(factory.Last!.Disposed);
    }

    [Fact]
    public async Task It_runs_one_tiny_request_with_the_smallest_model_in_a_throwaway_folder_without_user_settings()
    {
        var factory = Replies(Event, Result);

        await Refresher(factory).RefreshAsync(CancellationToken.None);

        var args = Assert.Single(factory.Starts);
        Assert.Contains("-p", args);
        Assert.Equal("haiku", args[args.ToList().IndexOf("--model") + 1]);
        Assert.Equal("project", args[args.ToList().IndexOf("--setting-sources") + 1]); // the user's own settings and status line are not loaded
        Assert.Contains("--output-format", args);
        Assert.Contains("stream-json", args);
        Assert.Contains("--no-session-persistence", args);
        Assert.Contains("Reply with the single word ok.", args);
        Assert.Equal(_dir, factory.WorkingDirectory);
    }

    [Fact]
    public async Task Nothing_of_the_prompt_the_answer_or_the_session_is_kept_in_the_snapshot_file()
    {
        await Refresher(Replies(System1, Assistant, Event, Result)).RefreshAsync(CancellationToken.None);

        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "claude-windows-*.json")));
        foreach (var word in new[] { "private-session", "someone", "example.com", "ok\"", "Reply", "cost", "assistant", "proj" })
        {
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task An_error_result_without_limits_is_a_failure_with_a_generic_message()
    {
        var factory = Replies(System1, "{\"type\":\"result\",\"subtype\":\"error\",\"is_error\":true,\"result\":\"Invalid API key: sk-ant-private for someone@example.com\"}");

        var result = await Refresher(factory).RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("sk-ant", result.Message);
        Assert.DoesNotContain("someone", result.Message);
        Assert.Empty(new ClaudeSnapshotStore(_dir).ReadAll());
    }

    [Fact]
    public async Task Garbage_lines_are_skipped_and_an_answer_without_limits_means_no_plan_limits()
    {
        var factory = Replies("not json", "{broken", "[]", Assistant, Result);

        var result = await Refresher(factory).RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.NoLimits, result.Outcome);
    }

    [Fact]
    public async Task Only_garbage_and_then_the_stream_ends_is_a_failure()
    {
        var result = await Refresher(Replies("garbage", "more garbage")).RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task A_program_that_is_not_installed_is_reported_without_throwing()
    {
        var result = await Refresher(new MissingFactory()).RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.NotInstalled, result.Outcome);
    }

    [Fact]
    public async Task A_claude_that_hangs_is_given_up_on_and_its_process_is_disposed()
    {
        var factory = new FakeFactory(() => new FakeSession(_ => [], hangWhenEmpty: true));

        var result = await Refresher(factory, timeout: TimeSpan.FromMilliseconds(300)).RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.TimedOut, result.Outcome);
        Assert.True(factory.Last!.Disposed);
    }

    [Fact]
    public async Task At_most_one_refresh_a_minute_and_never_two_at_once()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 24, 6, 0, 0, TimeSpan.Zero));
        var factory = Replies(Event, Result);
        var refresher = Refresher(factory, clock);

        Assert.Equal(RefreshOutcome.Done, (await refresher.RefreshAsync(CancellationToken.None)).Outcome);
        clock.Now = clock.Now.AddSeconds(20);
        var tooSoon = await refresher.RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.TooSoon, tooSoon.Outcome);
        Assert.Contains("40 s", tooSoon.Message);
        Assert.Equal(40, refresher.SecondsUntilAllowed());
        Assert.Single(factory.Starts);

        clock.Now = clock.Now.AddSeconds(41);
        Assert.Equal(0, refresher.SecondsUntilAllowed());
        Assert.Equal(RefreshOutcome.Done, (await refresher.RefreshAsync(CancellationToken.None)).Outcome);
        Assert.Equal(2, factory.Starts.Count);
    }

    [Fact]
    public async Task A_second_call_while_one_runs_returns_busy()
    {
        var factory = new FakeFactory(() => new FakeSession(_ => [], hangWhenEmpty: true));
        var refresher = Refresher(factory, timeout: TimeSpan.FromSeconds(3));

        var first = refresher.RefreshAsync(CancellationToken.None);
        await Task.Delay(300);
        var second = await refresher.RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.Busy, second.Outcome);
        Assert.Equal(RefreshOutcome.TimedOut, (await first).Outcome);
    }

    [Fact]
    public async Task The_request_is_started_off_the_calling_thread()
    {
        var release = new ManualResetEventSlim();
        var entered = new ManualResetEventSlim();
        var threadId = -1;
        var factory = new BlockingFactory(release, entered, id => threadId = id);
        var caller = Environment.CurrentManagedThreadId;

        var task = Refresher(factory).RefreshAsync(CancellationToken.None);

        Assert.False(task.IsCompleted);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(caller, threadId);
        release.Set();
        await task;
    }

    private sealed class BlockingFactory(ManualResetEventSlim release, ManualResetEventSlim entered, Action<int> record) : IClaudeSessionFactory
    {
        public IJsonLineSession Start(IReadOnlyList<string> arguments, string workingDirectory)
        {
            record(Environment.CurrentManagedThreadId);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(20));
            throw new FileNotFoundException();
        }
    }

    [Fact]
    public void The_locator_finds_claude_on_the_path_then_in_the_users_local_bin()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Users\u\.local\bin\claude.exe" };
        string? Find(params string[] dirs) => ClaudeLocator.FindExecutable(files.Contains, dirs, @"C:\Users\u");

        Assert.Equal(@"C:\Users\u\.local\bin\claude.exe", Find(@"C:\nowhere"));
        files.Add(@"C:\tools\claude.exe");
        Assert.Equal(@"C:\tools\claude.exe", Find(@"C:\nowhere", @"C:\tools"));
        files.Clear();
        Assert.Null(Find(@"C:\nowhere"));
    }

    // ---------- a real child process playing the part of `claude` ----------

    private sealed class ScriptFactory(string script) : IClaudeSessionFactory
    {
        public IJsonLineSession Start(IReadOnlyList<string> arguments, string workingDirectory) =>
            ProcessJsonLineSession.Start("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], workingDirectory, closeInput: true);
    }

    private static string Q(string json) => json.Replace("'", "''");

    [Fact]
    public async Task A_real_process_that_prints_the_events_gives_a_snapshot_and_exits()
    {
        var script = $"[Console]::Out.WriteLine('{Q(System1)}'); [Console]::Out.WriteLine('{Q(Event)}'); [Console]::Out.WriteLine('{Q(Result)}')";

        var result = await Refresher(new ScriptFactory(script), timeout: TimeSpan.FromSeconds(30)).RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.Done, result.Outcome);
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
    }

    [Fact]
    public async Task A_real_process_that_writes_a_line_in_two_pieces_is_assembled()
    {
        var script = $"[Console]::Out.Write('{Q(Event)[..40]}'); [Console]::Out.Flush(); Start-Sleep -Milliseconds 400; [Console]::Out.WriteLine('{Q(Event)[40..]}'); [Console]::Out.WriteLine('{Q(Result)}')";

        var result = await Refresher(new ScriptFactory(script), timeout: TimeSpan.FromSeconds(30)).RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.Done, result.Outcome);
    }

    [Fact]
    public async Task A_real_process_that_hangs_is_killed_at_the_timeout()
    {
        var clock = Stopwatch.StartNew();

        var result = await Refresher(new ScriptFactory("Start-Sleep -Seconds 120"), timeout: TimeSpan.FromSeconds(3)).RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.TimedOut, result.Outcome);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20));
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
    }

    [Fact]
    public async Task A_real_process_that_crashes_is_a_failure_and_one_that_prints_an_auth_error_too()
    {
        var crashed = await Refresher(new ScriptFactory("exit 5")).RefreshAsync(CancellationToken.None);
        var auth = await Refresher(new ScriptFactory($"[Console]::Out.WriteLine('{Q("{\"type\":\"result\",\"is_error\":true,\"result\":\"Please run /login\"}")}')")).RefreshAsync(CancellationToken.None);

        Assert.Equal(RefreshOutcome.Failed, crashed.Outcome);
        Assert.Equal(RefreshOutcome.Failed, auth.Outcome);
        Assert.DoesNotContain("/login", auth.Message);
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
    }
}
