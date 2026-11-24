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
}

/// <summary>What the Set up dialog shows before anything is written.</summary>
/// <param name="State">None or Custom (anything else cannot be installed).</param>
/// <param name="CurrentCommand">The current status line command, or null when there is none.</param>
/// <param name="NewCommand">The command that would be written.</param>
/// <param name="BackupDirectory">Where the backup of the whole file will be kept.</param>
internal sealed record BridgePreview(StatusLineState State, string? CurrentCommand, string NewCommand, string BackupDirectory);

/// <summary>What Settings shows about the bridge.</summary>
/// <param name="State">The state of the user-level file.</param>
/// <param name="Reason">Why it is unreadable, else empty.</param>
/// <param name="Problems">Things the Check found (bridge program missing, damaged chain, project overrides, ...).</param>
internal sealed record BridgeStatus(StatusLineState State, string Reason, IReadOnlyList<string> Problems);

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

    public ClaudeBridgeInstaller(string settingsPath, string stateDirectory, Func<string> bridgeExecutable, Func<string?>? gitBash = null, Func<DateTimeOffset>? now = null)
    {
        _settingsPath = settingsPath;
        _stateDirectory = stateDirectory;
        _bridgeExecutable = bridgeExecutable;
        _gitBash = gitBash ?? (() => ClaudeShell.FindGitBash());
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public static string DefaultSettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string DefaultStateDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PomoQuote");

    /// <summary>Test seam: runs between the backup and the check that the file is unchanged.</summary>
    internal Action? AfterBackup { get; set; }

    /// <summary>The settings file this installer works on.</summary>
    public string SettingsPath => _settingsPath;

    public string BackupDirectory => Path.Combine(_stateDirectory, "bridge-backups");

    private string StatePath => Path.Combine(_stateDirectory, "bridge-state.json");

    // ---------- reading ----------

    /// <summary>The state of the file; read only.</summary>
    public StatusLineInfo Detect()
    {
        var text = TryRead(out var reason);
        return text is null ? new StatusLineInfo(StatusLineState.Unreadable, null, reason) : ClaudeSettingsPatcher.Inspect(text);
    }

    /// <summary>Status plus the problems the Check action reports.</summary>
    public BridgeStatus Check(IEnumerable<string>? projectDirectories = null)
    {
        var info = Detect();
        var problems = new List<string>();
        if (info.State == StatusLineState.Bridged)
        {
            var exe = ClaudeBridgeCommand.ExecutableOf(info.Command!);
            if (exe is null || !File.Exists(exe)) problems.Add("The bridge program is missing or was moved. Remove the bridge and set it up again.");
            if (ClaudeBridge.ChainFrom(ClaudeBridgeCommand.Arguments(info.Command!)) is null && ClaudeBridgeCommand.HasChainArgument(info.Command!))
            {
                problems.Add("The saved original status line command is damaged.");
            }
        }

        foreach (var directory in projectDirectories ?? [])
        {
            if (ProjectOverrides(directory)) problems.Add($"The project \"{Path.GetFileName(directory.TrimEnd('\\', '/'))}\" has its own status line, which replaces this one there.");
        }

        return new BridgeStatus(info.State, info.Reason, problems);
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

    /// <summary>The exact change an install would make, or null when there is nothing to install (bridged, or unreadable).</summary>
    public BridgePreview? Preview()
    {
        var info = Detect();
        if (info.State is not (StatusLineState.None or StatusLineState.Custom)) return null;
        return new BridgePreview(info.State, info.Command, ClaudeBridgeCommand.Build(_bridgeExecutable(), info.Command, _gitBash() is not null), BackupDirectory);
    }

    // ---------- writing (only on the user's explicit confirmation) ----------

    public BridgeResult Install()
    {
        var original = TryReadBytes();
        if (original is null) return BridgeResult.Unreadable;
        var (text, bom) = Decode(original);
        var info = ClaudeSettingsPatcher.Inspect(text);
        if (info.State == StatusLineState.Bridged) return BridgeResult.AlreadyBridged;
        if (info.State != StatusLineState.None && info.State != StatusLineState.Custom) return BridgeResult.Unreadable;

        var command = ClaudeBridgeCommand.Build(_bridgeExecutable(), info.Command, _gitBash() is not null);
        var patched = ClaudeSettingsPatcher.Install(text, command);
        if (patched is null) return BridgeResult.Unreadable;

        var hash = Sha256(original);
        string backupFile;
        try
        {
            backupFile = WriteBackup(original, hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BridgeResult.WriteFailed; // no backup, no change
        }

        AfterBackup?.Invoke();

        // The file must still be the one that was backed up.
        var again = TryReadBytes();
        if (again is null || Sha256(again) != hash) return BridgeResult.ChangedMeanwhile;

        if (!TryWrite(Encode(patched, bom))) return BridgeResult.WriteFailed;

        try
        {
            SaveState(new BridgeState { BackupFile = Path.GetFileName(backupFile), OriginalSha256 = hash, OriginalCommand = info.Command, InstalledAt = _now() });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Undo does not need the state file: the original command is inside the new one.
        }

        return BridgeResult.Done;
    }

    public BridgeResult Uninstall()
    {
        var current = TryReadBytes();
        if (current is null) return BridgeResult.Unreadable;
        var (text, bom) = Decode(current);
        var info = ClaudeSettingsPatcher.Inspect(text);
        if (info.State == StatusLineState.Unreadable) return BridgeResult.Unreadable;
        if (info.State != StatusLineState.Bridged) return BridgeResult.NotOurs;

        // The original command is inside the bridge command; the saved state is only a fallback.
        var original = ClaudeBridge.ChainFrom(ClaudeBridgeCommand.Arguments(info.Command!));
        if (original is null && ClaudeBridgeCommand.HasChainArgument(info.Command!))
        {
            // A chain is there but damaged: the saved state is the only other copy. Without it, change nothing
            // rather than risk removing the user's own status line.
            original = LoadState()?.OriginalCommand;
            if (original is null) return BridgeResult.Unreadable;
        }

        // No chain at all means the user had no status line before: undo removes the key.
        var restored = ClaudeSettingsPatcher.Uninstall(text, original);
        if (restored is null) return BridgeResult.Unreadable;

        if (!TryWrite(Encode(restored, bom))) return BridgeResult.WriteFailed;
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

    private bool TryWrite(byte[] bytes)
    {
        var temporary = _settingsPath + ".pomoquote.tmp";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, _settingsPath, overwrite: true);
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
    private string WriteBackup(byte[] original, string hash)
    {
        Directory.CreateDirectory(BackupDirectory);
        var stamp = _now().ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(BackupDirectory, $"settings-{stamp}-{hash[..8]}.json");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(BackupDirectory, $"settings-{stamp}-{hash[..8]}-{n}.json");
        File.WriteAllBytes(path, original);
        return path;
    }

    private void SaveState(BridgeState state)
    {
        Directory.CreateDirectory(_stateDirectory);
        AtomicJson.Save(StatePath, state);
    }

    private BridgeState? LoadState()
    {
        var state = AtomicJson.Load(StatePath, new BridgeState { OriginalSha256 = "" }, s => s.OriginalSha256.Length > 0);
        return state.OriginalSha256.Length > 0 ? state : null;
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
    }
}

/// <summary>Builds and takes apart the command line Claude Code runs as the status line.</summary>
internal static class ClaudeBridgeCommand
{
    /// <summary>
    /// `"C:/path/PomoQuote.exe" --bridge claude [--chain-b64 &lt;base64 of the original command&gt;]` for Git Bash, with a leading `&amp;`
    /// for PowerShell. The original command travels as base64 so no quoting of it can go wrong in either shell.
    /// </summary>
    public static string Build(string executable, string? originalCommand, bool bash)
    {
        var path = executable.Replace('\\', '/');
        var core = $"\"{path}\" {ClaudeSettingsPatcher.BridgeMarker}";
        if (originalCommand is not null) core += " --chain-b64 " + ClaudeBridge.EncodeChain(originalCommand);
        return bash ? core : "& " + core;
    }

    /// <summary>The executable path inside a bridge command, or null.</summary>
    public static string? ExecutableOf(string command)
    {
        var text = command.TrimStart();
        if (text.StartsWith('&')) text = text[1..].TrimStart();
        if (!text.StartsWith('"')) return null;
        var end = text.IndexOf('"', 1);
        return end > 1 ? text[1..end].Replace('/', '\\') : null;
    }

    /// <summary>The arguments after the executable, split on spaces (the bridge's own arguments contain none).</summary>
    public static IReadOnlyList<string> Arguments(string command)
    {
        var text = command.TrimStart();
        if (text.StartsWith('&')) text = text[1..].TrimStart();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end > 0) text = text[(end + 1)..];
        }

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool HasChainArgument(string command) => Arguments(command).Contains("--chain-b64");
}
