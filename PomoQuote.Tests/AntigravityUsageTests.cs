using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using PomoQuote.AiUsage;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>The shape `agy -p /usage --output-format json` printed on a real PC (no account data is in it), and variations of it.</summary>
internal static class AgySamples
{
    public const string Real = """
        {"conversation_id":"","status":"SUCCESS","response":"Gemini Models\tWeekly Limit Remaining\t67%\t2026-12-02T00:48:34Z\nClaude and GPT models\tWeekly Limit Remaining\t100%\t2026-12-02T03:44:43Z\n","duration_seconds":0,"num_turns":0,"usage":{"input_tokens":0,"output_tokens":0,"thinking_tokens":0,"cache_read_tokens":0,"total_tokens":0},"command":{"name":"usage","data":{"description":"Within each group, models share a weekly limit.","groups":[{"name":"Gemini Models","description":"Models within this group: A, B","buckets":[{"id":"gemini-weekly","name":"Weekly Limit Remaining","description":"You have used some of your weekly limit.","window":"weekly","remaining_fraction":0.6695792078971863,"reset_time":"2026-12-02T00:48:34Z"}]},{"name":"Claude and GPT models","description":"Models within this group: C, D","buckets":[{"id":"3p-weekly","name":"Weekly Limit Remaining","window":"weekly","remaining_fraction":1,"reset_time":"2026-12-02T03:44:43Z"}]}]}}}
        """;

    public static string With(string groupsJson, string name = "usage", int turns = 0, long tokens = 0) =>
        "{\"status\":\"SUCCESS\",\"num_turns\":" + turns + ",\"usage\":{\"total_tokens\":" + tokens + "},\"command\":{\"name\":\"" + name + "\",\"data\":{\"groups\":" + groupsJson + "}}}";

    public static readonly DateTimeOffset Now = new(2026, 11, 25, 6, 0, 0, TimeSpan.Zero);
}

