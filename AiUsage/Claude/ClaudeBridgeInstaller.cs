using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PomoQuote.AiUsage.Claude;

/// <summary>How an install, undo or check ended.</summary>
public enum BridgeResult
{
    Done,
    AlreadyBridged,

    /// <summary>The file is missing, damaged, locked or has a status line PomoQuote will not touch; nothing was changed.</summary>
    Unreadable,

    /// <summary>The file changed between the backup and the write; nothing was changed.</summary>
    ChangedMeanwhile,

    /// <summary>The file could not be written (locked); nothing was changed.</summary>
    WriteFailed,

    /// <summary>Undo only: the status line is no longer ours; nothing was changed.</summary>
    NotOurs,

    /// <summary>Install only: this is not PomoQuote.exe (for example `dotnet PomoQuote.dll`), or its files cannot be copied to the bridge folder; nothing was changed.</summary>
    WrongProgram,
}

/// <summary>What the Set up dialog shows before anything is written.</summary>
/// <param name="State">None or Custom (anything else cannot be installed).</param>
/// <param name="CurrentCommand">The current status line command, or null when there is none.</param>
/// <param name="NewCommand">The command that would be written.</param>
/// <param name="BackupDirectory">Where the backup of the whole file will be kept.</param>
/// <param name="ProgramPath">The copy of the bridge program the command will run (a stable folder, not the folder the app is started from).</param>
/// <param name="NoFileYet">There is no settings file yet: it would be created with only the status line.</param>
/// <param name="IsUpgrade">An old-style bridge (no fail-safe) would be rewritten to the new form; the original command inside it stays as it is.</param>
internal sealed record BridgePreview(StatusLineState State, string? CurrentCommand, string NewCommand, string BackupDirectory, string ProgramPath = "", bool NoFileYet = false, bool IsUpgrade = false);

/// <summary>What Settings shows about the bridge.</summary>
/// <param name="State">The state of the user-level file.</param>
/// <param name="Reason">Why it is unreadable, else empty.</param>
/// <param name="Problems">Things the Check found (bridge program missing, damaged chain, wrong shell, project overrides, ...).</param>
/// <param name="ProgramMissing">The set-up bridge program (or its saved original command) is gone or damaged: the strip says "bridge missing".</param>
/// <param name="ShellMismatch">The command is written for a different shell than Claude Code would use now.</param>
/// <param name="OldStyle">The bridge was written by an earlier development build (before 1.2.0): it runs the program by an absolute path with no fail-safe, so a moved or deleted program takes the status line down. Settings offers an upgrade.</param>
internal sealed record BridgeStatus(StatusLineState State, string Reason, IReadOnlyList<string> Problems, bool ProgramMissing = false, bool ShellMismatch = false, bool OldStyle = false)
{
    /// <summary>Whether the user should be told about the bridge now (not project overrides, which are only listed by a manual check).</summary>
    public bool NeedsAttention => State == StatusLineState.Bridged && (ProgramMissing || ShellMismatch || OldStyle);
}

/// <summary>
/// Installs and removes the status line bridge in the user-level Claude settings file, safely: it only ever works on the
/// file path it is given (tests use throwaway files), makes a backup first, refuses when the file changed in between,
/// patches only the text that has to change, and never installs by itself. Undo works from the file alone (the chain is
/// inside the command), so it works even if the bridge program or this app's state files are gone.
/// </summary>
internal sealed class ClaudeBridgeInstaller
{
    private readonly string _settingsPath;
    private readonly string _stateDirectory;
    private readonly Func<string> _bridgeExecutable;
    private readonly Func<string?> _gitBash;
    private readonly Func<DateTimeOffset> _now;
    private readonly string _bridgeDirectory;

    /// <summary>The only program file name a bridge command may run.</summary>
    public const string ProgramName = "PomoQuote.exe";

