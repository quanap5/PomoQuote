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

/// <summary>The shape the real `grok agent stdio` billing request answered on a real PC (names and shape only; the numbers are made up), and variations.</summary>
internal static class GrokSamples
{
    public static readonly DateTimeOffset Now = new(2026, 11, 25, 6, 0, 0, TimeSpan.Zero);

    public const string Real = """
        {"config":{"creditUsagePercent":42.5,"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY","start":"2026-11-24T15:17:24.730+00:00","end":"2026-12-01T15:17:24.730+00:00"},
          "prepaidBalance":{"val":"0"},"isUnifiedBillingUser":true,"billingPeriodStart":"2026-11-24T15:17:24.730+00:00","billingPeriodEnd":"2026-12-01T15:17:24.730+00:00"},
         "subscription_tier":"SuperGrok"}
        """;

    public static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    public static string Reply(int id, string resultJson) => $$"""{"jsonrpc":"2.0","id":{{id}},"result":{{resultJson}}}""";

    public static string Error(int id, int code, string message) => $$$"""{"jsonrpc":"2.0","id":{{{id}}},"error":{"code":{{{code}}},"message":"{{{message}}}"}}""";

    public static (int Id, string Method) Parse(string line)
    {
        using var doc = JsonDocument.Parse(line);
        return (doc.RootElement.GetProperty("id").GetInt32(), doc.RootElement.GetProperty("method").GetString()!);
    }

    /// <summary>A server that answers initialize, then the billing request, with notifications and log noise in between.</summary>
    public static string[] Healthy(string line, string resultJson = Real)
    {
        var (id, method) = Parse(line);
        return method == "initialize"
            ? ["starting up...", Reply(id, """{"protocolVersion":1,"agentCapabilities":{},"authMethods":[]}""")]
            : ["""{"jsonrpc":"2.0","method":"_x.ai/mcp/servers_updated","params":{}}""", "not json at all", Reply(id, resultJson)];
    }

    /// <summary>A server that answers initialize and then the billing request with the given function.</summary>
    public static Func<string, string[]> Billing(Func<int, string, string> answer) => line =>
    {
        var (id, method) = Parse(line);
        return method == "initialize" ? [Reply(id, "{}")] : [answer(id, method)];
    };
}