public sealed class AgyUsageParserTests
{
    [Fact]
    public void The_real_answer_gives_one_weekly_window_per_group_with_percent_used_from_the_remaining_fraction()
    {
        var windows = AgyUsageParser.Parse(AgySamples.Real);

        Assert.Equal(2, windows.Count);
        Assert.All(windows, w => Assert.Equal(QuotaKind.Weekly, w.Kind));
        Assert.All(windows, w => Assert.Equal(10080, w.DurationMinutes));
        Assert.Equal("Gemini Models", windows[0].Bucket);
        Assert.Equal((1 - 0.6695792078971863) * 100, windows[0].PercentUsed, 9);
        Assert.Equal("Claude and GPT models", windows[1].Bucket);
        Assert.Equal(0, windows[1].PercentUsed);
        Assert.Equal(new DateTimeOffset(2026, 12, 2, 0, 48, 34, TimeSpan.Zero), windows[0].ResetsAt);
        Assert.All(windows, w => Assert.True(w.IsResetReal));
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.75, 25.0)]
    [InlineData(0.25, 75.0)]
    [InlineData(0.0, 100.0)]
    [InlineData(1.5, 0.0)] // out of range is clamped, never negative
    [InlineData(-0.2, 100.0)]
    public void Percent_used_is_one_minus_the_remaining_fraction_times_100(double remaining, double expectedUsed)
    {
        var json = AgySamples.With($$"""[{"name":"G","buckets":[{"window":"weekly","remaining_fraction":{{remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"reset_time":"2026-12-02T00:00:00Z"}]}]""");

        Assert.Equal(expectedUsed, AgyUsageParser.Parse(json).Single().PercentUsed, 9);
    }

    [Theory]
    [InlineData("weekly", 10080, QuotaKind.Weekly)]
    [InlineData("Weekly", 10080, QuotaKind.Weekly)]
    [InlineData("five_hour", 300, QuotaKind.FiveHour)]
    [InlineData("five-hour", 300, QuotaKind.FiveHour)]
    [InlineData("5h", 300, QuotaKind.FiveHour)]
    [InlineData("monthly", null, QuotaKind.Other)]
    [InlineData("", null, QuotaKind.Other)]
    public void The_window_name_gives_the_length(string window, int? minutes, QuotaKind kind)
    {
        var json = AgySamples.With($$"""[{"name":"G","buckets":[{"window":"{{window}}","remaining_fraction":0.5,"reset_time":"2026-12-02T00:00:00Z"}]}]""");

        var parsed = AgyUsageParser.Parse(json).Single();

        Assert.Equal(minutes, parsed.DurationMinutes);
        Assert.Equal(kind, parsed.Kind);
    }

    [Fact]
    public void A_five_hour_and_a_weekly_bucket_in_one_group_are_both_kept()
    {
        var json = AgySamples.With("""[{"name":"Gemini Models","buckets":[{"id":"g-5h","window":"five_hour","remaining_fraction":0.9,"reset_time":"2026-11-25T09:00:00Z"},{"id":"g-weekly","window":"weekly","remaining_fraction":0.5,"reset_time":"2026-12-02T00:00:00Z"}]}]""");

        var windows = AgyUsageParser.Parse(json);

        Assert.Equal([QuotaKind.FiveHour, QuotaKind.Weekly], windows.Select(w => w.Kind));
        Assert.Equal([10.0, 50.0], windows.Select(w => Math.Round(w.PercentUsed, 6)));
    }

    [Fact]
    public void Missing_extra_and_odd_fields_are_tolerated()
    {
        var json = AgySamples.With("""
            [{"name":"A","extra":{"x":1},"buckets":[{"window":"weekly","remaining_fraction":0.4}, {"window":"weekly"}, {"remaining_fraction":"half"}, 7, {"window":"weekly","remaining_fraction":0.2,"reset_time":"not a time","unknown":[1,2]}]},
             {"buckets":[{"window":"weekly","remaining_fraction":0.1,"reset_time":"2026-12-02T00:00:00Z"}]},
             {"name":"C"}, 5]
            """);

        var windows = AgyUsageParser.Parse(json);

        Assert.Equal(3, windows.Count); // the bucket without a fraction, with a text fraction and the number are skipped
        Assert.False(windows[0].IsResetReal); // no reset time: a placeholder, never alerted on
        Assert.Null(windows[0].ResetsAt);
        Assert.False(windows[1].IsResetReal); // a time that does not parse
        Assert.Equal("Antigravity", windows[2].Bucket); // a group without a name
    }

    [Fact]
    public void Text_before_or_after_the_json_is_ignored()
    {
        var parsed = AgyUsageParser.Parse("log line\n" + AgySamples.Real + "\ntrailing\n");

        Assert.Equal(2, parsed.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ broken")]
    [InlineData("[]")]
    [InlineData("{}")] // an object without the command: not the usage report
    public void Bad_json_or_no_json_is_a_parse_failure_or_a_guard_failure_never_a_crash(string output)
    {
        var failure = Record.Exception(() => AgyUsageParser.Parse(output));

        Assert.True(failure is UsageParseException or AgyGuardException, failure?.GetType().Name ?? "no exception");
    }

    [Fact]
    public void An_answer_without_a_usable_bucket_is_a_parse_failure()
    {
        Assert.Throws<UsageParseException>(() => AgyUsageParser.Parse(AgySamples.With("[]")));
        Assert.Throws<UsageParseException>(() => AgyUsageParser.Parse(AgySamples.With("""[{"name":"A","buckets":[]}]""")));
        Assert.Throws<UsageParseException>(() => AgyUsageParser.Parse(AgySamples.With("""[{"name":"A","buckets":[{"window":"weekly"}]}]""")));
    }

    [Fact]
    public void The_guards_stop_an_answer_that_is_not_the_usage_command_or_reports_a_turn_or_tokens()
    {
        const string group = """[{"name":"A","buckets":[{"window":"weekly","remaining_fraction":0.5}]}]""";

        Assert.Throws<AgyGuardException>(() => AgyUsageParser.Parse(AgySamples.With(group, name: "model")));
        Assert.Throws<AgyGuardException>(() => AgyUsageParser.Parse(AgySamples.With(group, turns: 1)));
        Assert.Throws<AgyGuardException>(() => AgyUsageParser.Parse(AgySamples.With(group, tokens: 5)));
        // A missing field cannot be checked, so it counts as a failed guard too.
        Assert.Throws<AgyGuardException>(() => AgyUsageParser.Parse("""{"status":"SUCCESS","usage":{"total_tokens":0},"command":{"name":"usage","data":{"groups":[]}}}"""));
        Assert.Throws<AgyGuardException>(() => AgyUsageParser.Parse("""{"status":"SUCCESS","num_turns":0,"command":{"name":"usage","data":{"groups":[]}}}"""));
        Assert.Throws<AgyGuardException>(() => AgyUsageParser.Parse("""{"status":"SUCCESS","num_turns":0,"usage":{"total_tokens":0},"response":"hello"}"""));
    }

    [Fact]
    public void A_failed_run_is_reported_as_signed_out_or_as_another_error()
    {
        var signedOut = Assert.Throws<AgyReportedErrorException>(() => AgyUsageParser.Parse("""{"status":"ERROR","error":"authentication required"}"""));
        var other = Assert.Throws<AgyReportedErrorException>(() => AgyUsageParser.Parse("""{"status":"ERROR","error":{"message":"the backend is down"}}"""));

        Assert.True(signedOut.SignedOut);
        Assert.False(other.SignedOut);
        Assert.DoesNotContain("authentication", signedOut.Message); // the text of the error is never kept
    }
}

public sealed class AntigravityProviderTests
{
    private sealed class FakeRunner(Func<int, Task<AgyRunResult>> run) : IAgyRunner
    {
        public int Calls { get; private set; }

        public Task<AgyRunResult> RunUsageAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return run(Calls);
        }
    }

    private static AntigravityUsageProvider Provider(FakeRunner runner) => new(runner, new FakeClock());

    [Fact]
    public async Task A_good_answer_becomes_an_Ok_snapshot_with_no_account_and_no_plan()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new AgyRunResult(0, AgySamples.Real)));
        var provider = Provider(runner);

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal("antigravity", snapshot.Source.ProviderId);
        Assert.Equal("", snapshot.Source.AccountKey);
        Assert.Equal("", snapshot.Source.PlanTier);
        Assert.Equal(UsageDataSource.AntigravityCli, snapshot.DataSource);
        Assert.Equal("Antigravity", provider.DisplayName);
        Assert.Equal(TimeSpan.FromMinutes(5), provider.PollInterval);
    }

    [Fact]
    public async Task A_missing_program_is_not_installed()
    {
        var provider = Provider(new FakeRunner(_ => throw new FileNotFoundException("agy")));

        Assert.Equal(UsageState.NotInstalled, (await provider.ReadAsync(CancellationToken.None)).State);
    }

    [Fact]
    public async Task A_timeout_is_an_error_and_the_next_read_tries_again()
    {
        var runner = new FakeRunner(call => call == 1 ? throw new TimeoutException() : Task.FromResult(new AgyRunResult(0, AgySamples.Real)));
        var provider = Provider(runner);

        Assert.Equal(UsageState.Error, (await provider.ReadAsync(CancellationToken.None)).State);
        Assert.Equal(UsageState.Ok, (await provider.ReadAsync(CancellationToken.None)).State);
        Assert.False(provider.Halted);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 12)]
    public async Task A_guard_failure_is_unsupported_and_stops_all_further_reads(int turns, long tokens)
    {
        var json = AgySamples.With("""[{"name":"A","buckets":[{"window":"weekly","remaining_fraction":0.5}]}]""", turns: turns, tokens: tokens);
        var runner = new FakeRunner(_ => Task.FromResult(new AgyRunResult(0, json)));
        var provider = Provider(runner);

        var first = await provider.ReadAsync(CancellationToken.None);
        var second = await provider.ReadAsync(CancellationToken.None);
        var third = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Unsupported, first.State);
        Assert.Equal(UsageState.Unsupported, second.State);
        Assert.Equal(UsageState.Unsupported, third.State);
        Assert.Equal(1, runner.Calls); // agy was run once: a read that might spend quota is never repeated
        Assert.True(provider.Halted);
        Assert.Empty(first.Windows);
    }

    [Fact]
    public async Task Other_commands_are_unsupported_and_stop_reads_too()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new AgyRunResult(0, """{"status":"SUCCESS","num_turns":1,"usage":{"total_tokens":300},"response":"Hello","command":null}""")));
        var provider = Provider(runner);

        await provider.ReadAsync(CancellationToken.None);
        await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(1, runner.Calls);
        Assert.True(provider.Halted);
    }

    [Fact]
    public async Task A_shape_it_does_not_know_is_unsupported_but_does_not_stop_the_reads()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new AgyRunResult(0, AgySamples.With("[]"))));
        var provider = Provider(runner);

        var snapshot = await provider.ReadAsync(CancellationToken.None);
        await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Unsupported, snapshot.State);
        Assert.Equal(2, runner.Calls); // no model was used, so asking again is safe
        Assert.False(provider.Halted);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    public async Task Garbage_is_unsupported(string output)
    {
        var provider = Provider(new FakeRunner(_ => Task.FromResult(new AgyRunResult(0, output))));

        Assert.Equal(UsageState.Unsupported, (await provider.ReadAsync(CancellationToken.None)).State);
    }

    [Fact]
    public async Task A_signed_out_cli_is_not_logged_in_whether_it_says_so_in_json_or_in_text()
    {
        var json = Provider(new FakeRunner(_ => Task.FromResult(new AgyRunResult(1, """{"status":"ERROR","error":"authentication required"}"""))));
        var text = Provider(new FakeRunner(_ => Task.FromResult(new AgyRunResult(1, "Error: authentication required. Run agy to sign in."))));
        var other = Provider(new FakeRunner(_ => Task.FromResult(new AgyRunResult(1, """{"status":"ERROR","error":"backend down"}"""))));

        Assert.Equal(UsageState.NotLoggedIn, (await json.ReadAsync(CancellationToken.None)).State);
        Assert.Equal(UsageState.NotLoggedIn, (await text.ReadAsync(CancellationToken.None)).State);
        Assert.Equal(UsageState.Error, (await other.ReadAsync(CancellationToken.None)).State);
    }

    [Fact]
    public async Task Cancelling_a_read_cancels_it_and_reports_nothing()
    {
        using var cancel = new CancellationTokenSource();
        var runner = new FakeRunner(_ => throw new OperationCanceledException());
        cancel.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Provider(runner).ReadAsync(cancel.Token));
    }

    [Fact]
    public void Antigravity_is_off_until_the_user_turns_it_on_and_is_listed_last_by_default()
    {
        var settings = UsageSettings.Default;

        Assert.False(settings.IsEnabled(AntigravityUsageProvider.ProviderId));
        Assert.True(settings.WithEnabled(AntigravityUsageProvider.ProviderId, true).IsEnabled(AntigravityUsageProvider.ProviderId));
        Assert.DoesNotContain(AntigravityUsageProvider.ProviderId, settings.EnabledProviders);
    }

    [Fact]
    public void The_read_that_worked_is_cached_with_its_groups_and_nothing_else()
    {
        var path = Path.Combine(Path.GetTempPath(), "pq-agy-cache-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new UsageCacheStore(path);
            var snapshot = new UsageSnapshot(new UsageSource(AntigravityUsageProvider.ProviderId, UsageEnvironment.Windows, "", ""), AgyUsageParser.Parse(AgySamples.Real), AgySamples.Now, UsageDataSource.AntigravityCli, UsageState.Ok);

            store.Save([snapshot]);
            var loaded = store.Load().Single();

            Assert.Equal(UsageDataSource.AntigravityCli, loaded.DataSource);
            Assert.Equal(["Gemini Models", "Claude and GPT models"], loaded.Windows.Select(w => w.Bucket));
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("@", text); // no email
            Assert.DoesNotContain("response", text); // none of the CLI's own text
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class AntigravityAlertTests
{
    private static readonly IReadOnlyList<int> Levels = [80, 95];

    private static UsageSnapshot Groups(double geminiUsed, double otherUsed, int geminiResetMinutes = 48, int otherResetMinutes = 249, string geminiName = "Gemini Models") =>
        new(new UsageSource(AntigravityUsageProvider.ProviderId, UsageEnvironment.Windows, "", ""),
            [
                new QuotaWindow(QuotaKind.Weekly, 10080, geminiUsed, AgySamples.Now.AddDays(6).AddMinutes(geminiResetMinutes), geminiName, true),
                new QuotaWindow(QuotaKind.Weekly, 10080, otherUsed, AgySamples.Now.AddDays(6).AddMinutes(otherResetMinutes), "Claude and GPT models", true),
            ],
            AgySamples.Now, UsageDataSource.AntigravityCli, UsageState.Ok);

    private static int[] Reads(UsageAlertEngine engine, Func<int, UsageSnapshot> snapshot, int count) =>
        Enumerable.Range(0, count).Select(i => engine.Evaluate(snapshot(i), Levels).Alerts.Count).ToArray();

    [Fact]
    public void Two_groups_with_the_same_window_length_do_not_repeat_a_warning_on_unchanged_numbers()
    {
        var engine = new UsageAlertEngine();

        Assert.Equal([1, 0, 0, 0, 0, 0], Reads(engine, _ => Groups(90, 10), 6)); // one group is at 90%
    }

    [Fact]
    public void Two_groups_that_are_both_over_the_level_warn_once_each()
    {
        var engine = new UsageAlertEngine();

        Assert.Equal([2, 0, 0, 0, 0, 0], Reads(engine, _ => Groups(90, 90), 6));
    }

    [Fact]
    public void Each_group_re_arms_and_fires_on_its_own()
    {
        var engine = new UsageAlertEngine();
        UsageAlert[] Step(double gemini, double other) => engine.Evaluate(Groups(gemini, other), Levels).Alerts.ToArray();

        var first = Step(90, 90);
        Assert.Equal(2, first.Length);
        Assert.Empty(Step(90, 90));
        Assert.Empty(Step(50, 90)); // Gemini drops below 80%: only Gemini re-arms
        var again = Step(90, 90);
        Assert.Equal("Gemini Models", Assert.Single(again).Group); // Gemini fires again, Claude and GPT does not repeat
        Assert.Empty(Step(90, 90));
        var critical = Step(96, 90);
        Assert.Equal("Gemini Models", Assert.Single(critical).Group);
        Assert.Equal(95, critical[0].Threshold);
    }

    [Fact]
    public void A_group_that_resets_warns_again_for_its_new_window_and_the_other_group_is_unaffected()
    {
        var engine = new UsageAlertEngine();
        Assert.Equal(2, engine.Evaluate(Groups(90, 90), Levels).Alerts.Count);

        // Gemini's window ends and a new one starts (a new reset time); Claude and GPT keeps its window.
        var next = engine.Evaluate(Groups(85, 90, geminiResetMinutes: 48 + 10080), Levels).Alerts;

        Assert.Equal("Gemini Models", Assert.Single(next).Group);
    }

    [Fact]
    public void A_restart_does_not_repeat_the_warnings_that_were_shown()
    {
        var first = new UsageAlertEngine();
        first.Evaluate(Groups(90, 90), Levels);

        var restarted = new UsageAlertEngine(first.FiredKeys.ToList());

        Assert.Empty(restarted.Evaluate(Groups(90, 90), Levels).Alerts);
    }

    [Fact]
    public void The_alert_and_the_balloon_name_the_group()
    {
        var engine = new UsageAlertEngine();
        var snapshot = Groups(90, 10);
        var alert = Assert.Single(engine.Evaluate(snapshot, Levels).Alerts);

        var text = UsageController.AlertMessage(alert, snapshot, AgySamples.Now);

        Assert.Equal("Gemini Models", alert.Group);
        Assert.StartsWith("The weekly window of Gemini is 90% used (warning at 80%). It resets in 6d", text);
    }

    [Fact]
    public void The_balloon_picks_the_right_window_when_two_groups_reset_at_the_same_minute()
    {
        var snapshot = Groups(90, 91, geminiResetMinutes: 60, otherResetMinutes: 60);
        var engine = new UsageAlertEngine();

        var alerts = engine.Evaluate(snapshot, Levels).Alerts;

        Assert.Equal(["Gemini Models", "Claude and GPT models"], alerts.Select(a => a.Group));
        Assert.Equal(["of Gemini", "of Claude and GPT"], alerts.Select(a => UsageController.AlertMessage(a, snapshot, AgySamples.Now)).Select(m => m.Substring(m.IndexOf("of ", StringComparison.Ordinal), m.IndexOf(" is ", StringComparison.Ordinal) - m.IndexOf("of ", StringComparison.Ordinal))));
    }

    [Fact]
    public void Codex_keys_and_the_balloon_text_are_exactly_what_they_were()
    {
        var engine = new UsageAlertEngine();
        var snapshot = UsageTestData.Snapshot(10, 90, weekReset: UsageTestData.Now.AddDays(3));
        var alert = Assert.Single(engine.Evaluate(snapshot, Levels).Alerts);

        Assert.Equal("", alert.Group);
        var key = Assert.Single(engine.FiredKeys);
        Assert.Equal($"codex|Windows|abc123abc123|Weekly|80|{UsageTestData.Now.AddDays(3).ToUnixTimeSeconds() / 60}", key); // no group part
        Assert.Equal("The weekly window is 90% used (warning at 80%). It resets in 3d 0h.", UsageController.AlertMessage(alert, snapshot, UsageTestData.Now));
        Assert.Equal([1, 0, 0, 0, 0, 0], Reads(new UsageAlertEngine(), _ => snapshot, 6));
    }

    [Fact]
    public void A_failed_signed_out_run_that_names_the_usage_command_is_not_a_reason_to_stop()
    {
        var parse = Assert.Throws<AgyReportedErrorException>(() => AgyUsageParser.Parse("""{"status":"ERROR","error":"authentication required","command":{"name":"usage"}}"""));

        Assert.True(parse.SignedOut);
    }

    [Fact]
    public void A_failed_run_that_reports_a_turn_or_tokens_does_stop_the_reads()
    {
        Assert.Throws<AgyGuardException>(() => AgyUsageParser.Parse("""{"status":"ERROR","num_turns":1,"error":"boom"}"""));
        Assert.Throws<AgyGuardException>(() => AgyUsageParser.Parse("""{"status":"ERROR","usage":{"total_tokens":42},"error":"boom"}"""));
    }

    [Fact]
    public async Task The_provider_reports_a_signed_out_run_without_halting()
    {
        var runner = new CountingRunner("""{"status":"ERROR","error":"authentication required","command":{"name":"usage"}}""");
        var provider = new AntigravityUsageProvider(runner, new FakeClock());

        var first = await provider.ReadAsync(CancellationToken.None);
        await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NotLoggedIn, first.State);
        Assert.False(provider.Halted);
        Assert.Equal(2, runner.Calls);
    }

    private sealed class CountingRunner(string output) : IAgyRunner
    {
        public int Calls { get; private set; }

        public Task<AgyRunResult> RunUsageAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgyRunResult(1, output));
        }
    }
}
public sealed class ProcessAgyRunnerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pq-agy-runner-" + Guid.NewGuid().ToString("N"));

    public ProcessAgyRunnerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>A stand-in for agy: a .cmd file with the given body.</summary>
    private string Script(string body)
    {
        var path = Path.Combine(_folder, "agy-" + Guid.NewGuid().ToString("N") + ".cmd");
        File.WriteAllText(path, "@echo off\r\n" + body + "\r\n");
        return path;
    }

    [Fact]
    public async Task It_returns_the_output_and_the_exit_code_of_the_program()
    {
        var script = Script("echo {\"ok\":true}\r\nexit /b 3");

        var result = await new ProcessAgyRunner(() => script).RunUsageAsync(CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("{\"ok\":true}", result.Output);
    }

    [Fact]
    public async Task It_starts_the_program_with_exactly_the_usage_arguments_and_no_other_switch()
    {
        var script = Script("echo ARGS=%*\r\necho UPDATE=%AGY_CLI_DISABLE_AUTO_UPDATE%\r\necho CWD=%CD%");

        var result = await new ProcessAgyRunner(() => script).RunUsageAsync(CancellationToken.None);

        Assert.Contains("ARGS=-p /usage --output-format json --print-timeout 10s", result.Output);
        Assert.DoesNotContain("--disable-slash-commands", result.Output);
        Assert.DoesNotContain("--dangerously", result.Output);
        Assert.Contains("UPDATE=true", result.Output); // the CLI's own updater is off for this child
        var cwd = result.Output.Split('\n').First(l => l.StartsWith("CWD=")).Trim()[4..];
        Assert.StartsWith(Path.GetTempPath().TrimEnd('\\'), cwd, StringComparison.OrdinalIgnoreCase); // a private temporary folder, not the user's folder
        Assert.False(Directory.Exists(cwd), "the temporary working folder was left behind");
    }

    [Fact]
    public async Task A_program_that_hangs_is_killed_with_its_children_at_the_time_limit()
    {
        var script = Script("ping -n 41 127.0.0.1 > nul");
        var before = Process.GetProcessesByName("PING").Length;
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(() => new ProcessAgyRunner(() => script, TimeSpan.FromSeconds(1)).RunUsageAsync(CancellationToken.None));

        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"took {watch.Elapsed.TotalSeconds:0.0} s");
        await Task.Delay(500);
        Assert.True(Process.GetProcessesByName("PING").Length <= before, "the child ping process of the hung script is still running");
    }

    [Fact]
    public async Task Cancelling_the_read_kills_the_program_and_cancels()
    {
        var script = Script("ping -n 41 127.0.0.1 > nul");
        var before = Process.GetProcessesByName("PING").Length;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessAgyRunner(() => script, TimeSpan.FromSeconds(30)).RunUsageAsync(cancel.Token));

        await Task.Delay(500);
        Assert.True(Process.GetProcessesByName("PING").Length <= before);
    }

    [Fact]
    public async Task A_missing_program_is_reported_as_not_found()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => new ProcessAgyRunner(() => null).RunUsageAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_huge_answer_is_cut_at_one_mebibyte_and_does_not_block_the_program()
    {
        var script = Script("powershell -NoProfile -Command \"[Console]::Out.Write('x' * 3000000)\"");

        var result = await new ProcessAgyRunner(() => script, TimeSpan.FromSeconds(30)).RunUsageAsync(CancellationToken.None);

        Assert.InRange(result.Output.Length, 1024 * 1024, 1024 * 1024 + 16);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void The_program_is_found_on_the_path_first_and_then_in_the_installer_folder()
    {
        var onPath = AgyLocator.FindExecutable(path => path == @"C:\tools\agy.exe", [@"C:\other", @"C:\tools"], @"C:\Users\x\AppData\Local");
        var cmd = AgyLocator.FindExecutable(path => path == @"C:\tools\agy.cmd", [@"C:\tools"], @"C:\Users\x\AppData\Local");
        var installed = AgyLocator.FindExecutable(path => path == @"C:\Users\x\AppData\Local\agy\bin\agy.exe", [@"C:\other"], @"C:\Users\x\AppData\Local");
        var none = AgyLocator.FindExecutable(_ => false, [@"C:\other", "bad|path"], @"C:\Users\x\AppData\Local");

        Assert.Equal(@"C:\tools\agy.exe", onPath);
        Assert.Equal(@"C:\tools\agy.cmd", cmd);
        Assert.Equal(@"C:\Users\x\AppData\Local\agy\bin\agy.exe", installed);
        Assert.Null(none);
    }

    [Fact]
    public async Task The_detector_reads_the_version_and_reports_a_missing_program()
    {
        var found = await new AgyDetector(new FakeCommandRunner("1.5.16\n"), () => @"C:\tools\agy.exe").DetectAsync(CancellationToken.None);
        var missing = await new AgyDetector(new FakeCommandRunner(""), () => null).DetectAsync(CancellationToken.None);

        Assert.True(found.Installed);
        Assert.Equal("1.5.16", found.Version);
        Assert.False(missing.Installed);
        Assert.Contains("not found", missing.Detail);
    }

    private sealed class FakeCommandRunner(string output) : ICommandRunner
    {
        public Task<(int ExitCode, string Output)> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Assert.Equal(["--version"], arguments); // the detector only ever asks for the version
            return Task.FromResult((0, output));
        }
    }

    [Fact]
    public void The_usage_arguments_are_exactly_the_documented_ones_and_the_source_uses_no_other_switch()
    {
        Assert.Equal(["-p", "/usage", "--output-format", "json", "--print-timeout", "10s"], ProcessAgyRunner.UsageArguments);
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "PomoQuote.csproj"))) root = Path.GetDirectoryName(root);
        var source = string.Join('\n', Directory.GetFiles(Path.Combine(root!, "AiUsage", "Antigravity"), "*.cs").Select(File.ReadAllText));

        Assert.DoesNotContain("\"--disable-slash-commands\"", source);
        Assert.DoesNotContain("\"--dangerously", source);
        Assert.DoesNotContain("\"--model\"", source);
        Assert.DoesNotContain("CommandFor(executable, \"login\"", source);
        Assert.DoesNotContain("\"logout\"", source);
        Assert.DoesNotContain("language_server", source, StringComparison.OrdinalIgnoreCase); // the vendor's local server is never used
        Assert.DoesNotContain("csrf", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cloudcode", source, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class AntigravityViewTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static UsageStatus Status(string json, DateTimeOffset? observed = null)
    {
        var snapshot = new UsageSnapshot(new UsageSource(AntigravityUsageProvider.ProviderId, UsageEnvironment.Windows, "", ""), AgyUsageParser.Parse(json), observed ?? AgySamples.Now, UsageDataSource.AntigravityCli, UsageState.Ok);
        return new UsageStatus(AntigravityUsageProvider.ProviderId, "Antigravity", snapshot, snapshot);
    }

    [Fact]
    public void Two_groups_become_one_Week_row_for_the_group_that_is_most_used_with_the_detail_in_the_tips_and_the_source_line()
    {
        var view = UsageViewBuilder.Build(Status(AgySamples.Real), UsageSettings.Default, AgySamples.Now, Utc);

        var row = Assert.Single(view.Rows);
        Assert.Equal("Week", row.Label);
        Assert.Equal("33%", row.Percent);
        Assert.Equal(0.33, row.Fraction, 2);
        Assert.Contains("Gemini (the most used)", row.Tip);
        Assert.Contains("Claude and GPT: used 0%", row.Tip);
        Assert.Equal("Windows · Gemini 33%, Claude and GPT 0%", view.Source); // the Expanded source line
        Assert.Contains("Gemini 33%, Claude and GPT 0%", view.Tip);
        Assert.True(view.HasData);
    }

    [Fact]
    public void A_five_hour_and_a_weekly_window_per_group_give_a_5h_row_and_a_Week_row_each_for_its_most_used_group()
    {
        var json = AgySamples.With("""
            [{"name":"Gemini Models","buckets":[{"window":"five_hour","remaining_fraction":0.9,"reset_time":"2026-11-25T09:00:00Z"},{"window":"weekly","remaining_fraction":0.5,"reset_time":"2026-12-02T00:00:00Z"}]},
             {"name":"Claude and GPT models","buckets":[{"window":"five_hour","remaining_fraction":0.6,"reset_time":"2026-11-25T08:00:00Z"},{"window":"weekly","remaining_fraction":0.8,"reset_time":"2026-12-02T01:00:00Z"}]}]
            """);

        var view = UsageViewBuilder.Build(Status(json), UsageSettings.Default, AgySamples.Now, Utc);

        Assert.Equal(["5h", "Week"], view.Rows.Select(r => r.Label));
        Assert.Equal(["40%", "50%"], view.Rows.Select(r => r.Percent)); // Claude and GPT for 5h, Gemini for the week
        Assert.Contains("Claude and GPT (the most used)", view.Rows[0].Tip);
        Assert.Contains("Gemini (the most used)", view.Rows[1].Tip);
    }

    [Fact]
    public void A_single_group_has_no_group_detail()
    {
        var json = AgySamples.With("""[{"name":"Gemini Models","buckets":[{"window":"weekly","remaining_fraction":0.5,"reset_time":"2026-12-02T00:00:00Z"}]}]""");

        var view = UsageViewBuilder.Build(Status(json), UsageSettings.Default, AgySamples.Now, Utc);

        Assert.Equal("Windows", view.Source);
        Assert.Single(view.Rows);
    }

    [Fact]
    public void The_warning_levels_apply_to_the_most_used_group()
    {
        var json = AgySamples.With("""[{"name":"A","buckets":[{"window":"weekly","remaining_fraction":0.5,"reset_time":"2026-12-02T00:00:00Z"}]},{"name":"B","buckets":[{"window":"weekly","remaining_fraction":0.03,"reset_time":"2026-12-02T00:00:00Z"}]}]""");

        var view = UsageViewBuilder.Build(Status(json), UsageSettings.Default, AgySamples.Now, Utc);

        Assert.Equal("97%", view.Rows.Single().Percent);
        Assert.Equal(UsageLevel.Critical, view.Rows.Single().Level);
        Assert.Equal(ProviderSeverity.NearLimit, view.Severity);
    }

    [Fact]
    public void Numbers_read_every_five_minutes_are_not_stale_even_when_the_setting_is_one_minute()
    {
        var settings = UsageSettings.Default with { IntervalMinutes = 1 };

        var fresh = UsageViewBuilder.Build(Status(AgySamples.Real), settings, AgySamples.Now.AddMinutes(9), Utc);
        var old = UsageViewBuilder.Build(Status(AgySamples.Real), settings, AgySamples.Now.AddMinutes(11), Utc);

        Assert.StartsWith("updated", fresh.Note); // 9 minutes is under twice the 5-minute reading
        Assert.StartsWith("stale", old.Note);
        Assert.Equal(ProviderSeverity.Stale, old.Severity);
    }

    [Fact]
    public void Codex_and_Claude_rows_are_unchanged_by_the_grouping()
    {
        var snapshot = new UsageSnapshot(UsageTestData.Source(), [UsageTestData.Window(QuotaKind.Weekly, 10, UsageTestData.Now.AddDays(1)), UsageTestData.Window(QuotaKind.Weekly, 20, UsageTestData.Now.AddDays(2))], UsageTestData.Now, UsageDataSource.CodexAppServer, UsageState.Ok);

        var view = UsageViewBuilder.Build(new UsageStatus("codex", "Codex", snapshot, snapshot), UsageSettings.Default, UsageTestData.Now, Utc);

        Assert.Equal(2, view.Rows.Count); // both rows still shown
        Assert.Equal("Windows", view.Source);
    }

    [Fact]
    public void The_long_source_line_is_cut_by_the_existing_note_fitter_keeping_the_time()
    {
        var view = UsageViewBuilder.Build(Status(AgySamples.Real), UsageSettings.Default, AgySamples.Now, Utc);
        var line = $"{view.Source}  ·  {view.Note}";

        var fitted = UsageViewBuilder.FitNote(view with { Note = line, ShortNote = $"{view.Source}  ·  {view.Short}" }, 150f, s => s.Length * 5f);

        Assert.True(fitted.Length * 5f <= 150f);
        Assert.EndsWith("06:00", fitted); // the time of the data is always kept
    }
}