    /// <param name="bridgeExecutable">The running PomoQuote.exe; its files are copied to <paramref name="bridgeDirectory"/> and the status line runs that copy.</param>
    /// <param name="bridgeDirectory">The stable folder of the copy (default: "bridge" inside <paramref name="stateDirectory"/>).</param>
    public ClaudeBridgeInstaller(string settingsPath, string stateDirectory, Func<string> bridgeExecutable, Func<string?>? gitBash = null, Func<DateTimeOffset>? now = null, string? bridgeDirectory = null)
    {
        _settingsPath = settingsPath;
        _stateDirectory = stateDirectory;
        _bridgeExecutable = bridgeExecutable;
        _gitBash = gitBash ?? (() => ClaudeShell.FindGitBash());
        _now = now ?? (() => DateTimeOffset.Now);
        _bridgeDirectory = bridgeDirectory ?? Path.Combine(stateDirectory, "bridge");
    }

    /// <summary>Environment variable that names the settings file to work on (tests, QA and relocated configs); it wins over everything else.</summary>
    public const string SettingsOverrideVariable = "POMOQUOTE_CLAUDE_SETTINGS";

    /// <summary>Environment variable that moves PomoQuote's own state (backups, bridge copy, bridge-state.json); used by tests and QA.</summary>
    public const string StateOverrideVariable = "POMOQUOTE_STATE_DIR";

