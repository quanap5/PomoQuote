using System.Diagnostics;
using PomoQuote.AiUsage;

namespace PomoQuote.Tests;

/// <summary>Runs the real process plumbing against a throwaway PowerShell script that plays the part of `codex app-server`.</summary>
[Collection("process")]
public sealed class ProcessSessionTests
{
    private sealed class ScriptFactory(string script) : IJsonLineSessionFactory
    {
        public IJsonLineSession Start() => ProcessJsonLineSession.Start("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script]);
    }

    private const string ReadLine = "[Console]::In.ReadLine()";

    private static CodexUsageProvider Provider(string script, TimeSpan timeout) => new(new ScriptFactory(script), TimeProvider.System, timeout);

    private static string Result => CodexSamples.RateLimitsResult.Replace("\r", "").Replace("\n", "").Replace("'", "''");

    private static string Server(string rateLimitsReply) =>
        "$null = " + ReadLine + "\n" +
        "[Console]::Out.WriteLine('{\"id\":1,\"result\":{\"userAgent\":\"x\"}}'); [Console]::Out.Flush()\n" +
        "$null = " + ReadLine + "\n" +
        "$null = " + ReadLine + "\n" +
        rateLimitsReply + "\n" +
        "[Console]::Out.Flush()\n" +
        "while (" + ReadLine + " -ne $null) { }\n";
    [Fact]
    public async Task A_real_process_that_answers_gives_an_ok_snapshot_and_exits_after_its_input_is_closed()
    {
        var script = Server("[Console]::Out.WriteLine('{\"id\":2,\"result\":" + Result + "}')");

        var snapshot = await Provider(script, TimeSpan.FromSeconds(20)).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
    }

    [Fact]
    public async Task A_reply_that_arrives_in_two_pieces_is_assembled_into_one_line()
    {
        var script = Server("[Console]::Out.Write('{\"id\":2,\"result\":'); [Console]::Out.Flush(); Start-Sleep -Milliseconds 400; [Console]::Out.WriteLine('" + Result + "}')");

        var snapshot = await Provider(script, TimeSpan.FromSeconds(20)).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, snapshot.State);
    }

    [Fact]
    public async Task A_process_that_never_answers_is_killed_at_the_timeout()
    {
        var script = "$null = " + ReadLine + "\nStart-Sleep -Seconds 120\n";
        var clock = Stopwatch.StartNew();

        var snapshot = await Provider(script, TimeSpan.FromSeconds(3)).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"took {clock.Elapsed}");
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
    }

    [Fact]
    public async Task A_process_that_crashes_before_answering_is_an_error()
    {
        var snapshot = await Provider("exit 3", TimeSpan.FromSeconds(20)).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
    }

    [Fact]
    public async Task A_program_that_does_not_exist_is_reported_as_not_installed()
    {
        var provider = new CodexUsageProvider(new MissingFactory(), TimeProvider.System, TimeSpan.FromSeconds(5));

        var snapshot = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NotInstalled, snapshot.State);
    }

    private sealed class MissingFactory : IJsonLineSessionFactory
    {
        public IJsonLineSession Start() => ProcessJsonLineSession.Start(@"C:\definitely\not\here\codex.exe", ["app-server"]);
    }

    [Fact]
    public async Task Kill_all_ends_a_running_process_at_app_exit()
    {
        var script = "$null = " + ReadLine + "\nStart-Sleep -Seconds 120\n";
        var read = Provider(script, TimeSpan.FromSeconds(60)).ReadAsync(CancellationToken.None);
        await Task.Delay(1500);
        Assert.Equal(1, ProcessJsonLineSession.LiveCount);

        ProcessJsonLineSession.KillAll();
        var snapshot = await read;

        Assert.Equal(UsageState.Error, snapshot.State);
        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
    }

    [Fact]
    public async Task Many_reads_in_a_row_leave_no_process_behind()
    {
        var script = Server("[Console]::Out.WriteLine('{\"id\":2,\"result\":" + Result + "}')");
        var provider = Provider(script, TimeSpan.FromSeconds(20));

        for (var i = 0; i < 6; i++) Assert.Equal(UsageState.Ok, (await provider.ReadAsync(CancellationToken.None)).State);

        Assert.Equal(0, ProcessJsonLineSession.LiveCount);
    }
}

[CollectionDefinition("process", DisableParallelization = true)]
public sealed class ProcessCollection;