/// <summary>The real widget with an Antigravity provider: the rows draw at 280 px in both layouts without clipping, and many layout switches do not leak.</summary>
[Collection("process")]
public sealed class AntigravityWidgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteAgy-" + Guid.NewGuid().ToString("N"));

    public AntigravityWidgetTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    private sealed class SampleRunner : IAgyRunner
    {
        public Task<AgyRunResult> RunUsageAsync(CancellationToken cancellationToken) => Task.FromResult(new AgyRunResult(0, AgySamples.Real.Replace("2026-12-02", DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"))));
    }

    private MainForm NewForm()
    {
        var usagePath = Path.Combine(_root, "usage.json");
        new UsageSettingsStore(usagePath).Save(UsageSettings.Default.WithEnabled(AntigravityUsageProvider.ProviderId, true) with { AlertsEnabled = false, SelectedProvider = AntigravityUsageProvider.ProviderId, Expanded = false });
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        var form = new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            new QuoteSettingsStore(Path.Combine(_root, "quotes.json")), new BundledQuoteProvider(), new Random(1),
            new UsageSettingsStore(usagePath), new UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            [new AntigravityUsageProvider(new SampleRunner(), TimeProvider.System)], null, new WeatherEffectSettingsStore(Path.Combine(_root, "weather-effects.json")))
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000),
            WorkAreaProvider = () => [new Rectangle(-6000, -6000, 4000, 4000)],
        };
        form.SetTheme(ThemeMode.Dark); // these tests measure the dark panel; the theme tests cover the others
        return form;
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

    private static MainForm WaitForRows(MainForm form)
    {
        form.Show();
        for (var i = 0; i < 200 && !form.UsageForTests.Views().Any(v => v.Rows.Count > 0); i++) Pump(TimeSpan.FromMilliseconds(50));
        Assert.Contains(form.UsageForTests.Views(), v => v.Id == AntigravityUsageProvider.ProviderId && v.Rows.Count == 1);
        return form;
    }

    private static Bitmap Draw(MainForm form)
    {
        var view = form.WidgetViewForTests;
        var bitmap = new Bitmap(view.ClientSize.Width, view.ClientSize.Height);
        view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, view.ClientSize));
        return bitmap;
    }

    /// <summary>True when the strip of the given logical x range, over the usage area, holds only the panel colour (no text or bar crossed the margin).</summary>
    private static bool MarginIsEmpty(Bitmap bitmap, float unit, int fromY, int toY, int fromLogicalX, int toLogicalX)
    {
        for (var y = fromY; y < Math.Min(toY, bitmap.Height - 2); y += 2)
        {
            for (var x = (int)(fromLogicalX * unit); x < (int)(toLogicalX * unit); x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (Math.Abs(c.R - 28) > 6 || Math.Abs(c.G - 32) > 6 || Math.Abs(c.B - 40) > 6) return false;
            }
        }

        return true;
    }

    [Fact]
    public void Both_layouts_draw_the_provider_at_280_px_without_text_in_the_margins_at_every_display_scale()
    {
        OnStaThread(() =>
        {
            using var form = WaitForRows(NewForm());
            form.SetWidgetMode(true);
            var view = form.WidgetViewForTests;
            foreach (var expanded in new[] { false, true })
            {
                form.UsageForTests.SetLayout(expanded, AntigravityUsageProvider.ProviderId);
                foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
                {
                    form.ClientSize = new Size((int)Math.Round(280 * scale), (int)Math.Round(view.LogicalHeight * scale));
                    Pump(TimeSpan.FromMilliseconds(80));
                    using var bitmap = Draw(form);
                    var unit = view.ClientSize.Width / 280f;
                    var top = (int)(view.UsageTop * unit);

                    Assert.Equal(280, (int)Math.Round(view.ClientSize.Width / unit));
                    Assert.True(MarginIsEmpty(bitmap, unit, top, view.ClientSize.Height, 2, 11), $"text in the left margin (expanded {expanded}, scale {scale})");
                    Assert.True(MarginIsEmpty(bitmap, unit, top, view.ClientSize.Height, 270, 278), $"text in the right margin (expanded {expanded}, scale {scale})");
                }
            }
        });
    }

    [Fact]
    public void A_hundred_layout_switches_and_reads_do_not_leak_gdi_objects_or_handles()
    {
        OnStaThread(() =>
        {
            using var form = WaitForRows(NewForm());
            form.SetWidgetMode(true);
            var process = Process.GetCurrentProcess();

            (int Handles, uint Gdi, uint User) Settled()
            {
                (int, uint, uint) last = (-1, 0, 0);
                for (var i = 0; i < 20; i++)
                {
                    Pump(TimeSpan.FromMilliseconds(150));
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    process.Refresh();
                    var now = (process.HandleCount, GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1));
                    if (now == last) break;
                    last = now;
                }

                return last;
            }

            void Cycle()
            {
                form.UsageForTests.SetLayout(true, AntigravityUsageProvider.ProviderId);
                Pump(TimeSpan.FromMilliseconds(20));
                using (Draw(form)) { }
                form.UsageForTests.SetLayout(false, AntigravityUsageProvider.ProviderId);
                form.UsageForTests.RefreshProvider(AntigravityUsageProvider.ProviderId);
                Pump(TimeSpan.FromMilliseconds(20));
                using (Draw(form)) { }
            }

            for (var i = 0; i < 10; i++) Cycle();
            var (handles, gdi, user) = Settled();

            for (var i = 0; i < 100; i++) Cycle();
            var (handlesAfter, gdiAfter, userAfter) = Settled();

            Assert.True(handlesAfter - handles <= 60, $"handles {handles} -> {handlesAfter}");
            Assert.True((long)gdiAfter - gdi <= 12, $"GDI objects {gdi} -> {gdiAfter}");
            Assert.True((long)userAfter - user <= 12, $"USER objects {user} -> {userAfter}");
        });
    }

    [Fact]
    public void The_settings_dialog_has_an_Antigravity_row_with_its_own_test_button_and_no_place_to_type_a_secret()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory.Split("PomoQuote.Tests")[0], "UsageSettingsDialog.cs"));

        Assert.Contains("Show Antigravity usage on the widget", source);
        Assert.Contains("_agyTest", source);
        Assert.Contains("AntigravityUsageProvider.ProviderId", source);
        Assert.DoesNotContain("PasswordChar", source);
    }
}