    public static string DefaultSettingsPath { get; } = ResolveSettingsPath(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string DefaultStateDirectory { get; } = Environment.GetEnvironmentVariable(StateOverrideVariable) is { Length: > 0 } state
        ? state
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PomoQuote");

    /// <summary>The settings file: the override variable, else CLAUDE_CONFIG_DIR (which Claude Code honours), else the user profile's .claude folder.</summary>
    public static string ResolveSettingsPath(Func<string, string?> env, string userProfile)
    {
        if (env(SettingsOverrideVariable) is { Length: > 0 } explicitPath) return explicitPath;
        if (env("CLAUDE_CONFIG_DIR") is { Length: > 0 } configDirectory) return Path.Combine(configDirectory, "settings.json");
        return Path.Combine(userProfile, ".claude", "settings.json");
    }

    /// <summary>The folder with the copy of the bridge program the status line runs.</summary>
    public string BridgeDirectory => _bridgeDirectory;

    /// <summary>The copy of PomoQuote.exe the status line runs.</summary>
    public string ProgramPath => Path.Combine(_bridgeDirectory, ProgramName);

    /// <summary>Test seam: runs between the backup and the check that the file is unchanged.</summary>
    internal Action? AfterBackup { get; set; }

    /// <summary>The settings file this installer works on.</summary>
    public string SettingsPath => _settingsPath;

    public string BackupDirectory => Path.Combine(_stateDirectory, "bridge-backups");

    private string StatePath => Path.Combine(_stateDirectory, "bridge-state.json");

    // ---------- reading ----------

    /// <summary>The state of the file; read only. A settings folder that exists but has no file yet (a new Claude Code profile) counts as "no status line".</summary>
    public StatusLineInfo Detect()
    {
        var text = TryRead(out var reason);
        if (text is not null) return ClaudeSettingsPatcher.Inspect(text);
        return NoFileYet() ? new StatusLineInfo(StatusLineState.None, null) : new StatusLineInfo(StatusLineState.Unreadable, null, reason);
    }

    private bool NoFileYet() => !File.Exists(_settingsPath) && Directory.Exists(Path.GetDirectoryName(_settingsPath));

    /// <summary>Status plus the problems the Check action reports.</summary>
    public BridgeStatus Check(IEnumerable<string>? projectDirectories = null)
    {
        var info = Detect();
        var problems = new List<string>();
        var programMissing = false;
        var shellMismatch = false;
        var oldStyle = false;
        if (info.State == StatusLineState.Bridged)
        {
            oldStyle = ClaudeBridgeCommand.IsOldStyle(info.Command!);
            var exe = ClaudeBridgeCommand.ExecutableOf(info.Command!);
            if (exe is null || !File.Exists(exe))
            {
                programMissing = true;
                problems.Add(oldStyle
                    ? "The bridge program is missing and this old-style bridge has no fail-safe, so Claude Code runs the missing program and shows no status line at all. Upgrade the bridge (it is then repaired) or remove it."
                    : "The bridge program is missing, so Claude Code now runs your original status line directly (if you had none, it shows nothing). Remove the bridge and set it up again.");
            }
            else if (oldStyle)
            {
                problems.Add("This is an old-style bridge without a fail-safe: if PomoQuote.exe is ever moved or deleted, your status line disappears. Upgrade the bridge to the new form (your original command is kept).");
            }

            if (ClaudeBridge.ChainFrom(ClaudeBridgeCommand.Arguments(info.Command!)) is null && ClaudeBridgeCommand.HasChainArgument(info.Command!))
            {
                programMissing = true;
                problems.Add("The saved original status line command is damaged.");
            }

            var bash = _gitBash() is not null;
            if (ClaudeBridgeCommand.IsBashForm(info.Command!) != bash)
            {
                shellMismatch = true;
                problems.Add(bash
                    ? "Git Bash is installed now, but the bridge was written for PowerShell. Remove the bridge and set it up again."
                    : "Git Bash was not found, but the bridge was written for it (Claude Code now uses PowerShell). Remove the bridge and set it up again.");
            }
        }

        foreach (var directory in projectDirectories ?? [])
        {
            if (ProjectOverrides(directory)) problems.Add($"The project \"{Path.GetFileName(directory.TrimEnd('\\', '/'))}\" has its own status line, which replaces this one there.");
        }

        return new BridgeStatus(info.State, info.Reason, problems, programMissing, shellMismatch, oldStyle);
    }

    /// <summary>Whether a project folder's own settings define a `statusLine` (that overrides the user-level one inside that project).</summary>
    public static bool ProjectOverrides(string projectDirectory)
    {
        foreach (var name in new[] { "settings.json", "settings.local.json" })
        {
            try
            {
                var path = Path.Combine(projectDirectory, ".claude", name);
                if (File.Exists(path) && ClaudeSettingsPatcher.Inspect(File.ReadAllText(path)).State is StatusLineState.Custom or StatusLineState.Bridged) return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable project settings are not an override we can see.
            }
        }

        return false;
    }

    /// <summary>Why the bridge cannot be installed from this program (not PomoQuote.exe, or its file is gone), else null.</summary>
    public string? CannotInstallReason()
    {
        string source;
        try
        {
            source = _bridgeExecutable();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return "PomoQuote cannot tell which program file it runs from.";
        }

        if (!string.Equals(Path.GetFileName(source), ProgramName, StringComparison.OrdinalIgnoreCase))
        {
            return $"PomoQuote is running as \"{Path.GetFileName(source)}\", not as {ProgramName} (for example through `dotnet`). The status line can only run {ProgramName}; start the app from {ProgramName} to set the bridge up.";
        }

        if (!File.Exists(source)) return $"{ProgramName} was not found next to the running app.";
        if (!ClaudeBridgeCommand.IsSafePath(ProgramPath)) return $"The bridge folder path contains a character that cannot be used in a shell command: {_bridgeDirectory}";
        return null;
    }

    /// <summary>The exact change an install would make, or null when there is nothing to install (bridged, unreadable, or not run from PomoQuote.exe).</summary>
    public BridgePreview? Preview()
    {
        var info = Detect();
        if (info.State is not (StatusLineState.None or StatusLineState.Custom)) return null;
        if (CannotInstallReason() is not null) return null;
        return new BridgePreview(info.State, info.Command, ClaudeBridgeCommand.Build(ProgramPath, info.Command, _gitBash() is not null), BackupDirectory, ProgramPath, NoFileYet());
    }

    /// <summary>The exact change an upgrade of an old-style bridge would make, or null when there is nothing to upgrade.</summary>
    public BridgePreview? PreviewUpgrade()
    {
        var info = Detect();
        if (info.State != StatusLineState.Bridged || !ClaudeBridgeCommand.IsOldStyle(info.Command!) || CannotInstallReason() is not null) return null;
        var chain = ClaudeBridge.ChainFrom(ClaudeBridgeCommand.Arguments(info.Command!));
        if (chain is null && ClaudeBridgeCommand.HasChainArgument(info.Command!)) return null; // a damaged chain is not guessed at
        return new BridgePreview(StatusLineState.Bridged, info.Command, ClaudeBridgeCommand.Build(ProgramPath, chain, _gitBash() is not null), BackupDirectory, ProgramPath, false, IsUpgrade: true);
    }

    // ---------- the stable copy of the bridge program ----------

    /// <summary>
    /// Copies the files the bridge needs (the app's .exe, .dll and .json files, not its assets) next to nothing else, into the
    /// bridge folder, so moving, deleting or updating the app's own folder cannot break the status line. Each file is
    /// copied beside its target and swapped in. False when something could not be copied.
    /// </summary>
    private bool CopyProgram()
    {
        var source = _bridgeExecutable();
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(source))!;
        if (string.Equals(Path.GetFullPath(sourceDirectory).TrimEnd('\\'), Path.GetFullPath(_bridgeDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true; // already running from there

        try
        {
            Directory.CreateDirectory(_bridgeDirectory);
            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                var extension = Path.GetExtension(file);
                if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(_bridgeDirectory, Path.GetFileName(file));
                if (SameFile(file, target)) continue;
                var temporary = target + ".new";
                File.Copy(file, temporary, overwrite: true);
                File.Move(temporary, target, overwrite: true);
            }

            return File.Exists(ProgramPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SameFile(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        return fb.Exists && fa.Length == fb.Length && fa.LastWriteTimeUtc == fb.LastWriteTimeUtc;
    }

    /// <summary>
    /// After an update of the app: when the bridge is set up and its copy exists but is older than the running program, refresh the
    /// copy. Only PomoQuote's own bridge folder is touched, never the Claude settings. A copy in use is left for next time.
    /// </summary>
    public bool RefreshProgramCopy()
    {
        try
        {
            if (Detect().State != StatusLineState.Bridged || !File.Exists(ProgramPath) || CannotInstallReason() is not null) return false;
            var source = _bridgeExecutable();
            if (SameFile(source, ProgramPath) && SameFile(Path.ChangeExtension(source, ".dll"), Path.ChangeExtension(ProgramPath, ".dll"))) return false;
            return CopyProgram();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    // ---------- writing (only on the user's explicit confirmation) ----------

    public BridgeResult Install()
    {
        if (CannotInstallReason() is not null) return BridgeResult.WrongProgram;

        var original = TryReadBytes();
        var noFileBefore = original is null && NoFileYet();
        if (original is null && !noFileBefore) return BridgeResult.Unreadable;
        var (text, bom) = original is null ? (NewFileText, false) : Decode(original);
        var info = ClaudeSettingsPatcher.Inspect(text);
        if (info.State == StatusLineState.Bridged) return BridgeResult.AlreadyBridged;
        if (info.State != StatusLineState.None && info.State != StatusLineState.Custom) return BridgeResult.Unreadable;

        if (!CopyProgram()) return BridgeResult.WrongProgram; // nothing in the Claude settings changed yet

        var command = ClaudeBridgeCommand.Build(ProgramPath, info.Command, _gitBash() is not null);
        var patched = ClaudeSettingsPatcher.Install(text, command);
        if (patched is null) return BridgeResult.Unreadable;

        var hash = original is null ? "" : Sha256(original);
        string backupFile;
        try
        {
            backupFile = original is null ? WriteNoFileRecord() : WriteBackup(original, hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BridgeResult.WriteFailed; // no backup, no change
        }

        AfterBackup?.Invoke();

        // The file must still be the one that was backed up (or still absent).
        var again = TryReadBytes();
        if (original is null ? again is not null : again is null || Sha256(again) != hash) return BridgeResult.ChangedMeanwhile;

        if (!TryWrite(Encode(patched, bom), overwrite: original is not null)) return original is null && File.Exists(_settingsPath) ? BridgeResult.ChangedMeanwhile : BridgeResult.WriteFailed;

        try
        {
            SaveState(new BridgeState { BackupFile = Path.GetFileName(backupFile), OriginalSha256 = hash, OriginalCommand = info.Command, InstalledAt = _now(), NoFileBefore = noFileBefore });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Undo does not need the state file: the original command is inside the new one.
        }

        return BridgeResult.Done;
    }

    /// <summary>
    /// Rewrites only the command of an old-style bridge to the new form: the program is copied to the stable folder, the same
    /// original command (byte for byte) travels inside, the file is backed up first and left alone when it changed meanwhile.
    /// </summary>
    public BridgeResult Upgrade()
    {
        if (CannotInstallReason() is not null) return BridgeResult.WrongProgram;
        var original = TryReadBytes();
        if (original is null) return BridgeResult.Unreadable;
        var (text, bom) = Decode(original);
        var info = ClaudeSettingsPatcher.Inspect(text);
        if (info.State == StatusLineState.Unreadable) return BridgeResult.Unreadable;
        if (info.State != StatusLineState.Bridged) return BridgeResult.NotOurs;
        if (!ClaudeBridgeCommand.IsOldStyle(info.Command!)) return BridgeResult.AlreadyBridged;

        var chain = ClaudeBridge.ChainFrom(ClaudeBridgeCommand.Arguments(info.Command!));
        if (chain is null && ClaudeBridgeCommand.HasChainArgument(info.Command!)) return BridgeResult.Unreadable;
        if (!CopyProgram()) return BridgeResult.WrongProgram;

        var patched = ClaudeSettingsPatcher.ReplaceBridgeCommand(text, ClaudeBridgeCommand.Build(ProgramPath, chain, _gitBash() is not null));
        if (patched is null) return BridgeResult.Unreadable;

        var hash = Sha256(original);
        try
        {
            WriteBackup(original, hash, "before-upgrade-");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BridgeResult.WriteFailed;
        }

        AfterBackup?.Invoke();
        var again = TryReadBytes();
        if (again is null || Sha256(again) != hash) return BridgeResult.ChangedMeanwhile;

        return TryWrite(Encode(patched, bom), overwrite: true) ? BridgeResult.Done : BridgeResult.WriteFailed;
    }

    private const string NewFileText = "{\n}\n";

    public BridgeResult Uninstall()
    {
        var current = TryReadBytes();
        if (current is null) return BridgeResult.Unreadable;
        var (text, bom) = Decode(current);
        var info = ClaudeSettingsPatcher.Inspect(text);
        if (info.State == StatusLineState.Unreadable) return BridgeResult.Unreadable;
        if (info.State != StatusLineState.Bridged) return BridgeResult.NotOurs;

        // The original command is inside the bridge command; the saved state is only a fallback.
        var state = LoadState();
        var original = ClaudeBridge.ChainFrom(ClaudeBridgeCommand.Arguments(info.Command!));
        if (original is null && ClaudeBridgeCommand.HasChainArgument(info.Command!))
        {
            // A chain is there but damaged: the saved state is the only other copy. Without it, change nothing
            // rather than risk removing the user's own status line.
            original = state?.OriginalCommand;
            if (original is null) return BridgeResult.Unreadable;
        }

        // No chain at all means the user had no status line before: undo removes the key.
        var restored = ClaudeSettingsPatcher.Uninstall(text, original);
        if (restored is null) return BridgeResult.Unreadable;

        // A fresh copy of the file as it is now, before anything is changed.
        try
        {
            WriteBackup(current, Sha256(current), "before-remove-");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BridgeResult.WriteFailed;
        }

        // A settings file PomoQuote created itself and that holds nothing else is removed again.
        if (state?.NoFileBefore == true && IsEmptyObject(restored))
        {
            if (!TryDelete()) return BridgeResult.WriteFailed;
        }
        else if (!TryWrite(Encode(restored, bom), overwrite: true))
        {
            return BridgeResult.WriteFailed;
        }

        try
        {
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover state file is harmless.
        }

        return BridgeResult.Done;
    }

    private static bool IsEmptyObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryDelete()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.Delete(_settingsPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(150);
            }
        }

        return false;
    }

    // ---------- file helpers ----------

    private string? TryRead(out string reason)
    {
        var bytes = TryReadBytes();
        if (bytes is null)
        {
            reason = File.Exists(_settingsPath) ? "the file is locked or cannot be read" : "the settings file does not exist yet";
            return null;
        }

        reason = "";
        return Decode(bytes).Text;
    }

    /// <summary>Reads the file, retrying a few times when it is briefly locked (Claude Code may be rewriting it).</summary>
    private byte[]? TryReadBytes()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return File.Exists(_settingsPath) ? File.ReadAllBytes(_settingsPath) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(150);
            }
        }

        return null;
    }

    private bool TryWrite(byte[] bytes, bool overwrite = true)
    {
        var temporary = _settingsPath + ".pomoquote.tmp";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, _settingsPath, overwrite);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(150);
            }
        }

        try
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind, harmless.
        }

