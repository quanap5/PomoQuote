using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PomoQuote.AiUsage;
using PomoQuote.AiUsage.Claude;

namespace PomoQuote.Tests;

public sealed class ClaudeBridgeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private ClaudeSnapshotStore Store => new(_dir);

    // A realistic status line payload (made-up values) with everything that must NOT end up in a snapshot.
    private static string Payload(bool withLimits, bool apiResponse = true) =>
        "{\"session_id\":\"secret-session-id-1234\",\"transcript_path\":\"C:/Users/someone/.claude/projects/x.jsonl\",\"cwd\":\"C:/Users/someone/private-project\"," +
        "\"model\":{\"id\":\"claude-x\",\"display_name\":\"X\"},\"workspace\":{\"current_dir\":\"C:/Users/someone/private-project\"},\"version\":\"2.1.331\"," +
        "\"cost\":{\"total_cost_usd\":1.23},\"email\":\"someone@example.com\"," +
        "\"context_window\":{\"current_usage\":" + (apiResponse ? "{\"input_tokens\":5}" : "null") + "}" +
        (withLimits ? ",\"rate_limits\":{\"five_hour\":{\"used_percentage\":18.4,\"resets_at\":1795500600},\"seven_day\":{\"used_percentage\":28,\"resets_at\":1795896000}}" : "") + "}";

    private sealed class FakeChain(Func<string, byte[], (byte[], int)?> answer) : IChainRunner
    {
        public List<string> Commands { get; } = [];

        public byte[]? LastInput { get; private set; }

        public (byte[] Output, int ExitCode)? Run(string command, byte[] input, TimeSpan timeout)
        {
            Commands.Add(command);
            LastInput = input;
            return answer(command, input);
        }
    }

    private int Run(string payload, string[] args, IChainRunner chain, out string output)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        using var stdout = new MemoryStream();
        var code = ClaudeBridge.Run(args, input, stdout, chain, Store, TimeProvider.System);
        output = Encoding.UTF8.GetString(stdout.ToArray());
        return code;
    }

    private static string[] Bridge(string? original = null) =>
        original is null ? ["--bridge", "claude"] : ["--bridge", "claude", "--chain-b64", ClaudeBridge.EncodeChain(original)];

    [Fact]
    public void The_original_command_gets_the_same_input_and_its_output_and_exit_code_pass_through_unchanged()
    {
        var chain = new FakeChain((_, _) => (Encoding.UTF8.GetBytes("\u001b[32mmy line\u001b[0m\n"), 7));

        var code = Run(Payload(true), Bridge("node C:/t/line.js"), chain, out var output);

        Assert.Equal(7, code);
        Assert.Equal("\u001b[32mmy line\u001b[0m\n", output);
        Assert.Equal(["node C:/t/line.js"], chain.Commands);
        Assert.Equal(Payload(true), Encoding.UTF8.GetString(chain.LastInput!));
    }

    [Fact]
    public void A_snapshot_is_written_with_only_the_quota_and_hashed_session()
    {
        Run(Payload(true), Bridge("x"), new FakeChain((_, _) => ([], 0)), out _);

        var snapshot = Assert.Single(Store.ReadAll());
        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal(QuotaKind.FiveHour, snapshot.Windows[0].Kind);
        Assert.Equal(18.4, snapshot.Windows[0].PercentUsed);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795500600), snapshot.Windows[0].ResetsAt);
        Assert.Equal(QuotaKind.Weekly, snapshot.Windows[1].Kind);
        Assert.True(snapshot.HadApiResponse);
        Assert.True(snapshot.RateLimitsPresent);
        Assert.Equal("2.1.331", snapshot.Version);
        Assert.Equal(ClaudeSnapshotSource.StatusLine, snapshot.Source);
        Assert.Equal(12, snapshot.SessionHash.Length);
    }

    [Fact]
    public void The_snapshot_file_has_only_whitelisted_keys_and_none_of_the_private_values()
    {
        Run(Payload(true), Bridge(), new FakeChain((_, _) => null), out _);

        var path = Assert.Single(Directory.GetFiles(_dir));
        var text = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(text);
        var keys = new HashSet<string>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { keys.Add(p.Name); Walk(p.Value); }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(x);
        }

        Walk(doc.RootElement);

        Assert.All(keys, key => Assert.Contains(key, ClaudeSnapshotStore.AllowedKeys));
        foreach (var secret in new[] { "secret-session-id-1234", "someone", "example.com", "private-project", ".jsonl", "total_cost", "transcript", "cwd", "email", "claude-x" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("secret-session-id-1234", Path.GetFileName(path));
    }

    [Fact]
    public void Before_the_first_reply_the_snapshot_says_no_limits_yet_and_no_api_response()
    {
        Run(Payload(withLimits: false, apiResponse: false), Bridge(), new FakeChain((_, _) => null), out _);

        var snapshot = Assert.Single(Store.ReadAll());
        Assert.False(snapshot.RateLimitsPresent);
        Assert.False(snapshot.HadApiResponse);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public void Without_a_chain_the_bridge_prints_a_short_usage_line_and_exits_zero()
    {
        var code = Run(Payload(true), Bridge(), new FakeChain((_, _) => throw new InvalidOperationException()), out var output);

        Assert.Equal(0, code);
        Assert.Equal("5h 18% · week 28%", output);
    }

    [Fact]
    public void Without_limits_and_without_a_chain_nothing_is_printed()
    {
        Run(Payload(false), Bridge(), new FakeChain((_, _) => null), out var output);

        Assert.Equal("", output);
    }

    [Fact]
    public void A_chain_that_times_out_or_cannot_run_falls_back_to_the_usage_line_and_never_fails()
    {
        var code = Run(Payload(true), Bridge("slow command"), new FakeChain((_, _) => null), out var output);

        Assert.Equal(0, code);
        Assert.Equal("5h 18% · week 28%", output);
    }

    [Fact]
    public void A_chain_runner_that_throws_does_not_break_the_bridge()
    {
        var code = Run(Payload(true), Bridge("x"), new FakeChain((_, _) => throw new IOException("boom")), out var output);

        Assert.Equal(0, code);
        Assert.Equal("5h 18% · week 28%", output);
        Assert.Single(Store.ReadAll());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2]")]
    [InlineData("{\"rate_limits\": 5}")]
    public void Unusable_input_writes_no_bad_snapshot_but_the_original_still_gets_it_and_nothing_throws(string payload)
    {
        var chain = new FakeChain((_, _) => (Encoding.UTF8.GetBytes("ok"), 0));

        var code = Run(payload, Bridge("x"), chain, out var output);

        Assert.Equal(0, code);
        Assert.Equal("ok", output);
        Assert.Equal(payload, Encoding.UTF8.GetString(chain.LastInput!));
        Assert.All(Store.ReadAll(), s => Assert.Empty(s.Windows));
    }

    [Fact]
    public void A_damaged_chain_argument_is_ignored_like_no_chain()
    {
        var chain = new FakeChain((_, _) => throw new InvalidOperationException("must not run"));

        var code = Run(Payload(true), ["--bridge", "claude", "--chain-b64", "!!!not base64"], chain, out var output);

        Assert.Equal(0, code);
        Assert.Equal("5h 18% · week 28%", output);
        Assert.Empty(chain.Commands);
    }

    [Fact]
    public void A_snapshot_store_that_cannot_write_is_only_a_missed_update()
    {
        File.WriteAllText(_dir, "a file where the folder should be");
        var chain = new FakeChain((_, _) => (Encoding.UTF8.GetBytes("line"), 0));

        var code = Run(Payload(true), Bridge("x"), chain, out var output);

        Assert.Equal(0, code);
        Assert.Equal("line", output);
    }

    [Fact]
    public void Several_sessions_keep_separate_files_and_the_newest_is_first()
    {
        var older = Payload(true).Replace("secret-session-id-1234", "session-A");
        var newer = Payload(true).Replace("secret-session-id-1234", "session-B").Replace("18.4", "40");
        Run(older, Bridge(), new FakeChain((_, _) => null), out _);
        Thread.Sleep(30);
        Run(newer, Bridge(), new FakeChain((_, _) => null), out _);

        var all = Store.ReadAll();

        Assert.Equal(2, all.Count);
        Assert.Equal(40, all[0].Windows[0].PercentUsed);
        Assert.NotEqual(all[0].SessionHash, all[1].SessionHash);
    }

    [Fact]
    public void The_same_session_updates_its_file_instead_of_adding_one()
    {
        Run(Payload(true), Bridge(), new FakeChain((_, _) => null), out _);
        Run(Payload(true).Replace("18.4", "20"), Bridge(), new FakeChain((_, _) => null), out _);

        var snapshot = Assert.Single(Store.ReadAll());
        Assert.Equal(20, snapshot.Windows[0].PercentUsed);
    }

    [Fact]
    public void Files_older_than_seven_days_are_deleted_and_recent_ones_kept()
    {
        Run(Payload(true), Bridge(), new FakeChain((_, _) => null), out _);
        var path = Assert.Single(Directory.GetFiles(_dir));

        Assert.Equal(0, Store.DeleteOld(DateTimeOffset.UtcNow.AddDays(6)));
        Assert.True(File.Exists(path));
        Assert.Equal(1, Store.DeleteOld(DateTimeOffset.UtcNow.AddDays(8)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Damaged_snapshot_files_are_skipped_when_reading()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "claude-windows-bad.json"), "{{{");
        File.WriteAllText(Path.Combine(_dir, "claude-windows-bad2.json"), "{\"schema\":99,\"windows\":[]}");
        File.WriteAllText(Path.Combine(_dir, "claude-windows-bad3.json"), "{\"schema\":1,\"windows\":[{\"kind\":\"FiveHour\",\"percentUsed\":500}]}");

        var all = Store.ReadAll();

        Assert.Single(all); // the schema-1 file is read, its impossible window dropped
        Assert.Empty(all[0].Windows);
    }

    // ---------- the real program: the apphost PomoQuote.exe next to the test assembly ----------

    private static string? Exe()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PomoQuote.exe");
        return File.Exists(path) ? path : null;
    }

    private (string Output, int Code, long Milliseconds) RunExe(string payload, string? chainCommand)
    {
        var info = new ProcessStartInfo(Exe()!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        info.ArgumentList.Add("--bridge");
        info.ArgumentList.Add("claude");
        if (chainCommand is not null)
        {
            info.ArgumentList.Add("--chain-b64");
            info.ArgumentList.Add(ClaudeBridge.EncodeChain(chainCommand));
        }

        info.Environment["POMOQUOTE_USAGE_DIR"] = _dir;
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(info)!;
        var bytes = new UTF8Encoding(false).GetBytes(payload);
        process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(20000), "the bridge did not finish");
        return (output, process.ExitCode, clock.ElapsedMilliseconds);
    }

    [Fact]
    public void The_real_program_with_a_chain_prints_what_the_original_prints_and_writes_the_snapshot()
    {
        if (Exe() is null) return;

        var (output, code, _) = RunExe(Payload(true), ClaudeShell.FindGitBash() is not null ? "echo CHAINED-OK; exit 3" : "Write-Output 'CHAINED-OK'; exit 3");

        Assert.Contains("CHAINED-OK", output);
        Assert.Equal(3, code);
        Assert.Single(Store.ReadAll());
    }

    [Fact]
    public void The_real_program_without_a_chain_prints_the_usage_line_without_opening_a_window()
    {
        if (Exe() is null) return;

        var (output, code, _) = RunExe(Payload(true), null);

        Assert.Equal("5h 18% · week 28%", output.Trim());
        Assert.Equal(0, code);
    }

    [Fact]
    public void The_real_program_starts_and_finishes_quickly_without_a_chain()
    {
        if (Exe() is null) return;

        RunExe(Payload(true), null); // warm the disk cache
        var times = Enumerable.Range(0, 5).Select(_ => RunExe(Payload(true), null).Milliseconds).OrderBy(t => t).ToArray();

        // The median is reported in the pull request; the guard here is loose so a busy machine does not fail the build.
        Assert.True(times[2] < 1500, $"median {times[2]} ms ({string.Join(", ", times)})");
    }
}
