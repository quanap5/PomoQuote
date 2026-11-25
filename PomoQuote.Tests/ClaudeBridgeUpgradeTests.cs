using System.Text.Json;
using PomoQuote.AiUsage.Claude;

namespace PomoQuote.Tests;

/// <summary>
/// The bridge an earlier development build wrote (absolute program path, no test, no fallback) is recognised as unsafe, reported by what it
/// really does, and can be upgraded in place. Everything works on throwaway files.
/// </summary>
public sealed class ClaudeBridgeUpgradeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteUpgrade-" + Guid.NewGuid().ToString("N"));

    private string Settings => Path.Combine(_root, "claude", "settings.json");

    private string State => Path.Combine(_root, "state");

    private string App => Path.Combine(_root, "app");

    private string AppExe => Path.Combine(App, "PomoQuote.exe");

    private string OldExe => Path.Combine(_root, "old", "PomoQuote.exe");

    private const string Original = "node C:/t/line.js --flag \"a b\" | cat";

    public ClaudeBridgeUpgradeTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        Directory.CreateDirectory(App);
        Directory.CreateDirectory(Path.GetDirectoryName(OldExe)!);
        File.WriteAllText(AppExe, "exe");
        File.WriteAllText(Path.Combine(App, "PomoQuote.dll"), "dll");
        File.WriteAllText(OldExe, "old exe");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ClaudeBridgeInstaller Installer(bool bash = true) =>
        new(Settings, State, () => AppExe, () => bash ? @"C:\Git\bin\bash.exe" : null, () => new DateTimeOffset(2026, 11, 24, 7, 0, 0, TimeSpan.Zero));

    /// <summary>The command an earlier development build wrote: the quoted program path, the marker and the chain; with a leading `&amp;` for PowerShell.</summary>
    private string LegacyCommand(string? original, bool bash = true)
    {
        var core = $"\"{OldExe.Replace('\\', '/')}\" --bridge claude" + (original is null ? "" : " --chain-b64 " + ClaudeBridge.EncodeChain(original));
        return bash ? core : "& " + core;
    }

    /// <summary>A settings file with a tab-indented layout, other keys and CRLF line endings, as a real one might have.</summary>
    private string SettingsWith(string command) =>
        "{\r\n\t\"theme\": \"dark\",\r\n\t\"statusLine\": { \"type\": \"command\", \"command\": " + Lit(command) + ", \"padding\": 2 },\r\n\t\"model\": \"x\"\r\n}\r\n";

    private static string Lit(string value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // ---------- recognising the old form ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_old_forms_are_recognised_and_the_new_form_is_not(bool bash)
    {
        Assert.True(ClaudeBridgeCommand.IsOldStyle(LegacyCommand(Original, bash)));
        Assert.True(ClaudeBridgeCommand.IsOldStyle(LegacyCommand(null, bash)));
        Assert.False(ClaudeBridgeCommand.IsOldStyle(ClaudeBridgeCommand.Build(@"C:\x\PomoQuote.exe", Original, bash)));
        Assert.False(ClaudeBridgeCommand.IsOldStyle("node C:/t/line.js")); // not a bridge at all
    }

    [Fact]
    public void With_the_program_present_check_reports_an_old_style_bridge_without_saying_it_is_missing()
    {
        File.WriteAllText(Settings, SettingsWith(LegacyCommand(Original)));

        var status = Installer().Check();

        Assert.True(status.OldStyle);
        Assert.False(status.ProgramMissing);
        Assert.True(status.NeedsAttention);
        var problem = Assert.Single(status.Problems);
        Assert.Contains("old-style bridge without a fail-safe", problem);
        Assert.Contains("Upgrade", problem);
    }

    [Fact]
    public void With_the_program_gone_check_says_the_status_line_is_down_and_never_claims_the_original_still_runs()
    {
        File.WriteAllText(Settings, SettingsWith(LegacyCommand(Original)));
        File.Delete(OldExe);

        var status = Installer().Check();

        Assert.True(status.OldStyle);
        Assert.True(status.ProgramMissing);
        var text = string.Join(" ", status.Problems);
        Assert.Contains("shows no status line at all", text);
        Assert.DoesNotContain("runs your original status line directly", text);
    }

    [Fact]
    public void The_start_up_notice_is_worded_by_the_form_of_the_bridge()
    {
        var balloons = new List<string>();

        // Old style, program present.
        File.WriteAllText(Settings, SettingsWith(LegacyCommand(Original)));
        ClaudeActions.StartupCheck(Installer(), (_, m) => balloons.Add(m));
        // Old style, program gone: the original does NOT keep running.
        File.Delete(OldExe);
        ClaudeActions.StartupCheck(Installer(), (_, m) => balloons.Add(m));
        // New style, program gone: the original keeps running.
        File.WriteAllText(Settings, SettingsWith("node C:/t/line.js"));
        var installer = Installer();
        installer.Install();
        Directory.Delete(Path.Combine(State, "bridge"), recursive: true);
        ClaudeActions.StartupCheck(installer, (_, m) => balloons.Add(m));

        Assert.Equal(3, balloons.Count);
        Assert.Contains("old style", balloons[0]);
        Assert.Contains("Upgrade bridge", balloons[0]);
        Assert.Contains("shows no status line now", balloons[1]);
        Assert.DoesNotContain("still runs", balloons[1]);
        Assert.Contains("own status line still runs", balloons[2]);
    }

    [Fact]
    public void A_set_up_that_finds_an_old_style_bridge_says_already_set_up_and_changes_nothing()
    {
        var text = SettingsWith(LegacyCommand(Original));
        File.WriteAllText(Settings, text);

        Assert.Equal(BridgeResult.AlreadyBridged, Installer().Install());

        Assert.Equal(text, File.ReadAllText(Settings));
    }

    // ---------- the upgrade ----------

    [Fact]
    public void The_upgrade_preview_shows_the_current_and_the_new_command_and_changes_nothing()
    {
        var text = SettingsWith(LegacyCommand(Original));
        File.WriteAllText(Settings, text);

        var preview = Installer().PreviewUpgrade()!;

        Assert.True(preview.IsUpgrade);
        Assert.Equal(LegacyCommand(Original), preview.CurrentCommand);
        Assert.StartsWith("if [ -f \"", preview.NewCommand);
        Assert.Equal(Path.Combine(State, "bridge", "PomoQuote.exe"), preview.ProgramPath);
        Assert.Equal(Installer().BackupDirectory, preview.BackupDirectory);
        Assert.Equal(text, File.ReadAllText(Settings));
        Assert.False(Directory.Exists(State));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_upgrade_rewrites_only_the_command_keeps_the_original_byte_exact_and_backs_up_first(bool bash)
    {
        var before = SettingsWith(LegacyCommand(Original, bash));
        File.WriteAllText(Settings, before);
        var beforeBytes = File.ReadAllBytes(Settings);

        Assert.Equal(BridgeResult.Done, Installer(bash).Upgrade());

        var after = File.ReadAllText(Settings);
        var info = Installer(bash).Detect();
        Assert.Equal(StatusLineState.Bridged, info.State);
        Assert.False(ClaudeBridgeCommand.IsOldStyle(info.Command!));
        Assert.Equal(Original, ClaudeBridge.ChainFrom(ClaudeBridgeCommand.Arguments(info.Command!))); // byte-exact
        Assert.Equal(Path.Combine(State, "bridge", "PomoQuote.exe"), ClaudeBridgeCommand.ExecutableOf(info.Command!));
        Assert.True(File.Exists(Path.Combine(State, "bridge", "PomoQuote.exe")));

        // Only the command string changed: the same text with the old literal swapped for the new one.
        var oldLiteral = Lit(LegacyCommand(Original, bash));
        var newLiteral = Lit(info.Command!);
        Assert.Equal(before.Replace(oldLiteral, newLiteral), after);
        Assert.Contains("\"padding\": 2", after);
        Assert.Contains("\"theme\": \"dark\"", after);
        Assert.Contains("\r\n\t\"model\"", after);

        var backup = Assert.Single(Directory.GetFiles(Installer().BackupDirectory, "settings-before-upgrade-*"));
        Assert.Equal(beforeBytes, File.ReadAllBytes(backup));
    }

    [Fact]
    public void An_old_bridge_that_wrapped_no_original_is_upgraded_and_removing_it_then_removes_the_key()
    {
        File.WriteAllText(Settings, "{\n  \"theme\": \"dark\",\n  \"statusLine\": { \"type\": \"command\", \"command\": " + Lit(LegacyCommand(null)) + " }\n}\n");

        Assert.Equal(BridgeResult.Done, Installer().Upgrade());
        var command = Installer().Detect().Command!;
        Assert.DoesNotContain("--chain-b64", command);

        Assert.Equal(BridgeResult.Done, Installer().Uninstall());

        var root = JsonDocument.Parse(File.ReadAllText(Settings)).RootElement;
        Assert.False(root.TryGetProperty("statusLine", out _));
        Assert.Equal("dark", root.GetProperty("theme").GetString());
    }

    [Fact]
    public void Remove_after_an_upgrade_restores_the_exact_original_command_and_file()
    {
        File.WriteAllText(Settings, SettingsWith(Original)); // the user's own status line
        var originalFile = File.ReadAllText(Settings);
        // The 1.1 install: the same file with the command replaced by the old-style bridge.
        File.WriteAllText(Settings, originalFile.Replace(Lit(Original), Lit(LegacyCommand(Original))));

        Assert.Equal(BridgeResult.Done, Installer().Upgrade());
        Assert.Equal(BridgeResult.Done, Installer().Uninstall());

        Assert.Equal(originalFile, File.ReadAllText(Settings));
    }

    [Fact]
    public void If_the_file_changes_between_the_backup_and_the_write_the_upgrade_changes_nothing()
    {
        var text = SettingsWith(LegacyCommand(Original));
        File.WriteAllText(Settings, text);
        var installer = Installer();
        installer.AfterBackup = () => File.WriteAllText(Settings, text.Replace("dark", "light"));

        Assert.Equal(BridgeResult.ChangedMeanwhile, installer.Upgrade());

        Assert.Equal(text.Replace("dark", "light"), File.ReadAllText(Settings));
    }

    [Fact]
    public void A_damaged_saved_command_is_not_guessed_at()
    {
        var damaged = LegacyCommand(Original).Replace(ClaudeBridge.EncodeChain(Original), "!!!");
        File.WriteAllText(Settings, SettingsWith(damaged));
        var before = File.ReadAllBytes(Settings);

        Assert.Null(Installer().PreviewUpgrade());
        Assert.Equal(BridgeResult.Unreadable, Installer().Upgrade());

        Assert.Equal(before, File.ReadAllBytes(Settings));
    }

    [Fact]
    public void The_upgrade_does_nothing_when_there_is_nothing_to_upgrade()
    {
        File.WriteAllText(Settings, SettingsWith("node C:/t/line.js"));
        Assert.Equal(BridgeResult.NotOurs, Installer().Upgrade()); // not a bridge
        Assert.Null(Installer().PreviewUpgrade());

        Installer().Install();
        var installed = File.ReadAllText(Settings);
        Assert.Equal(BridgeResult.AlreadyBridged, Installer().Upgrade()); // already the new form
        Assert.Null(Installer().PreviewUpgrade());
        Assert.Equal(installed, File.ReadAllText(Settings));
    }

    [Fact]
    public void The_upgrade_is_refused_from_a_program_that_is_not_PomoQuote_exe_and_changes_nothing()
    {
        var text = SettingsWith(LegacyCommand(Original));
        File.WriteAllText(Settings, text);
        var dotnet = Path.Combine(App, "dotnet.exe");
        File.WriteAllText(dotnet, "x");

        var installer = new ClaudeBridgeInstaller(Settings, State, () => dotnet, () => null);

        Assert.Null(installer.PreviewUpgrade());
        Assert.Equal(BridgeResult.WrongProgram, installer.Upgrade());
        Assert.Equal(text, File.ReadAllText(Settings));
    }

    [Fact]
    public void After_the_upgrade_a_deleted_program_no_longer_takes_the_status_line_down_and_check_is_quiet_with_it_present()
    {
        File.WriteAllText(Settings, SettingsWith(LegacyCommand(Original)));
        Installer().Upgrade();

        Assert.Empty(Installer().Check().Problems);

        Directory.Delete(Path.GetDirectoryName(OldExe)!, recursive: true); // where the old command pointed
        Directory.Delete(App, recursive: true); // and the app's own folder
        Assert.Empty(Installer().Check().Problems);
    }

    // ---------- the short-lived cache ----------

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 7, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void A_cached_reading_is_shared_for_its_lifetime_and_dropped_on_invalidate()
    {
        var clock = new ManualClock();
        var reads = 0;
        var cache = new CachedValue<int>(() => ++reads, TimeSpan.FromSeconds(5), clock);

        Assert.Equal(1, cache.Get());
        Assert.Equal(1, cache.Get());
        clock.Now = clock.Now.AddSeconds(4);
        Assert.Equal(1, cache.Get());
        clock.Now = clock.Now.AddSeconds(2);
        Assert.Equal(2, cache.Get());
        cache.Invalidate();
        Assert.Equal(3, cache.Get());
        Assert.Equal(3, reads);
    }
}
