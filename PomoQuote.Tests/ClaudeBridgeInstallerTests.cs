using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PomoQuote.AiUsage.Claude;

namespace PomoQuote.Tests;

/// <summary>Every test works on throwaway files in a temp folder: the real ~/.claude/settings.json is never opened.</summary>
public sealed class ClaudeBridgeInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    private string Settings => Path.Combine(_root, "claude", "settings.json");

    private string State => Path.Combine(_root, "state");

    /// <summary>The folder the "running app" lives in: a fake PomoQuote.exe with the files the bridge copy needs.</summary>
    private string App => Path.Combine(_root, "app");

    private string AppExe => Path.Combine(App, "PomoQuote.exe");

    private string BridgeExe => Path.Combine(State, "bridge", "PomoQuote.exe");

    public ClaudeBridgeInstallerTests()
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

    private ClaudeBridgeInstaller Installer(bool bash = true) =>
        new(Settings, State, () => AppExe, () => bash ? @"C:\Git\bin\bash.exe" : null, () => new DateTimeOffset(2026, 11, 24, 7, 0, 0, TimeSpan.Zero));

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private const string Custom = "{\n  \"theme\": \"dark\",\n  \"statusLine\": { \"type\": \"command\", \"command\": \"node C:/t/line.js\", \"padding\": 1 },\n  \"model\": \"x\"\n}\n";

    [Fact]
    public void A_missing_settings_folder_is_unreadable_and_install_creates_nothing()
    {
        Directory.Delete(Path.GetDirectoryName(Settings)!);
        var installer = Installer();

        Assert.Equal(StatusLineState.Unreadable, installer.Detect().State);
        Assert.Equal(BridgeResult.Unreadable, installer.Install());
        Assert.False(File.Exists(Settings));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Settings)!));
        Assert.False(Directory.Exists(installer.BackupDirectory));
    }

    [Fact]
    public void Detect_never_writes_anything()
    {
        File.WriteAllText(Settings, Custom);
        var before = File.GetLastWriteTimeUtc(Settings);

        var info = Installer().Detect();
        Installer().Preview();
        Installer().Check();

        Assert.Equal(StatusLineState.Custom, info.State);
        Assert.Equal(before, File.GetLastWriteTimeUtc(Settings));
        Assert.Equal(Custom, File.ReadAllText(Settings));
        Assert.False(Directory.Exists(State));
    }

    [Fact]
    public void The_preview_shows_the_exact_new_command_and_where_the_backup_goes()
    {
        File.WriteAllText(Settings, Custom);

        var preview = Installer().Preview()!;

        Assert.Equal("node C:/t/line.js", preview.CurrentCommand);
        Assert.StartsWith("if [ -f \"", preview.NewCommand);
        Assert.Contains($"\"{BridgeExe.Replace('\\', '/')}\" --bridge claude --chain-b64 ", preview.NewCommand);
        Assert.Equal(BridgeExe, preview.ProgramPath);
        Assert.Equal(Installer().BackupDirectory, preview.BackupDirectory);
        Assert.Equal(Custom, File.ReadAllText(Settings)); // a preview changes nothing
    }

    [Fact]
    public void Installing_over_a_custom_status_line_backs_up_the_whole_file_first_and_keeps_every_other_key()
    {
        File.WriteAllText(Settings, Custom);
        var originalBytes = File.ReadAllBytes(Settings);

        var result = Installer().Install();

        Assert.Equal(BridgeResult.Done, result);
        var backup = Assert.Single(Directory.GetFiles(Installer().BackupDirectory));
        Assert.Equal(originalBytes, File.ReadAllBytes(backup));
        Assert.Contains(Sha(originalBytes)[..8], Path.GetFileName(backup));

        var info = Installer().Detect();
        Assert.Equal(StatusLineState.Bridged, info.State);
        var statusLine = JsonDocument.Parse(File.ReadAllText(Settings)).RootElement.GetProperty("statusLine");
        Assert.Equal(1, statusLine.GetProperty("padding").GetInt32());
        Assert.Equal("dark", JsonDocument.Parse(File.ReadAllText(Settings)).RootElement.GetProperty("theme").GetString());
        Assert.Equal("x", JsonDocument.Parse(File.ReadAllText(Settings)).RootElement.GetProperty("model").GetString());

        var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(State, "bridge-state.json"))).RootElement;
        Assert.Equal(Sha(originalBytes), state.GetProperty("originalSha256").GetString());
        Assert.Equal("node C:/t/line.js", state.GetProperty("originalCommand").GetString());
        Assert.False(File.Exists(Settings + ".pomoquote.tmp"));
    }

    [Fact]
    public void The_installed_command_carries_the_original_so_undo_works_without_the_state_file_or_the_program()
    {
        File.WriteAllText(Settings, Custom);
        Installer().Install();
        Directory.Delete(State, recursive: true); // the app's own files are gone

        var result = Installer().Uninstall();

        Assert.Equal(BridgeResult.Done, result);
        Assert.Equal(Custom, File.ReadAllText(Settings));
    }

    [Fact]
    public void Installing_when_there_is_no_status_line_adds_only_that_key_and_undo_removes_it()
    {
        const string none = "{\n  \"theme\": \"dark\"\n}\n";
        File.WriteAllText(Settings, none);

        Assert.Equal(BridgeResult.Done, Installer().Install());
        var command = JsonDocument.Parse(File.ReadAllText(Settings)).RootElement.GetProperty("statusLine").GetProperty("command").GetString()!;
        Assert.DoesNotContain("--chain-b64", command);

        Assert.Equal(BridgeResult.Done, Installer().Uninstall());
        Assert.Equal(none, File.ReadAllText(Settings));
        Assert.False(File.Exists(Path.Combine(State, "bridge-state.json")));
    }

    [Fact]
    public void A_second_install_does_nothing_and_a_backup_is_never_overwritten()
    {
        File.WriteAllText(Settings, Custom);
        Installer().Install();
        var afterFirst = File.ReadAllText(Settings);

        Assert.Equal(BridgeResult.AlreadyBridged, Installer().Install());
        Assert.Equal(afterFirst, File.ReadAllText(Settings));

        // Same second, same content: the second backup gets its own name.
        Installer().Uninstall();
        Installer().Install();
        var backups = Directory.GetFiles(Installer().BackupDirectory);
        Assert.Equal(3, backups.Length); // install, the copy taken before the undo, install again
    }

    [Fact]
    public void If_the_file_changes_between_the_backup_and_the_write_nothing_is_changed()
    {
        File.WriteAllText(Settings, Custom);
        var installer = Installer();
        installer.AfterBackup = () => File.WriteAllText(Settings, Custom.Replace("dark", "light")); // Claude Code rewrote it

        var result = installer.Install();

        Assert.Equal(BridgeResult.ChangedMeanwhile, result);
        Assert.Equal(Custom.Replace("dark", "light"), File.ReadAllText(Settings));
    }

    [Fact]
    public void A_locked_file_gives_write_failed_without_changing_it()
    {
        File.WriteAllText(Settings, Custom);
        using var hold = new FileStream(Settings, FileMode.Open, FileAccess.Read, FileShare.Read); // readable, not writable

        var result = Installer().Install();

        Assert.Equal(BridgeResult.WriteFailed, result);
        hold.Dispose();
        Assert.Equal(Custom, File.ReadAllText(Settings));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ // c\n}")]
    [InlineData("{ \"statusLine\": 5 }")]
    public void Unreadable_files_are_never_touched_and_get_no_backup(string content)
    {
        File.WriteAllText(Settings, content);

        Assert.Equal(BridgeResult.Unreadable, Installer().Install());

        Assert.Equal(content, File.ReadAllText(Settings));
        Assert.False(Directory.Exists(Installer().BackupDirectory));
    }

    [Fact]
    public void Undo_says_not_ours_when_the_status_line_was_changed_by_the_user_afterwards()
    {
        File.WriteAllText(Settings, Custom);
        Installer().Install();
        File.WriteAllText(Settings, Custom.Replace("node C:/t/line.js", "node D:/other.js"));
        var edited = File.ReadAllText(Settings);

        Assert.Equal(BridgeResult.NotOurs, Installer().Uninstall());

        Assert.Equal(edited, File.ReadAllText(Settings));
    }

    [Fact]
    public void Undo_keeps_edits_made_after_the_install()
    {
        File.WriteAllText(Settings, Custom);
        Installer().Install();
        File.WriteAllText(Settings, File.ReadAllText(Settings).Replace("\"theme\": \"dark\"", "\"theme\": \"light\""));

        Installer().Uninstall();

        Assert.Equal(Custom.Replace("dark", "light"), File.ReadAllText(Settings));
    }

    [Fact]
    public void A_damaged_chain_is_restored_from_the_saved_state_or_left_alone()
    {
        File.WriteAllText(Settings, Custom);
        Installer().Install();
        var good = File.ReadAllText(Settings);
        var text = System.Text.RegularExpressions.Regex.Replace(good, "--chain-b64 [A-Za-z0-9+/=]+", "--chain-b64 !!!");
        File.WriteAllText(Settings, text);

        Assert.Equal(BridgeResult.Done, Installer().Uninstall()); // the state file still has the original command
        Assert.Equal("node C:/t/line.js", JsonDocument.Parse(File.ReadAllText(Settings)).RootElement.GetProperty("statusLine").GetProperty("command").GetString());

        File.WriteAllText(Settings, Custom);
        Installer().Install();
        File.WriteAllText(Settings, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Settings), "--chain-b64 [A-Za-z0-9+/=]+", "--chain-b64 !!!"));
        Directory.Delete(State, recursive: true);
        var before = File.ReadAllText(Settings);

        Assert.Equal(BridgeResult.Unreadable, Installer().Uninstall()); // no copy of the original anywhere: change nothing
        Assert.Equal(before, File.ReadAllText(Settings));
    }

    [Fact]
    public void A_byte_order_mark_and_crlf_line_endings_survive_install_and_undo()
    {
        var original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("{\r\n  \"theme\": \"dark\"\r\n}\r\n")).ToArray();
        File.WriteAllBytes(Settings, original);

        Installer().Install();
        var installed = File.ReadAllBytes(Settings);
        Installer().Uninstall();

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, installed[..3]);
        Assert.Equal(original, File.ReadAllBytes(Settings));
    }

    [Fact]
    public void The_powershell_form_is_used_when_there_is_no_git_bash()
    {
        File.WriteAllText(Settings, Custom);

        var preview = Installer(bash: false).Preview()!;

        Assert.StartsWith("if (Test-Path -LiteralPath '", preview.NewCommand);
        Assert.Contains("& '" + BridgeExe.Replace('\\', '/') + "' --bridge claude", preview.NewCommand);
    }

    [Fact]
    public void Check_reports_a_missing_bridge_program_and_a_project_that_overrides_the_status_line()
    {
        File.WriteAllText(Settings, Custom);
        Installer().Install();
        Directory.Delete(Path.Combine(State, "bridge"), recursive: true); // the bridge program is gone
        var project = Path.Combine(_root, "proj");
        Directory.CreateDirectory(Path.Combine(project, ".claude"));
        File.WriteAllText(Path.Combine(project, ".claude", "settings.json"), "{ \"statusLine\": { \"command\": \"own\" } }");

        var status = Installer().Check([project]);

        Assert.Equal(StatusLineState.Bridged, status.State);
        Assert.Contains(status.Problems, p => p.Contains("bridge program is missing"));
        Assert.True(status.ProgramMissing);
        Assert.True(status.NeedsAttention);
        Assert.Contains(status.Problems, p => p.Contains("proj") && p.Contains("own status line"));
    }

    [Fact]
    public void Check_is_quiet_when_everything_is_in_place()
    {
        File.WriteAllText(Settings, Custom);
        var installer = Installer(bash: true);
        installer.Install();

        var status = installer.Check();
        Assert.Empty(status.Problems);
        Assert.False(status.NeedsAttention);
    }

    [Fact]
    public void Command_parts_can_be_taken_apart_again()
    {
        var command = ClaudeBridgeCommand.Build(@"C:\Program Files\PomoQuote\PomoQuote.exe", "echo \"hi there\" | cat", bash: true);

        Assert.Equal(@"C:\Program Files\PomoQuote\PomoQuote.exe", ClaudeBridgeCommand.ExecutableOf(command));
        Assert.Equal("echo \"hi there\" | cat", ClaudeBridge.ChainFrom(ClaudeBridgeCommand.Arguments(command)));
        Assert.True(ClaudeBridgeCommand.HasChainArgument(command));
        Assert.DoesNotContain(" ", ClaudeBridgeCommand.Arguments(command).First(a => a.Length > 20 && a != "--chain-b64")); // the chain is one shell-safe token

        var powershell = ClaudeBridgeCommand.Build(@"C:\Program Files\PomoQuote\PomoQuote.exe", "echo \"hi there\" | cat", bash: false);
        Assert.Equal(@"C:\Program Files\PomoQuote\PomoQuote.exe", ClaudeBridgeCommand.ExecutableOf(powershell));
        Assert.Equal("echo \"hi there\" | cat", ClaudeBridge.ChainFrom(ClaudeBridgeCommand.Arguments(powershell)));
        Assert.True(ClaudeBridgeCommand.IsBashForm(command));
        Assert.False(ClaudeBridgeCommand.IsBashForm(powershell));

        // The form an earlier development build wrote (no fallback) is still understood.
        var legacy = "\"C:/Apps/PomoQuote.exe\" --bridge claude --chain-b64 " + ClaudeBridge.EncodeChain("node x.js");
        Assert.Equal(@"C:\Apps\PomoQuote.exe", ClaudeBridgeCommand.ExecutableOf(legacy));
        Assert.Equal("node x.js", ClaudeBridge.ChainFrom(ClaudeBridgeCommand.Arguments(legacy)));
        Assert.True(ClaudeBridgeCommand.IsBashForm(legacy));
        Assert.False(ClaudeBridgeCommand.IsBashForm("& " + legacy));
    }
}