        return false;
    }

    /// <summary>Copies the whole file into the backup folder under a new name; an existing backup is never overwritten.</summary>
    private string WriteBackup(byte[] original, string hash, string prefix = "")
    {
        Directory.CreateDirectory(BackupDirectory);
        var stamp = _now().ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(BackupDirectory, $"settings-{prefix}{stamp}-{hash[..8]}.json");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(BackupDirectory, $"settings-{prefix}{stamp}-{hash[..8]}-{n}.json");
        File.WriteAllBytes(path, original);
        return path;
    }

    /// <summary>The backup record for a profile that had no settings file: a note that says so (there is nothing to copy).</summary>
    private string WriteNoFileRecord()
    {
        Directory.CreateDirectory(BackupDirectory);
        var path = Path.Combine(BackupDirectory, $"settings-{_now():yyyyMMdd-HHmmss}-no-file-before.txt");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(BackupDirectory, $"settings-{_now():yyyyMMdd-HHmmss}-no-file-before-{n}.txt");
        File.WriteAllText(path, "There was no Claude settings file before PomoQuote created it with only the status line. Remove bridge deletes it again if it still holds nothing else.");
        return path;
    }

    private void SaveState(BridgeState state)
    {
        Directory.CreateDirectory(_stateDirectory);
        AtomicJson.Save(StatePath, state);
    }

    private BridgeState? LoadState()
    {
        var state = AtomicJson.Load(StatePath, new BridgeState { OriginalSha256 = "" }, s => s.OriginalSha256.Length > 0 || s.NoFileBefore);
        return state.OriginalSha256.Length > 0 || state.NoFileBefore ? state : null;
    }

    private static (string Text, bool Bom) Decode(byte[] bytes)
    {
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return (new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom);
    }

    private static byte[] Encode(string text, bool bom)
    {
        var body = new UTF8Encoding(false).GetBytes(text);
        return bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>What the installer remembers (nothing secret: a file name, a hash, the original command and the time).</summary>
    private sealed class BridgeState
    {
        [JsonPropertyName("backupFile")] public string BackupFile { get; set; } = "";
        [JsonPropertyName("originalSha256")] public string OriginalSha256 { get; set; } = "";
        [JsonPropertyName("originalCommand")] public string? OriginalCommand { get; set; }
        [JsonPropertyName("installedAt")] public DateTimeOffset InstalledAt { get; set; }
        [JsonPropertyName("noFileBefore")] public bool NoFileBefore { get; set; }
    }
}

