using System.ComponentModel;
using System.Text.Json;
using PomoQuote.AiUsage;

namespace PomoQuote.Tests;

public sealed class CodexRateLimitsParserTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void The_verified_shape_gives_five_hour_and_weekly_windows_with_plan_and_hashed_account()
    {
        var limits = CodexRateLimitsParser.Parse(Parse(CodexSamples.RateLimitsResult));

        Assert.Equal("plus", limits.PlanTier);
        Assert.Equal(2, limits.Windows.Count);
        var five = limits.Windows[0];
        Assert.Equal(QuotaKind.FiveHour, five.Kind);
        Assert.Equal(300, five.DurationMinutes);
        Assert.Equal(21, five.PercentUsed);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795510747), five.ResetsAt);
        Assert.True(five.IsResetReal);
        Assert.Equal("codex", five.Bucket);
        Assert.Equal(QuotaKind.Weekly, limits.Windows[1].Kind);
        Assert.Equal(27, limits.Windows[1].PercentUsed);
        Assert.NotEmpty(limits.AccountKey);
        Assert.DoesNotContain("acct", limits.AccountKey);
    }

    [Fact]
    public void The_account_key_is_stable_short_and_not_the_id()
    {
        var a = AccountKey.From("account-one");

        Assert.Equal(a, AccountKey.From("account-one"));
        Assert.NotEqual(a, AccountKey.From("account-two"));
        Assert.Equal(12, a.Length);
        Assert.DoesNotContain("account", a);
        Assert.Equal("", AccountKey.From(null));
        Assert.Equal("", AccountKey.From("  "));
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        var json = """{"rateLimits":{"primary":{"usedPercent":5,"windowDurationMins":300,"resetsAt":1795510747,"future":{"a":1}},"brandNew":[1,2]},"somethingElse":true}""";

        var limits = CodexRateLimitsParser.Parse(Parse(json));

        Assert.Single(limits.Windows);
        Assert.Equal("", limits.PlanTier);
        Assert.Equal("", limits.AccountKey);
    }

    [Fact]
    public void A_missing_or_null_window_is_left_out_and_the_other_still_shows()
    {
        var json = """{"rateLimits":{"primary":null,"secondary":{"usedPercent":40,"windowDurationMins":10080,"resetsAt":1795771509}}}""";

        var limits = CodexRateLimitsParser.Parse(Parse(json));

        var window = Assert.Single(limits.Windows);
        Assert.Equal(QuotaKind.Weekly, window.Kind);
    }

    [Fact]
    public void A_missing_or_null_reset_time_means_the_reset_is_not_real_and_the_window_is_kept()
    {
        var json = """{"rateLimits":{"primary":{"usedPercent":50,"windowDurationMins":300,"resetsAt":null},"secondary":{"usedPercent":10,"windowDurationMins":10080}}}""";

        var limits = CodexRateLimitsParser.Parse(Parse(json));

        Assert.All(limits.Windows, w => { Assert.Null(w.ResetsAt); Assert.False(w.IsResetReal); });
    }

    [Fact]
    public void An_unknown_or_missing_duration_is_kind_other()
    {
        var json = """{"rateLimits":{"primary":{"usedPercent":50,"windowDurationMins":null,"resetsAt":1795510747},"secondary":{"usedPercent":10,"windowDurationMins":123,"resetsAt":1795510747}}}""";

        var limits = CodexRateLimitsParser.Parse(Parse(json));

        Assert.All(limits.Windows, w => Assert.Equal(QuotaKind.Other, w.Kind));
        Assert.Null(limits.Windows[0].DurationMinutes);
        Assert.Equal(123, limits.Windows[1].DurationMinutes);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(250, 100)]
    [InlineData(37.5, 37.5)]
    public void The_percentage_is_kept_between_0_and_100(double given, double expected)
    {
        var json = """{"rateLimits":{"primary":{"usedPercent":""" + given.ToString(System.Globalization.CultureInfo.InvariantCulture) + ""","windowDurationMins":300,"resetsAt":1795510747}}}""";

        Assert.Equal(expected, CodexRateLimitsParser.Parse(Parse(json)).Windows[0].PercentUsed);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"x":1}""")]
    [InlineData("""{"rateLimits":null}""")]
    [InlineData("""{"rateLimits":{}}""")]
    [InlineData("""{"rateLimits":{"primary":null,"secondary":null}}""")]
    [InlineData("""{"rateLimits":{"primary":{"windowDurationMins":300}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":"five"}}}""")]
    public void An_answer_without_a_usable_window_is_rejected(string json)
    {
        Assert.Throws<UsageParseException>(() => CodexRateLimitsParser.Parse(Parse(json)));
    }

    [Fact]
    public void A_reset_time_out_of_range_is_treated_as_unknown()
    {
        var json = """{"rateLimits":{"primary":{"usedPercent":5,"windowDurationMins":300,"resetsAt":99999999999999}}}""";

        var window = CodexRateLimitsParser.Parse(Parse(json)).Windows[0];

        Assert.Null(window.ResetsAt);
    }
}