public sealed class GrokBillingParserTests
{
    [Fact]
    public void The_real_shape_gives_one_weekly_window_with_the_plan_and_the_period_end_as_reset()
    {
        var billing = GrokBillingParser.Parse(GrokSamples.Json(GrokSamples.Real));

        Assert.Equal("SuperGrok", billing.PlanTier);
        var window = Assert.Single(billing.Windows);
        Assert.Equal(QuotaKind.Weekly, window.Kind);
        Assert.Equal(10080, window.DurationMinutes);
        Assert.Equal(42.5, window.PercentUsed);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 15, 17, 24, 730, TimeSpan.Zero), window.ResetsAt);
        Assert.True(window.IsResetReal);
        Assert.Equal("", window.Bucket);
    }

    [Fact]
    public void A_monthly_period_takes_its_real_length_from_its_start_and_end_and_is_not_called_weekly()
    {
        var json = GrokSamples.Real.Replace("USAGE_PERIOD_TYPE_WEEKLY", "USAGE_PERIOD_TYPE_MONTHLY").Replace("\"end\":\"2026-12-01T15:17:24.730+00:00\"", "\"end\":\"2026-12-24T15:17:24.730+00:00\"");

        var window = Assert.Single(GrokBillingParser.Parse(GrokSamples.Json(json)).Windows);

        Assert.Equal(QuotaKind.Other, window.Kind);
        Assert.Equal(30 * 1440, window.DurationMinutes);
        Assert.Equal("30d", UsageViewBuilder.Label(window));
    }

    [Theory]
    [InlineData("150", 100)]
    [InlineData("-5", 0)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    public void The_percent_is_kept_between_0_and_100(string value, double expected)
    {
        var json = GrokSamples.Real.Replace("42.5", value);

        Assert.Equal(expected, GrokBillingParser.Parse(GrokSamples.Json(json)).Windows[0].PercentUsed);
    }

    [Fact]
    public void Unknown_and_extra_fields_and_a_missing_plan_are_tolerated()
    {
        var json = """{"config":{"creditUsagePercent":10,"futureField":{"a":[1,2]},"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY","end":"2026-12-01T00:00:00Z","extra":1}},"unknown":true}""";

        var billing = GrokBillingParser.Parse(GrokSamples.Json(json));

        Assert.Equal("", billing.PlanTier);
        Assert.Equal(10, billing.Windows[0].PercentUsed);
        Assert.Equal(10080, billing.Windows[0].DurationMinutes);
    }

    [Fact]
    public void A_period_without_an_end_gives_a_window_without_a_real_reset_so_it_never_alerts()
    {
        var json = """{"config":{"creditUsagePercent":90,"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY"}}}""";

        var window = Assert.Single(GrokBillingParser.Parse(GrokSamples.Json(json)).Windows);

        Assert.Null(window.ResetsAt);
        Assert.False(window.IsResetReal);
        var snapshot = new UsageSnapshot(new UsageSource("grok", UsageEnvironment.Windows, "", ""), [window], GrokSamples.Now, UsageDataSource.GrokAcp, UsageState.Ok);
        Assert.Empty(new UsageAlertEngine().Evaluate(snapshot, [80, 95]).Alerts);
    }

    [Fact]
    public void The_deprecated_used_and_limit_fields_are_used_only_when_there_is_no_percent()
    {
        var json = """{"config":{"monthlyLimit":{"val":10000},"used":{"val":2500},"billingPeriodStart":"2026-11-01T00:00:00Z","billingPeriodEnd":"2026-12-01T00:00:00Z"}}""";

        var window = Assert.Single(GrokBillingParser.Parse(GrokSamples.Json(json)).Windows);

        Assert.Equal(25, window.PercentUsed);
        Assert.Equal(30 * 1440, window.DurationMinutes);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    [InlineData("{}")]
    [InlineData("""{"config":[]}""")]
    [InlineData("""{"config":{}}""")]
    [InlineData("""{"config":{"creditUsagePercent":"lots"}}""")]
    [InlineData("""{"config":{"creditUsagePercent":null}}""")]
    public void Bad_or_missing_data_is_a_parse_error_not_a_crash_or_a_made_up_number(string json)
    {
        Assert.Throws<UsageParseException>(() => GrokBillingParser.Parse(GrokSamples.Json(json)));
    }
}

public sealed class GrokProviderTests
{
    private static GrokUsageProvider Provider(IJsonLineSessionFactory factory, TimeSpan? timeout = null) => new(factory, new FakeClock(), timeout);

    private static FakeSessionFactory Healthy() => new(() => new FakeSession(line => GrokSamples.Healthy(line)));

    [Fact]
    public async Task A_healthy_read_is_ok_with_the_plan_and_sends_only_initialize_and_the_billing_request()
    {
        var factory = Healthy();
        var provider = Provider(factory);

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.Equal(UsageDataSource.GrokAcp, snapshot.DataSource);
        Assert.Equal("SuperGrok", snapshot.Source.PlanTier);
        Assert.Equal(42.5, Assert.Single(snapshot.Windows).PercentUsed);
        Assert.Equal(["initialize", "_x.ai/billing"], factory.Last!.Sent.Select(l => GrokSamples.Parse(l).Method).ToArray());
        Assert.True(factory.Last.Disposed);
        Assert.Equal(TimeSpan.FromMinutes(5), provider.PollInterval);
    }

    [Fact]
    public void No_request_that_could_start_a_session_or_send_a_prompt_can_be_built()
    {
        foreach (var method in new[] { "session/new", "session/prompt", "prompt", "_x.ai/chat", "authenticate", "session/load", "x.ai/billing/../prompt" })
        {
            Assert.Throws<InvalidOperationException>(() => GrokUsageProvider.Request(5, method, "{}"));
        }

        Assert.Equal(["initialize", "_x.ai/billing", "x.ai/billing"], GrokUsageProvider.AllowedMethods);
        var source = string.Join('\n', Directory.GetFiles(Path.Combine(RepoRoot(), "AiUsage", "Grok"), "*.cs").Select(File.ReadAllText));
        foreach (var word in new[] { "session/new", "session/prompt", "\"prompt\"", "authenticate", "--prompt", "\"-p\"", "/usage" })
        {
            Assert.DoesNotContain(word, source);
        }

        Assert.Equal(["agent", "stdio"], GrokAcpSessionFactory.Arguments);
    }

    [Fact]
    public async Task The_plain_method_name_is_tried_only_when_the_first_is_unknown()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(GrokSamples.Billing((id, method) =>
            method == "_x.ai/billing" ? GrokSamples.Error(id, -32601, "Method not found") : GrokSamples.Reply(id, GrokSamples.Real))));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.Equal(3, factory.Last!.Sent.Count);
    }

    [Fact]
    public async Task A_cli_that_knows_neither_name_is_an_unsupported_version_and_nothing_is_run_again()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(GrokSamples.Billing((id, _) => GrokSamples.Error(id, -32601, "Method not found"))));
        var provider = Provider(factory);

        var first = await provider.ReadAsync(CancellationToken.None);
        var second = await provider.ReadAsync(CancellationToken.None);
        var third = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Unsupported, first.State);
        Assert.Equal(UsageState.Unsupported, second.State);
        Assert.Equal(UsageState.Unsupported, third.State);
        Assert.True(provider.Halted);
        Assert.Equal(1, factory.Starts); // polling stopped after the first unsupported answer
        Assert.Contains("unsupported", first.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"config":{"creditUsagePercent":"x"}}""")]
    [InlineData("""{"nothing":1}""")]
    [InlineData("[1,2]")]
    public async Task An_answer_in_a_shape_that_is_not_understood_stops_the_polling(string result)
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line => GrokSamples.Healthy(line, result)));
        var provider = Provider(factory);

        var snapshot = await provider.ReadAsync(CancellationToken.None);
        await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Unsupported, snapshot.State);
        Assert.Equal(1, factory.Starts);
    }

    [Fact]
    public async Task A_refused_initialize_is_an_unsupported_version()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line => [GrokSamples.Error(GrokSamples.Parse(line).Id, -32600, "bad request")]));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Unsupported, snapshot.State);
    }

    [Fact]
    public async Task A_cli_that_is_not_signed_in_says_so_without_repeating_what_it_printed_and_keeps_asking_later()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(GrokSamples.Billing((id, _) => GrokSamples.Error(id, -32000, "Authentication required for someone@example.com"))));
        var provider = Provider(factory);

        var snapshot = await provider.ReadAsync(CancellationToken.None);
        await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NotLoggedIn, snapshot.State);
        Assert.Contains("grok login", snapshot.ErrorText);
        Assert.DoesNotContain("example.com", snapshot.ErrorText);
        Assert.False(provider.Halted);
        Assert.Equal(2, factory.Starts);
    }

    [Fact]
    public async Task Any_other_refusal_is_an_error_that_does_not_stop_polling_and_does_not_echo_the_text()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(GrokSamples.Billing((id, _) => GrokSamples.Error(id, -32603, "internal failure for acct-12345"))));
        var provider = Provider(factory);

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.DoesNotContain("acct-12345", snapshot.ErrorText);
        Assert.False(provider.Halted);
    }

    [Fact]
    public async Task A_missing_cli_is_not_installed()
    {
        var snapshot = await Provider(new ThrowingFactory(new FileNotFoundException())).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NotInstalled, snapshot.State);
    }

    [Fact]
    public async Task A_process_that_ends_before_answering_is_an_error_and_is_disposed()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(_ => []));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.True(factory.Last!.Disposed);
    }

    [Fact]
    public async Task A_process_that_never_answers_is_abandoned_at_the_limit_and_disposed()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(_ => [], hangWhenEmpty: true));
        var clock = Stopwatch.StartNew();

        var snapshot = await Provider(factory, TimeSpan.FromMilliseconds(300)).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.True(factory.Last!.Disposed);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Cancelling_the_read_cancels_it_and_disposes_the_process()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(_ => [], hangWhenEmpty: true));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(factory, TimeSpan.FromSeconds(30)).ReadAsync(cancel.Token));

        Assert.True(factory.Last!.Disposed);
    }

    [Fact]
    public async Task A_reply_with_another_id_or_noise_before_the_right_one_is_skipped()
    {
        var factory = new FakeSessionFactory(() => new FakeSession(line =>
        {
            var (id, method) = GrokSamples.Parse(line);
            return method == "initialize" ? [GrokSamples.Reply(id, "{}")] : [GrokSamples.Reply(99, "{}"), "", "   ", "{broken", GrokSamples.Reply(id, GrokSamples.Real)];
        }));

        var snapshot = await Provider(factory).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
    }

    internal static string RepoRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "PomoQuote.csproj"))) root = Path.GetDirectoryName(root);
        return root!;
    }
}

[Collection("process")]
public sealed class GrokProcessTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pq-grok-" + Guid.NewGuid().ToString("N"));

    public GrokProcessTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private sealed class ScriptFactory(string script) : IJsonLineSessionFactory
    {
        public IJsonLineSession Start() => ProcessJsonLineSession.Start("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], environment: GrokAcpSessionFactory.Environment_);
    }

    private const string ReadLine = "[Console]::In.ReadLine()";

    private static GrokUsageProvider Provider(string script, TimeSpan timeout) => new(new ScriptFactory(script), TimeProvider.System, timeout);

    [Fact]
    public async Task A_real_process_answers_and_is_gone_afterwards()
    {
        var result = GrokSamples.Real.Replace("\r", "").Replace("\n", "").Replace("'", "''");
        var script =
            "$null = " + ReadLine + "\n" +
            "[Console]::Out.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}'); [Console]::Out.Flush()\n" +
            "$null = " + ReadLine + "\n" +
            "[Console]::Out.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":" + result + "}'); [Console]::Out.Flush()\n" +
            "while (" + ReadLine + " -ne $null) { }\n";

        var snapshot = await Provider(script, TimeSpan.FromSeconds(30)).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
        Assert.Equal("0", GrokAcpSessionFactory.Environment_["GROK_WORKTREE_AUTO_GC"]);
        Assert.Equal("1", GrokAcpSessionFactory.Environment_["GROK_DISABLE_AUTOUPDATER"]);
    }

    [Fact]
    public async Task The_environment_reaches_the_child_process()
    {
        await using var session = ProcessJsonLineSession.Start("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.WriteLine($env:GROK_WORKTREE_AUTO_GC + '/' + $env:GROK_DISABLE_AUTOUPDATER)"], environment: GrokAcpSessionFactory.Environment_);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var line = await session.ReceiveAsync(limit.Token);

        Assert.Equal("0/1", line?.Trim());
    }

    [Fact]
    public async Task A_process_that_never_answers_is_killed_with_its_children_at_the_limit()
    {
        var script = "$null = " + ReadLine + "\nStart-Process ping -ArgumentList '-n','41','127.0.0.1' -WindowStyle Hidden\nStart-Sleep -Seconds 120\n";
        var before = Process.GetProcessesByName("PING").Length;
        var clock = Stopwatch.StartNew();

        var snapshot = await Provider(script, TimeSpan.FromSeconds(3)).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"took {clock.Elapsed}");
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
        await Task.Delay(500);
        Assert.True(Process.GetProcessesByName("PING").Length <= before, "a child process of the hung program is still running");
    }

    [Fact]
    public async Task A_program_that_prints_without_ever_ending_a_line_is_cut_at_the_size_limit()
    {
        var script = "$null = " + ReadLine + "\n$s = 'x' * 100000\nwhile ($true) { [Console]::Out.Write($s) }\n";

        var snapshot = await Provider(script, TimeSpan.FromSeconds(20)).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
    }

    [Fact]
    public async Task A_program_that_crashes_after_the_first_message_is_an_error()
    {
        var script = "$null = " + ReadLine + "\nexit 3\n";

        var snapshot = await Provider(script, TimeSpan.FromSeconds(20)).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
    }

    [Fact]
    public async Task The_cmd_shim_is_started_with_exactly_agent_stdio_and_nothing_else()
    {
        var shim = Path.Combine(_folder, "grok.cmd");
        File.WriteAllText(shim, "@echo off\r\necho ARGS=%*\r\n");
        await using var session = new GrokAcpSessionFactory(() => shim).Start();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var line = await session.ReceiveAsync(limit.Token);

        Assert.Equal("ARGS=agent stdio", line?.Trim());
    }

    [Fact]
    public void The_program_is_found_on_the_path_first_and_then_in_the_installer_folder_by_name_only()
    {
        var onPath = GrokLocator.FindExecutable(path => path == @"C:\tools\grok.exe", [@"C:\other", @"C:\tools"], @"C:\Users\x");
        var cmd = GrokLocator.FindExecutable(path => path == @"C:\tools\grok.cmd", [@"C:\tools"], @"C:\Users\x");
        var installed = GrokLocator.FindExecutable(path => path == @"C:\Users\x\.grok\bin\grok.exe", [@"C:\other"], @"C:\Users\x");
        var none = GrokLocator.FindExecutable(_ => false, [@"C:\other", "bad|path"], @"C:\Users\x");

        Assert.Equal(@"C:\tools\grok.exe", onPath);
        Assert.Equal(@"C:\tools\grok.cmd", cmd);
        Assert.Equal(@"C:\Users\x\.grok\bin\grok.exe", installed);
        Assert.Null(none);
    }

    [Fact]
    public async Task The_detector_reads_the_version_with_the_documented_command_and_reports_a_missing_program()
    {
        var found = await new GrokDetector(new VersionRunner("grok 1.0.92 (b06756fd6256) [stable]\n"), () => @"C:\tools\grok.exe").DetectAsync(CancellationToken.None);
        var missing = await new GrokDetector(new VersionRunner(""), () => null).DetectAsync(CancellationToken.None);

        Assert.True(found.Installed);
        Assert.Contains("1.0.92", found.Version);
        Assert.False(missing.Installed);
        Assert.Contains("not found", missing.Detail);
    }

    private sealed class VersionRunner(string output) : ICommandRunner
    {
        public Task<(int ExitCode, string Output)> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Assert.Equal(["version"], arguments); // the detector only ever asks for the version
            return Task.FromResult((0, output));
        }
    }

    [Fact]
    public void The_logo_files_are_the_official_icon_with_its_white_square_made_transparent_and_are_recorded_with_their_source()
    {
        var root = GrokProviderTests.RepoRoot();
        var light = File.ReadAllBytes(Path.Combine(root, "Assets", "Providers", "grok-on-light.png"));
        var dark = File.ReadAllBytes(Path.Combine(root, "Assets", "Providers", "grok-on-dark.png"));
        var credits = File.ReadAllText(Path.Combine(root, "THIRD_PARTY_ASSETS.md"));

        Assert.Equal("117444EB36F0262A99B1C8D762B07F878976BA8481486970636BA9DACD5931AB", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(light)));
        Assert.Equal("CBC39037550847CE649898F72353A8F99A4C6520A485F35472A630D290BBED86", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(dark)));
        Assert.Contains("docs.x.ai", credits);
        Assert.Contains("E54B4BAF2AACA29EB182CB51D22BE3DBAD5AB09C86CCEE4183FEFD8326E6EE28", credits); // the official file\n        Assert.Contains("117444EB36F0262A99B1C8D762B07F878976BA8481486970636BA9DACD5931AB", credits);\n        Assert.Contains("CBC39037550847CE649898F72353A8F99A4C6520A485F35472A630D290BBED86", credits);
        Assert.Contains("grok-on-dark.png", credits);
    }

    [Fact]
    public void The_grok_code_opens_no_file_of_the_cli_home_and_only_names_the_program_in_the_installer_folder()
    {
        var source = string.Join('\n', Directory.GetFiles(Path.Combine(GrokProviderTests.RepoRoot(), "AiUsage", "Grok"), "*.cs").Select(File.ReadAllText));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, @"""\.grok"""));
        Assert.Contains("\".grok\", \"bin\", \"grok.exe\"", source);
        Assert.DoesNotContain("ReadAllText", source);
        Assert.DoesNotContain("File.Open", source);
        Assert.DoesNotContain("XAI_API_KEY", source);
        Assert.DoesNotContain("HttpClient", source);
    }
}

public sealed class GrokViewTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static UsageStatus Status(string json = GrokSamples.Real, DateTimeOffset? observed = null)
    {
        var billing = GrokBillingParser.Parse(GrokSamples.Json(json));
        var snapshot = new UsageSnapshot(new UsageSource(GrokUsageProvider.ProviderId, UsageEnvironment.Windows, "", billing.PlanTier), billing.Windows, observed ?? GrokSamples.Now, UsageDataSource.GrokAcp, UsageState.Ok);
        return new UsageStatus(GrokUsageProvider.ProviderId, "Grok", snapshot, snapshot);
    }

    [Fact]
    public void One_Week_row_with_the_percent_and_the_plan()
    {
        var view = UsageViewBuilder.Build(Status(), UsageSettings.Default, GrokSamples.Now, Utc);

        var row = Assert.Single(view.Rows);
        Assert.Equal("Week", row.Label);
        Assert.Equal("42%", row.Percent);
        Assert.Equal("SuperGrok", view.Plan);
        Assert.True(view.HasData);
    }

    [Fact]
    public void Numbers_read_every_five_minutes_are_not_stale_even_when_the_setting_is_one_minute()
    {
        var settings = UsageSettings.Default with { IntervalMinutes = 1 };

        var fresh = UsageViewBuilder.Build(Status(), settings, GrokSamples.Now.AddMinutes(9), Utc);
        var old = UsageViewBuilder.Build(Status(), settings, GrokSamples.Now.AddMinutes(11), Utc);

        Assert.StartsWith("updated", fresh.Note);
        Assert.StartsWith("stale", old.Note);
    }

    [Fact]
    public void A_percent_over_the_warning_level_is_flagged()
    {
        var view = UsageViewBuilder.Build(Status(GrokSamples.Real.Replace("42.5", "96")), UsageSettings.Default, GrokSamples.Now, Utc);

        Assert.Equal(UsageLevel.Critical, view.Rows.Single().Level);
        Assert.Equal(ProviderSeverity.NearLimit, view.Severity);
    }

    [Fact]
    public void Grok_is_off_by_default_and_the_enabled_flag_round_trips()
    {
        Assert.False(UsageSettings.Default.IsEnabled(GrokUsageProvider.ProviderId));
        Assert.True(UsageSettings.Default.WithEnabled(GrokUsageProvider.ProviderId, true).IsEnabled(GrokUsageProvider.ProviderId));
    }
}

public sealed class GrokAlertTests
{
    private static readonly IReadOnlyList<int> Levels = [80, 95];

    private static UsageSnapshot Snapshot(double percent, int resetDays = 6) =>
        new(new UsageSource(GrokUsageProvider.ProviderId, UsageEnvironment.Windows, "", "SuperGrok"),
            [new QuotaWindow(QuotaKind.Weekly, 10080, percent, GrokSamples.Now.AddDays(resetDays), "", true)],
            GrokSamples.Now, UsageDataSource.GrokAcp, UsageState.Ok);

    [Fact]
    public void Unchanged_numbers_never_repeat_a_balloon_and_a_reset_that_moves_by_seconds_does_not_look_like_a_new_window()
    {
        var engine = new UsageAlertEngine();
        var counts = Enumerable.Range(0, 8).Select(i =>
        {
            var snapshot = Snapshot(90);
            var jittered = snapshot with { Windows = [snapshot.Windows[0] with { ResetsAt = snapshot.Windows[0].ResetsAt!.Value.AddSeconds(i % 3) }] };
            return engine.Evaluate(jittered, Levels).Alerts.Count;
        }).ToArray();

        Assert.Equal([1, 0, 0, 0, 0, 0, 0, 0], counts);
    }

    [Fact]
    public void The_critical_level_fires_once_more_then_the_next_week_re_arms()
    {
        var engine = new UsageAlertEngine();

        Assert.Single(engine.Evaluate(Snapshot(85), Levels).Alerts);
        Assert.Empty(engine.Evaluate(Snapshot(85), Levels).Alerts);
        var critical = Assert.Single(engine.Evaluate(Snapshot(96), Levels).Alerts);
        Assert.Equal(95, critical.Threshold);
        Assert.Empty(engine.Evaluate(Snapshot(96), Levels).Alerts);
        Assert.Single(engine.Evaluate(Snapshot(85, resetDays: 13), Levels).Alerts); // a new week
    }

    [Fact]
    public void A_restart_does_not_repeat_the_balloon_and_the_text_names_the_percent()
    {
        var first = new UsageAlertEngine();
        var alert = Assert.Single(first.Evaluate(Snapshot(90), Levels).Alerts);

        var restarted = new UsageAlertEngine(first.FiredKeys.ToList());

        Assert.Empty(restarted.Evaluate(Snapshot(90), Levels).Alerts);
        Assert.Equal("grok", alert.ProviderId);
        Assert.Equal("", alert.Group);
        Assert.Contains("90", UsageController.AlertMessage(alert, Snapshot(90), GrokSamples.Now));
    }
}

/// <summary>The real widget with Codex, Claude, Antigravity and Grok together: the rows draw at 280 px in both layouts without clipping, and many layout switches do not leak.</summary>
[Collection("process")]
public sealed class GrokWidgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteGrok-" + Guid.NewGuid().ToString("N"));

    public GrokWidgetTests() => Directory.CreateDirectory(_root);

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

    private static string FutureGrok() => GrokSamples.Real
        .Replace("2026-12-01T15:17:24.730+00:00", DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd") + "T15:00:00+00:00")
        .Replace("2026-11-24T15:17:24.730+00:00", DateTime.UtcNow.AddDays(-4).ToString("yyyy-MM-dd") + "T15:00:00+00:00");

    private static readonly string[] Ids = ["codex", "claude", AntigravityUsageProvider.ProviderId, GrokUsageProvider.ProviderId];

    private MainForm NewForm(bool expanded)
    {
        var usagePath = Path.Combine(_root, "usage.json");
        var settings = UsageSettings.Default with { AlertsEnabled = false, SelectedProvider = GrokUsageProvider.ProviderId, Expanded = expanded };
        foreach (var id in Ids) settings = settings.WithEnabled(id, true);
        new UsageSettingsStore(usagePath).Save(settings);
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        var claude = new FakeProvider("claude", _ => Task.FromResult(UsageTestData.Snapshot(30, 55, DateTimeOffset.UtcNow, UsageTestData.Source("claude", "", "max"), DateTimeOffset.UtcNow.AddHours(3), DateTimeOffset.UtcNow.AddDays(4))));
        IUsageProvider[] providers =
        [
            new CodexUsageProvider(new FakeSessionFactory(() => new FakeSession(line => CodexSamples.Healthy(line))), TimeProvider.System),
            claude,
            new AntigravityUsageProvider(new SampleRunner(), TimeProvider.System),
            new GrokUsageProvider(new FakeSessionFactory(() => new FakeSession(line => GrokSamples.Healthy(line, FutureGrok()))), TimeProvider.System),
        ];
        return new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            new QuoteSettingsStore(Path.Combine(_root, "quotes.json")), new BundledQuoteProvider(), new Random(1),
            new UsageSettingsStore(usagePath), new UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            providers, null, new WeatherEffectSettingsStore(Path.Combine(_root, "weather-effects.json")))
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(180)), "the form test did not finish");
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
        for (var i = 0; i < 300 && form.UsageForTests.Views().Count(v => v.Rows.Count > 0) < Ids.Length; i++) Pump(TimeSpan.FromMilliseconds(50));
        foreach (var id in Ids) Assert.Contains(form.UsageForTests.Views(), v => v.Id == id && v.Rows.Count > 0);
        return form;
    }

    private static Bitmap Draw(MainForm form)
    {
        var view = form.WidgetViewForTests;
        var bitmap = new Bitmap(view.ClientSize.Width, view.ClientSize.Height);
        view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, view.ClientSize));
        return bitmap;
    }

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
    public void Both_layouts_draw_the_four_providers_at_280_px_without_text_in_the_margins_at_every_display_scale()
    {
        OnStaThread(() =>
        {
            using var form = WaitForRows(NewForm(expanded: false));
            form.SetWidgetMode(true);
            var view = form.WidgetViewForTests;
            foreach (var expanded in new[] { false, true })
            {
                foreach (var id in expanded ? new[] { GrokUsageProvider.ProviderId } : Ids)
                {
                    form.UsageForTests.SetLayout(expanded, id);
                    foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
                    {
                        form.ClientSize = new Size((int)Math.Round(280 * scale), (int)Math.Round(view.LogicalHeight * scale));
                        Pump(TimeSpan.FromMilliseconds(80));
                        using var bitmap = Draw(form);
                        var unit = view.ClientSize.Width / 280f;
                        var top = (int)(view.UsageTop * unit);

                        Assert.Equal(280, (int)Math.Round(view.ClientSize.Width / unit));
                        Assert.True(MarginIsEmpty(bitmap, unit, top, view.ClientSize.Height, 2, 11), $"text in the left margin ({id}, expanded {expanded}, scale {scale})");
                        Assert.True(MarginIsEmpty(bitmap, unit, top, view.ClientSize.Height, 270, 278), $"text in the right margin ({id}, expanded {expanded}, scale {scale})");
                    }
                }
            }
        });
    }

    [Fact]
    public void A_hundred_layout_switches_and_reads_with_four_providers_do_not_leak_gdi_objects_or_handles()
    {
        OnStaThread(() =>
        {
            using var form = WaitForRows(NewForm(expanded: false));
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
                form.UsageForTests.SetLayout(true, GrokUsageProvider.ProviderId);
                Pump(TimeSpan.FromMilliseconds(20));
                using (Draw(form)) { }
                form.UsageForTests.SetLayout(false, GrokUsageProvider.ProviderId);
                form.UsageForTests.RefreshProvider(GrokUsageProvider.ProviderId);
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
    public void The_settings_dialog_has_a_Grok_row_with_its_own_test_button_and_no_place_to_type_a_secret()
    {
        var source = File.ReadAllText(Path.Combine(GrokProviderTests.RepoRoot(), "UsageSettingsDialog.cs"));

        Assert.Contains("Show Grok usage on the widget", source);
        Assert.Contains("_grokTest", source);
        Assert.Contains("GrokUsageProvider.ProviderId", source);
        Assert.DoesNotContain("PasswordChar", source);
    }
}
