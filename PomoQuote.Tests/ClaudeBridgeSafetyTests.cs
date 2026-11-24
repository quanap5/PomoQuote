using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PomoQuote.AiUsage;
using PomoQuote.AiUsage.Claude;

namespace PomoQuote.Tests;

/// <summary>
/// The bridge must never be able to take the user's own status line down: a stable copy of the program, a command that still runs
/// the original when the program is gone, a start-up check, and a settings path that follows CLAUDE_CONFIG_DIR.
/// Everything works on throwaway files; the real ~/.claude is never opened.
/// </summary>
public sealed class ClaudeBridgeSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteSafety-" + Guid.NewGuid().ToString("N"));

    private string Settings => Path.Combine(_root, "claude", "settings.json");

    private string State => Path.Combine(_root, "state");

    private string App => Path.Combine(_root, "app");

    private string AppExe => Path.Combine(App, "PomoQuote.exe");

    private string BridgeDirectory => Path.Combine(State, "bridge");

    private const string Custom = "{\n  \"theme\": \"dark\",\n  \"statusLine\": { \"type\": \"command\", \"command\": \"node C:/t/line.js\" }\n}\n";

    public ClaudeBridgeSafetyTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        Directory.CreateDirectory(App);
        File.WriteAllText(AppExe, "exe");
        File.WriteAllText(Path.Combine(App, "PomoQuote.dll"), "dll");
        File.WriteAllText(Path.Combine(App, "PomoQuote.runtimeconfig.json"), "{}");
        File.WriteAllText(Path.Combine(App, "PomoQuote.pdb"), "symbols");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ClaudeBridgeInstaller Installer(bool bash = true, string? source = null) =>
        new(Settings, State, () => source ?? AppExe, () => bash ? @"C:\Git\bin\bash.exe" : null, () => new DateTimeOffset(2026, 11, 24, 7, 0, 0, TimeSpan.Zero));

    // ---------- (a) only PomoQuote.exe ----------

    [Theory]
    [InlineData("dotnet.exe")]
    [InlineData("PomoQuote.dll")]
    [InlineData("pomoquote-helper.exe")]
    public void A_program_that_is_not_PomoQuote_exe_is_refused_and_nothing_is_changed(string name)
    {
        var other = Path.Combine(App, name);
        File.WriteAllText(other, "x");
        File.WriteAllText(Settings, Custom);
        var installer = Installer(source: other);

        Assert.Null(installer.Preview());
        Assert.Contains(name, installer.CannotInstallReason());
        Assert.Equal(BridgeResult.WrongProgram, installer.Install());

        Assert.Equal(Custom, File.ReadAllText(Settings));
        Assert.False(Directory.Exists(State));
    }

    [Fact]
    public void A_program_file_that_is_gone_is_refused()
    {
        File.WriteAllText(Settings, Custom);
        File.Delete(AppExe);

        Assert.Equal(BridgeResult.WrongProgram, Installer().Install());
        Assert.Equal(Custom, File.ReadAllText(Settings));
    }

    // ---------- (b) the stable copy ----------

    [Fact]
    public void Install_copies_the_bridge_to_the_stable_folder_and_the_command_points_there()
    {
        File.WriteAllText(Settings, Custom);

        Assert.Equal(BridgeResult.Done, Installer().Install());

        Assert.True(File.Exists(Path.Combine(BridgeDirectory, "PomoQuote.exe")));
        Assert.True(File.Exists(Path.Combine(BridgeDirectory, "PomoQuote.dll")));
        Assert.True(File.Exists(Path.Combine(BridgeDirectory, "PomoQuote.runtimeconfig.json")));
        Assert.False(File.Exists(Path.Combine(BridgeDirectory, "PomoQuote.pdb")));
        var command = Installer().Detect().Command!;
        Assert.Equal(Path.Combine(BridgeDirectory, "PomoQuote.exe"), ClaudeBridgeCommand.ExecutableOf(command));
        Assert.DoesNotContain(App.Replace('\\', '/'), command);
    }

    [Fact]
    public void Moving_or_deleting_the_apps_own_folder_does_not_break_the_bridge()
    {
        File.WriteAllText(Settings, Custom);
        Installer().Install();

        Directory.Delete(App, recursive: true);

        var status = Installer().Check();
        Assert.Empty(status.Problems);
        Assert.True(File.Exists(Path.Combine(BridgeDirectory, "PomoQuote.exe")));
    }

    [Fact]
    public void After_an_update_the_copy_is_refreshed_and_the_claude_settings_are_not_touched()
    {
        File.WriteAllText(Settings, Custom);
        Installer().Install();
        var settingsAfterInstall = File.ReadAllBytes(Settings);
        File.WriteAllText(Path.Combine(App, "PomoQuote.dll"), "a newer dll");
        File.SetLastWriteTimeUtc(Path.Combine(App, "PomoQuote.dll"), DateTime.UtcNow.AddMinutes(5));

        Assert.True(Installer().RefreshProgramCopy());

        Assert.Equal("a newer dll", File.ReadAllText(Path.Combine(BridgeDirectory, "PomoQuote.dll")));
        Assert.Equal(settingsAfterInstall, File.ReadAllBytes(Settings));
        Assert.False(Installer().RefreshProgramCopy()); // nothing newer now
    }

    [Fact]
    public void The_copy_is_not_refreshed_or_created_when_the_bridge_is_not_set_up()
    {
        File.WriteAllText(Settings, Custom);

        Assert.False(Installer().RefreshProgramCopy());

        Assert.False(Directory.Exists(State));
    }

    // ---------- (c) the command still works without the program ----------

    private const string BashOriginal = "cat | tr a-z A-Z; exit 3";

    private const string PowerShellOriginal = "[Console]::In.ReadToEnd().ToUpper(); exit 3";

    private static (string Output, int ExitCode) RunShell(string file, IEnumerable<string> arguments, string input, IDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var (key, value) in environment ?? new Dictionary<string, string>()) start.Environment[key] = value;
        using var process = Process.Start(start)!;
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "the shell did not finish");
        _ = error.Result;
        return (output, process.ExitCode);
    }

    private static (string Output, int ExitCode) RunInBash(string command, string input, IDictionary<string, string>? environment = null) =>
        RunShell(ClaudeShell.FindGitBash()!, ["-c", command], input, environment);

    private static (string Output, int ExitCode) RunInPowerShell(string command, string input, IDictionary<string, string>? environment = null) =>
        RunShell("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command], input, environment);

    [Fact]
    [Trait("Category", "RealShell")]
    public void With_the_program_absent_the_bash_command_still_runs_the_original_with_the_same_input_and_exit_code()
    {
        if (ClaudeShell.FindGitBash() is null) return; // Git Bash is not installed on this PC
        var command = ClaudeBridgeCommand.Build(Path.Combine(_root, "gone", "PomoQuote.exe"), BashOriginal, bash: true);

        var (output, exit) = RunInBash(command, "hello from claude");

        Assert.Equal("HELLO FROM CLAUDE", output);
        Assert.Equal(3, exit);
    }

    [Fact]
    [Trait("Category", "RealShell")]
    public void With_the_program_absent_and_no_original_the_bash_command_prints_nothing_and_succeeds()
    {
        if (ClaudeShell.FindGitBash() is null) return;
        var command = ClaudeBridgeCommand.Build(Path.Combine(_root, "gone", "PomoQuote.exe"), null, bash: true);

        var (output, exit) = RunInBash(command, "{}");

        Assert.Equal("", output);
        Assert.Equal(0, exit);
    }

    [Fact]
    [Trait("Category", "RealShell")]
    public void With_the_program_absent_the_powershell_command_still_runs_the_original_with_the_same_input_and_exit_code()
    {
        var command = ClaudeBridgeCommand.Build(Path.Combine(_root, "gone", "PomoQuote.exe"), PowerShellOriginal, bash: false);

        var (output, exit) = RunInPowerShell(command, "hello from claude");

        Assert.Equal("HELLO FROM CLAUDE", output.TrimEnd('\r', '\n'));
        Assert.Equal(3, exit);
    }

    [Fact]
    [Trait("Category", "RealShell")]
    public void With_the_program_absent_and_no_original_the_powershell_command_prints_nothing_and_succeeds()
    {
        var command = ClaudeBridgeCommand.Build(Path.Combine(_root, "gone", "PomoQuote.exe"), null, bash: false);

        var (output, exit) = RunInPowerShell(command, "{}");

        Assert.Equal("", output);
        Assert.Equal(0, exit);
    }

    [Fact]
    [Trait("Category", "RealShell")]
    public void With_the_program_present_the_real_bridge_runs_in_both_shells_and_passes_the_original_through()
    {
        var real = Path.Combine(AppContext.BaseDirectory, "PomoQuote.exe");
        Assert.True(File.Exists(real));
        var environment = new Dictionary<string, string> { ["POMOQUOTE_USAGE_DIR"] = Path.Combine(_root, "usage") };
        const string input = "{\"version\":\"2.1.0\"}";

        if (ClaudeShell.FindGitBash() is not null)
        {
            var bash = RunInBash(ClaudeBridgeCommand.Build(real, "cat; exit 4", bash: true), input, environment);
            Assert.Equal(input, bash.Output);
            Assert.Equal(4, bash.ExitCode);
        }

        // The bridge runs the original in Git Bash when it is installed, else in PowerShell. (PowerShell does not hand the exit code of
        // a program without a console window back to Claude Code; the output and the input pass through.)
        var original = ClaudeShell.FindGitBash() is null ? "[Console]::In.ReadToEnd()" : "cat";
        var powershell = RunInPowerShell(ClaudeBridgeCommand.Build(real, original, bash: false), input, environment);
        Assert.Equal(input, powershell.Output.TrimEnd('\r', '\n'));
    }

    // ---------- (d) the start-up check and the strip ----------

    [Fact]
    public void Start_up_says_so_when_the_bridge_program_is_missing_and_is_quiet_when_all_is_well()
    {
        File.WriteAllText(Settings, Custom);
        var installer = Installer();
        installer.Install();
        var balloons = new List<(string Title, string Message)>();

        var healthy = ClaudeActions.StartupCheck(installer, (t, m) => balloons.Add((t, m)));
        Assert.Empty(balloons);
        Assert.False(healthy.NeedsAttention);

        Directory.Delete(BridgeDirectory, recursive: true);
        var broken = ClaudeActions.StartupCheck(installer, (t, m) => balloons.Add((t, m)));

        Assert.True(broken.ProgramMissing);
        var balloon = Assert.Single(balloons);
        Assert.Contains("missing", balloon.Message);
        Assert.Contains("own status line still runs", balloon.Message);
    }

    [Fact]
    public void Start_up_stays_quiet_when_the_bridge_is_not_set_up_and_never_writes_to_the_claude_settings()
    {
        File.WriteAllText(Settings, Custom);
        var before = File.GetLastWriteTimeUtc(Settings);
        var balloons = new List<string>();

        ClaudeActions.StartupCheck(Installer(), (_, m) => balloons.Add(m));

        Assert.Empty(balloons);
        Assert.Equal(before, File.GetLastWriteTimeUtc(Settings));
        Assert.Equal(Custom, File.ReadAllText(Settings));
    }

    [Fact]
    public void Start_up_refreshes_the_copy_after_an_update_before_checking()
    {
        File.WriteAllText(Settings, Custom);
        Installer().Install();
        File.WriteAllText(Path.Combine(App, "PomoQuote.dll"), "newer");
        File.SetLastWriteTimeUtc(Path.Combine(App, "PomoQuote.dll"), DateTime.UtcNow.AddMinutes(5));

        ClaudeActions.StartupCheck(Installer(), (_, _) => { });

        Assert.Equal("newer", File.ReadAllText(Path.Combine(BridgeDirectory, "PomoQuote.dll")));
    }

    private static ClaudeSnapshotProvider Provider(string folder, StatusLineState state, bool broken, DateTimeOffset now) =>
        new(new ClaudeSnapshotStore(folder), () => state, new FixedClock(now), () => broken);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task The_strip_says_bridge_missing_even_when_old_numbers_exist_and_keeps_them_marked()
    {
        var now = new DateTimeOffset(2026, 11, 24, 7, 0, 0, TimeSpan.Zero);
        var folder = Path.Combine(_root, "usage");
        var store = new ClaudeSnapshotStore(folder);
        store.Write(new ClaudeSnapshot(now.AddMinutes(-3), ClaudeSnapshotSource.StatusLine, "2.1.0", "abc", true, true,
            [new ClaudeSnapshotWindow(QuotaKind.FiveHour, 40, now.AddHours(2))]));

        var good = await Provider(folder, StatusLineState.Bridged, broken: false, now).ReadAsync(CancellationToken.None);
        var missing = await Provider(folder, StatusLineState.Bridged, broken: true, now).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.Ok, good.State);
        Assert.Equal(UsageState.BridgeMissing, missing.State);
        var view = UsageViewBuilder.Build(new UsageStatus("claude", "Claude", missing, good), UsageSettings.Default.WithEnabled("claude", true), now, TimeZoneInfo.Utc);
        Assert.StartsWith("bridge missing", view.Note);
        Assert.True(view.IsProblem);
        Assert.Single(view.Rows); // the last good numbers stay on screen
    }

    [Theory]
    [InlineData(StatusLineState.None)]
    [InlineData(StatusLineState.Custom)]
    [InlineData(StatusLineState.Unreadable)]
    public async Task Bridge_missing_is_only_said_when_the_bridge_was_set_up(StatusLineState state)
    {
        var now = new DateTimeOffset(2026, 11, 24, 7, 0, 0, TimeSpan.Zero);

        var snapshot = await Provider(Path.Combine(_root, "usage"), state, broken: true, now).ReadAsync(CancellationToken.None);

        Assert.Equal(UsageState.NotSetUp, snapshot.State);
    }

    // ---------- (2) CLAUDE_CONFIG_DIR and the override ----------

    [Fact]
    public void The_settings_path_follows_the_override_then_CLAUDE_CONFIG_DIR_then_the_user_profile()
    {
        string? Env(Dictionary<string, string> values, string name) => values.GetValueOrDefault(name);
        var profile = @"C:\Users\someone";

        Assert.Equal(Path.Combine(profile, ".claude", "settings.json"), ClaudeBridgeInstaller.ResolveSettingsPath(n => Env([], n), profile));
        Assert.Equal(Path.Combine(@"D:\cfg", "settings.json"), ClaudeBridgeInstaller.ResolveSettingsPath(n => Env(new() { ["CLAUDE_CONFIG_DIR"] = @"D:\cfg" }, n), profile));
        Assert.Equal(@"E:\throwaway\s.json", ClaudeBridgeInstaller.ResolveSettingsPath(n => Env(new() { ["CLAUDE_CONFIG_DIR"] = @"D:\cfg", ["POMOQUOTE_CLAUDE_SETTINGS"] = @"E:\throwaway\s.json" }, n), profile));
        Assert.Equal(Path.Combine(profile, ".claude", "settings.json"), ClaudeBridgeInstaller.ResolveSettingsPath(n => Env(new() { ["CLAUDE_CONFIG_DIR"] = "" }, n), profile));
    }

    // ---------- (3) the shell ----------

    [Fact]
    public void Check_warns_when_the_shell_claude_would_use_now_is_not_the_one_the_command_was_written_for()
    {
        File.WriteAllText(Settings, Custom);
        Installer(bash: false).Install(); // PowerShell form

        var gitBashInstalledLater = Installer(bash: true).Check();
        var sameShell = Installer(bash: false).Check();

        Assert.True(gitBashInstalledLater.ShellMismatch);
        Assert.True(gitBashInstalledLater.NeedsAttention);
        Assert.Contains(gitBashInstalledLater.Problems, p => p.Contains("Git Bash is installed now"));
        Assert.False(sameShell.ShellMismatch);

        Installer(bash: false).Uninstall();
        Installer(bash: true).Install(); // bash form
        var gitBashGone = Installer(bash: false).Check();
        Assert.True(gitBashGone.ShellMismatch);
        Assert.Contains(gitBashGone.Problems, p => p.Contains("Git Bash was not found"));
    }

    [Fact]
    public void Start_up_balloon_names_the_shell_mismatch()
    {
        File.WriteAllText(Settings, Custom);
        Installer(bash: false).Install();
        var balloons = new List<string>();

        ClaudeActions.StartupCheck(Installer(bash: true), (_, m) => balloons.Add(m));

        Assert.Contains("shell", Assert.Single(balloons));
    }

    // ---------- (4) no settings file yet ----------

    [Fact]
    public void A_new_profile_without_a_settings_file_can_be_set_up_and_the_undo_removes_the_file_again()
    {
        var installer = Installer();

        Assert.Equal(StatusLineState.None, installer.Detect().State);
        var preview = installer.Preview()!;
        Assert.True(preview.NoFileYet);
        Assert.False(File.Exists(Settings)); // a preview creates nothing

        Assert.Equal(BridgeResult.Done, installer.Install());

        var root = JsonDocument.Parse(File.ReadAllText(Settings)).RootElement;
        Assert.Equal(["statusLine"], root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(StatusLineState.Bridged, installer.Detect().State);
        var note = Assert.Single(Directory.GetFiles(installer.BackupDirectory));
        Assert.Contains("no-file-before", note);
        Assert.Contains("no Claude settings file", File.ReadAllText(note));
        Assert.True(JsonDocument.Parse(File.ReadAllText(Path.Combine(State, "bridge-state.json"))).RootElement.GetProperty("noFileBefore").GetBoolean());

        Assert.Equal(BridgeResult.Done, installer.Uninstall());

        Assert.False(File.Exists(Settings));
        Assert.Equal(StatusLineState.None, installer.Detect().State);
    }

    [Fact]
    public void The_file_created_for_a_new_profile_is_kept_when_the_user_added_something_to_it()
    {
        var installer = Installer();
        installer.Install();
        File.WriteAllText(Settings, File.ReadAllText(Settings).TrimEnd().TrimEnd('}').TrimEnd() + ",\n  \"theme\": \"dark\"\n}\n");

        Assert.Equal(BridgeResult.Done, installer.Uninstall());

        var root = JsonDocument.Parse(File.ReadAllText(Settings)).RootElement;
        Assert.Equal("dark", root.GetProperty("theme").GetString());
        Assert.False(root.TryGetProperty("statusLine", out _));
    }

    [Fact]
    public void If_the_settings_file_appears_while_it_is_being_prepared_nothing_is_overwritten()
    {
        var installer = Installer();
        installer.AfterBackup = () => File.WriteAllText(Settings, Custom); // Claude Code created it just now

        Assert.Equal(BridgeResult.ChangedMeanwhile, installer.Install());

        Assert.Equal(Custom, File.ReadAllText(Settings));
    }

    // ---------- (5) the undo takes a fresh backup ----------

    [Fact]
    public void Remove_first_saves_a_copy_of_the_file_as_it_is_now()
    {
        File.WriteAllText(Settings, Custom);
        var installer = Installer();
        installer.Install();
        File.WriteAllText(Settings, File.ReadAllText(Settings).Replace("\"dark\"", "\"light\""));
        var beforeRemove = File.ReadAllBytes(Settings);

        Assert.Equal(BridgeResult.Done, installer.Uninstall());

        var copy = Assert.Single(Directory.GetFiles(installer.BackupDirectory, "settings-before-remove-*"));
        Assert.Equal(beforeRemove, File.ReadAllBytes(copy));
        Assert.Contains("light", File.ReadAllText(Settings));
    }

    [Fact]
    public void Remove_changes_nothing_when_that_copy_cannot_be_saved()
    {
        File.WriteAllText(Settings, Custom);
        var installer = Installer();
        installer.Install();
        var installed = File.ReadAllBytes(Settings);
        Directory.Delete(installer.BackupDirectory, recursive: true);
        File.WriteAllText(installer.BackupDirectory, "a file where the folder should be"); // the backup folder cannot be created

        Assert.Equal(BridgeResult.WriteFailed, installer.Uninstall());

        Assert.Equal(installed, File.ReadAllBytes(Settings));
    }
}