public sealed class CodexUsageProviderTests
{
    private static CodexUsageProvider Provider(IJsonLineSessionFactory factory, TimeSpan? timeout = null) =>
        new(factory, TimeProvider.System, timeout ?? TimeSpan.FromSeconds(5));

    [Fact]
    public async Task A_healthy_server_gives_an_ok_snapshot_and_the_messages_follow_the_protocol()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line => CodexSamples.Healthy(line)));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal("plus", snapshot.Source.PlanTier);
        Assert.Equal("codex", snapshot.Source.ProviderId);
        Assert.Equal(UsageDataSource.CodexAppServer, snapshot.DataSource);

        var sent = factory.Last!.Sent;
        Assert.Equal(3, sent.Count);
        Assert.Contains("\"method\":\"initialize\"", sent[0]);
        Assert.Contains("clientInfo", sent[0]);
        Assert.Equal("""{"method":"initialized"}""", sent[1]);
        Assert.Contains("\"method\":\"account/rateLimits/read\"", sent[2]);
        Assert.True(factory.Last.Disposed);
    }

    [Fact]
    public async Task Notifications_log_lines_and_other_ids_before_the_reply_are_skipped()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line => CodexSamples.Healthy(line)));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State); // the healthy fake sends noise before each reply
    }

    [Fact]
    public async Task A_missing_executable_is_not_installed_and_nothing_throws()
    {
        var snapshot = await Provider(new ThrowingFactory(new FileNotFoundException())).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NotInstalled, snapshot.State);
        Assert.Empty(snapshot.Windows);
        Assert.NotEmpty(snapshot.ErrorText);
    }

    [Fact]
    public async Task A_process_that_cannot_start_is_not_installed()
    {
        var snapshot = await Provider(new ThrowingFactory(new Win32Exception("blocked"))).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NotInstalled, snapshot.State);
    }

    [Theory]
    [InlineData("authentication required")]
    [InlineData("You are not logged in")]
    [InlineData("please sign in again")]
    public async Task A_signed_out_error_gives_not_logged_in_without_keeping_the_message(string message)
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line =>
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("id", out var id)) return [];
            return id.GetInt32() == 1
                ? [CodexSamples.Reply(1, "{}")]
                : [@"{""id"":2,""error"":{""code"":-32000,""message"":""" + message + @" for someone@example.com""}}"];
        }));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NotLoggedIn, snapshot.State);
        Assert.DoesNotContain("someone", snapshot.ErrorText);
        Assert.Contains("codex login", snapshot.ErrorText);
    }

    [Fact]
    public async Task Another_error_from_the_server_gives_error_with_a_generic_text()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line =>
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("id", out var id)) return [];
            return id.GetInt32() == 1 ? [CodexSamples.Reply(1, "{}")] : ["""{"id":2,"error":{"code":500,"message":"boom with details"}}"""];
        }));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.DoesNotContain("details", snapshot.ErrorText);
    }

    [Theory]
    [InlineData("""{"somethingNew":true}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":"x"}}}""")]
    public async Task An_answer_in_an_unknown_shape_is_unsupported(string result)
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line => CodexSamples.Healthy(line, result)));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Unsupported, snapshot.State);
        Assert.Contains("unsupported", snapshot.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_refused_handshake_is_unsupported()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line => ["""{"id":1,"error":{"code":-1,"message":"nope"}}"""]));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Unsupported, snapshot.State);
    }

    [Fact]
    public async Task A_server_that_never_answers_times_out_and_the_session_is_disposed()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(_ => [], hangWhenEmpty: true));

        var snapshot = await Provider(factory, TimeSpan.FromMilliseconds(300)).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.Contains("did not answer", snapshot.ErrorText);
        Assert.True(factory.Last!.Disposed);
    }

    [Fact]
    public async Task A_server_that_stops_before_the_reply_is_an_error()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line => line.Contains("\"id\":1") ? [CodexSamples.Reply(1, "{}")] : []));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.Contains("stopped", snapshot.ErrorText);
    }

    [Fact]
    public async Task A_server_that_stops_at_once_is_an_error()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(_ => []));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
    }

    [Fact]
    public async Task Cancelling_the_read_propagates_and_still_disposes_the_session()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(_ => [], hangWhenEmpty: true));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(150);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(factory, TimeSpan.FromSeconds(30)).ReadAsync(cts.Token));

        Assert.True(factory.Last!.Disposed);
    }

    [Fact]
    public async Task The_snapshot_never_contains_the_raw_account_id()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line => CodexSamples.Healthy(line)));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        var text = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("acct-0000-test", text);
    }
}

