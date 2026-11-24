using System.Text.Json;
using System.Text.Json.Serialization;

namespace PomoQuote.AiUsage.Claude;

/// <summary>Where a Claude snapshot came from.</summary>
public enum ClaudeSnapshotSource
{
    StatusLine,
    ManualRefresh,
}

/// <summary>One quota window in a snapshot file: kind, percent used and the reset time. Nothing else.</summary>
internal sealed record ClaudeSnapshotWindow(QuotaKind Kind, double PercentUsed, DateTimeOffset? ResetsAt);

/// <summary>
/// The redacted record the bridge (or a manual refresh) leaves for PomoQuote. Only these fields exist: no email, no
/// session id text, no transcript path, no working directory, no token, no cost. The session is a salted hash.
/// </summary>
/// <param name="WrittenAt">When the status line (or the refresh) ran.</param>
/// <param name="Source">Status line or manual refresh.</param>
/// <param name="Version">The Claude Code version string, or empty.</param>
/// <param name="SessionHash">Hash of the session id (so several sessions can be told apart), or "manual".</param>
/// <param name="HadApiResponse">Whether the session had received its first API reply (rate limits appear only after it).</param>
/// <param name="RateLimitsPresent">Whether the payload carried `rate_limits` at all.</param>
/// <param name="Windows">The five-hour and weekly windows that were present.</param>
internal sealed record ClaudeSnapshot(
    DateTimeOffset WrittenAt, ClaudeSnapshotSource Source, string Version, string SessionHash, bool HadApiResponse, bool RateLimitsPresent,
    IReadOnlyList<ClaudeSnapshotWindow> Windows);

/// <summary>
/// Reads and writes the snapshot files in %LOCALAPPDATA%\PomoQuote\usage, one per session hash
/// (claude-windows-&lt;sessionHash&gt;.json), written atomically. Files older than a week are deleted.
/// </summary>
internal sealed class ClaudeSnapshotStore
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly string _directory;

    public ClaudeSnapshotStore(string directory) => _directory = directory;

    /// <summary>%LOCALAPPDATA%\PomoQuote\usage, or the folder named by POMOQUOTE_USAGE_DIR (used by tests so they never touch the real one).</summary>
    public static string DefaultDirectory { get; } = Environment.GetEnvironmentVariable("POMOQUOTE_USAGE_DIR") is { Length: > 0 } overridden
        ? overridden
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PomoQuote", "usage");

    /// <summary>The property names a snapshot file may contain (the privacy test compares against this).</summary>
    internal static readonly string[] AllowedKeys =
    [
        "schema", "provider", "environment", "writtenAt", "source", "version", "sessionHash", "hadApiResponse", "rateLimitsPresent",
        "windows", "kind", "percentUsed", "resetsAt",
    ];

    public string DirectoryPath => _directory;

    public void Write(ClaudeSnapshot snapshot)
    {
        Directory.CreateDirectory(_directory);
        var file = new SnapshotFile
        {
            WrittenAt = snapshot.WrittenAt,
            Source = snapshot.Source,
            Version = snapshot.Version,
            SessionHash = snapshot.SessionHash,
            HadApiResponse = snapshot.HadApiResponse,
            RateLimitsPresent = snapshot.RateLimitsPresent,
            Windows = snapshot.Windows.Select(w => new WindowFile { Kind = w.Kind, PercentUsed = w.PercentUsed, ResetsAt = w.ResetsAt }).ToList(),
        };
        AtomicJson.Save(PathFor(snapshot.SessionHash), file);
    }

    /// <summary>All readable snapshots, newest first. Damaged or unknown files are skipped.</summary>
    public IReadOnlyList<ClaudeSnapshot> ReadAll()
    {
        var list = new List<ClaudeSnapshot>();
        if (!Directory.Exists(_directory)) return list;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_directory, "claude-windows-*.json").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return list;
        }

        foreach (var path in files)
        {
            var file = AtomicJson.Load<SnapshotFile>(path, new SnapshotFile { Schema = -1 }, f => f.Schema == 1 && f.Windows is not null);
            if (file.Schema != 1) continue;
            var windows = file.Windows!
                .Where(w => Enum.IsDefined(w.Kind) && w.PercentUsed is >= 0 and <= 100)
                .Select(w => new ClaudeSnapshotWindow(w.Kind, w.PercentUsed, w.ResetsAt))
                .ToList();
            list.Add(new ClaudeSnapshot(file.WrittenAt, file.Source, file.Version ?? "", file.SessionHash ?? "", file.HadApiResponse, file.RateLimitsPresent, windows));
        }

        return list.OrderByDescending(s => s.WrittenAt).ToList();
    }

    /// <summary>Deletes snapshot files older than <see cref="MaxAge"/> (by the time written inside, or the file time when unreadable).</summary>
    public int DeleteOld(DateTimeOffset now)
    {
        if (!Directory.Exists(_directory)) return 0;
        var deleted = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "claude-windows-*.json"))
            {
                var file = AtomicJson.Load<SnapshotFile>(path, new SnapshotFile { Schema = -1 }, f => f.Schema == 1);
                var written = file.Schema == 1 ? file.WrittenAt : File.GetLastWriteTimeUtc(path);
                if (now - written <= MaxAge) continue;
                File.Delete(path);
                deleted++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleaning up is best effort.
        }

        return deleted;
    }

    private string PathFor(string sessionHash)
    {
        // The hash is hex; anything else is reduced so a file name can never escape the folder.
        var safe = new string(sessionHash.Where(char.IsLetterOrDigit).Take(32).ToArray());
        return Path.Combine(_directory, $"claude-windows-{(safe.Length > 0 ? safe : "unknown")}.json");
    }

    private sealed class SnapshotFile
    {
        [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
        [JsonPropertyName("provider")] public string Provider { get; set; } = "claude";
        [JsonPropertyName("environment")] public string Environment { get; set; } = "Windows";
        [JsonPropertyName("writtenAt")] public DateTimeOffset WrittenAt { get; set; }
        [JsonPropertyName("source")] public ClaudeSnapshotSource Source { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("sessionHash")] public string? SessionHash { get; set; }
        [JsonPropertyName("hadApiResponse")] public bool HadApiResponse { get; set; }
        [JsonPropertyName("rateLimitsPresent")] public bool RateLimitsPresent { get; set; }
        [JsonPropertyName("windows")] public List<WindowFile>? Windows { get; set; } = [];
    }

    private sealed class WindowFile
    {
        [JsonPropertyName("kind")] public QuotaKind Kind { get; set; }
        [JsonPropertyName("percentUsed")] public double PercentUsed { get; set; }
        [JsonPropertyName("resetsAt")] public DateTimeOffset? ResetsAt { get; set; }
    }
}