/// <summary>Builds and takes apart the command line Claude Code runs as the status line.</summary>
internal static class ClaudeBridgeCommand
{
    /// <summary>
    /// The status line command, written so it still works when the bridge program is gone: it tests for the program and runs it
    /// (with the original command passed along as base64, so no quoting can go wrong), else it runs the original command itself
    /// with the same input (or does nothing when the user had none).
    /// Git Bash: <c>if [ -f "P" ]; then "P" --bridge claude --chain-b64 X; else eval "$(printf %s X | base64 -d)"; fi</c>
    /// PowerShell: <c>if (Test-Path -LiteralPath 'P' -PathType Leaf) { &amp; 'P' --bridge claude --chain-b64 X } else { Invoke-Expression ... }</c>
    /// </summary>
    public static string Build(string executable, string? originalCommand, bool bash)
    {
        var path = executable.Replace('\\', '/');
        var chain = originalCommand is null ? "" : " --chain-b64 " + ClaudeBridge.EncodeChain(originalCommand);
        if (bash)
        {
            var fallback = originalCommand is null ? ":" : $"eval \"$(printf %s {ClaudeBridge.EncodeChain(originalCommand)} | base64 -d)\"";
            return $"if [ -f \"{path}\" ]; then \"{path}\" {ClaudeSettingsPatcher.BridgeMarker}{chain}; else {fallback}; fi";
        }

        var powershellFallback = originalCommand is null ? "" : $" Invoke-Expression ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{ClaudeBridge.EncodeChain(originalCommand)}'))) ";
        return $"if (Test-Path -LiteralPath '{path}' -PathType Leaf) {{ & '{path}' {ClaudeSettingsPatcher.BridgeMarker}{chain} }} else {{{powershellFallback}}}";
    }