public sealed class CodexDetectorTests
{
    private sealed class FakeRunner(Func<string, IReadOnlyList<string>, (int, string)> answer) : ICommandRunner
    {
        public List<string> Calls { get; } = [];

        public Task<(int ExitCode, string Output)> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add(string.Join(' ', arguments));
            return Task.FromResult(answer(fileName, arguments));
        }
    }

    [Fact]
    public async Task Not_found_means_not_installed()
    {
        var info = await new CodexDetector(new FakeRunner((_, _) => (0, "")), () => null).DetectAsync(CancellationToken.None);

        Assert.False(info.Installed);
        Assert.Null(info.LoggedIn);
    }

    [Fact]
    public async Task Version_and_login_state_are_read_only_commands()
    {
        var runner = new FakeRunner((_, args) => args.Contains("--version") ? (0, "codex-cli 0.171.0\n") : (0, "Logged in using ChatGPT\n"));

        var info = await new CodexDetector(runner, () => @"C:\x\codex.exe").DetectAsync(CancellationToken.None);

        Assert.True(info.Installed);
        Assert.Equal("0.171.0", info.Version);
        Assert.True(info.LoggedIn);
        Assert.Equal(["--version", "login status"], runner.Calls);
        Assert.DoesNotContain(runner.Calls, call => call.Contains("logout") || call.StartsWith("login") && !call.Contains("status"));
    }

    [Fact]
    public async Task Not_logged_in_is_reported_with_the_terminal_hint_and_no_token_request()
    {
        var runner = new FakeRunner((_, args) => args.Contains("--version") ? (0, "codex-cli 0.171.0") : (1, "Not logged in"));

        var info = await new CodexDetector(runner, () => @"C:\x\codex.exe").DetectAsync(CancellationToken.None);

        Assert.False(info.LoggedIn);
        Assert.Contains("codex login", info.Detail);
        Assert.DoesNotContain("paste", info.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_runner_that_fails_gives_unknown_login_state()
    {
        var runner = new FakeRunner((_, _) => throw new Win32Exception("x"));

        var info = await new CodexDetector(runner, () => @"C:\x\codex.exe").DetectAsync(CancellationToken.None);

        Assert.True(info.Installed);
        Assert.Null(info.LoggedIn);
    }

    [Theory]
    [InlineData("codex-cli 0.171.0", "0.171.0")]
    [InlineData("codex 1.2.3.", "1.2.3")]
    [InlineData("nothing", "")]
    public void Versions_are_picked_out_of_the_text(string text, string expected) => Assert.Equal(expected, CodexDetector.ParseVersion(text));

    [Theory]
    [InlineData("Logged in using ChatGPT", true)]
    [InlineData("Not logged in", false)]
    [InlineData("not LOGGED IN.", false)]
    [InlineData("weird", null)]
    public void Login_text_is_understood(string text, bool? expected) => Assert.Equal(expected, CodexDetector.ParseLoggedIn(text));

    [Fact]
    public void The_locator_prefers_exe_on_path_then_cmd_then_the_desktop_app()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\b\codex.cmd", @"C:\L\Programs\OpenAI\Codex\bin\codex.exe" };
        string? Find(params string[] dirs) => CodexLocator.FindExecutable(files.Contains, dirs, @"C:\L");

        Assert.Equal(@"C:\b\codex.cmd", Find(@"C:\a", @"C:\b"));
        files.Add(@"C:\a\codex.exe");
        Assert.Equal(@"C:\a\codex.exe", Find(@"C:\a", @"C:\b"));
        Assert.Equal(@"C:\L\Programs\OpenAI\Codex\bin\codex.exe", Find(@"C:\none"));
        files.Clear();
        Assert.Null(Find(@"C:\none"));
    }

    [Fact]
    public void A_cmd_shim_is_run_through_cmd_exe()
    {
        var (file, args) = CodexLocator.CommandFor(@"C:\n\codex.cmd", "app-server");
        var (file2, args2) = CodexLocator.CommandFor(@"C:\n\codex.exe", "app-server");

        Assert.Equal("cmd.exe", file);
        Assert.Equal(["/c", @"C:\n\codex.cmd", "app-server"], args);
        Assert.Equal(@"C:\n\codex.exe", file2);
        Assert.Equal(["app-server"], args2);
    }
}