    /// <summary>Quotes, dollar signs, backticks, semicolons and line breaks cannot be put safely inside the quoted program path of either shell.</summary>
    public static bool IsSafePath(string path) => path.IndexOfAny(['"', '\'', '$', '`', ';', '\r', '\n', '%']) < 0;

    /// <summary>The fail-safe forms start with a test of the program file (Git Bash `if [ -f`, PowerShell `if (Test-Path`).</summary>
    public static bool HasFailSafe(string command)
    {
        var text = command.TrimStart();
        return text.StartsWith("if [ -f \"", StringComparison.Ordinal) || text.StartsWith("if (Test-Path ", StringComparison.Ordinal);
    }

    /// <summary>A bridge written by an earlier development build (before 1.2.0): the program by absolute path, no test, no fallback.</summary>
    public static bool IsOldStyle(string command) => ClaudeSettingsPatcher.IsBridge(command) && !HasFailSafe(command);

    /// <summary>Whether the command is written for Git Bash (else PowerShell). The first form PomoQuote wrote began with a quote (bash) or `&amp;` (PowerShell).</summary>
    public static bool IsBashForm(string command)
    {
        var text = command.TrimStart();
        if (text.StartsWith("if (", StringComparison.Ordinal) || text.StartsWith('&')) return false;
        return true;
    }

    /// <summary>The program path inside a bridge command (the quoted path right before the bridge marker), or null.</summary>
    public static string? ExecutableOf(string command)
    {
        var marker = command.IndexOf(ClaudeSettingsPatcher.BridgeMarker, StringComparison.Ordinal);
        if (marker < 3) return null;
        var end = marker - 1; // the space before the marker
        if (command[end] != ' ' || end < 1) return null;
        var quote = command[end - 1];
        if (quote is not ('"' or '\'')) return null;
        var start = command.LastIndexOf(quote, end - 2);
        return start >= 0 && end - 1 > start + 1 ? command[(start + 1)..(end - 1)].Replace('/', '\\') : null;
    }

    /// <summary>The words after the bridge marker, split on spaces and semicolons (the bridge's own arguments contain neither).</summary>
    public static IReadOnlyList<string> Arguments(string command)
    {
        var marker = command.IndexOf(ClaudeSettingsPatcher.BridgeMarker, StringComparison.Ordinal);
        if (marker < 0) return [];
        var rest = command[(marker + ClaudeSettingsPatcher.BridgeMarker.Length)..];
        return rest.Split([' ', ';'], StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool HasChainArgument(string command) => Arguments(command).Contains("--chain-b64");
}
