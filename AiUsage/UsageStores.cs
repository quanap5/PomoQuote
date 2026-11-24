using System.Text.Json;
using System.Text.Json.Serialization;

namespace PomoQuote.AiUsage;

/// <summary>How the widget draws a window.</summary>
public enum UsageDisplayMode
{
    /// <summary>A percentage and a bar per window.</summary>
    Bars,

    /// <summary>Only the percentages, on one line.</summary>
    Percent,
}

/// <summary>The user's choices for AI usage, saved in %AppData%\PomoQuote\usage.json. Nothing in it is secret.</summary>
/// <param name="EnabledProviders">Ids of the providers to read (none by default: nothing runs until the user turns one on).</param>
/// <param name="Order">Provider ids in display order (unknown ids are ignored, missing ones are appended).</param>
/// <param name="IntervalMinutes">Minutes between reads of a provider.</param>
/// <param name="DisplayMode">Kept for old files; the widget always draws bars now (Compact or Expanded is <paramref name="Expanded"/>).</param>
/// <param name="AlertsEnabled">Whether a balloon is shown when a threshold is crossed.</param>
/// <param name="Thresholds">Percent-used levels that raise a warning, ascending.</param>
/// <param name="ClaudeRefreshAcknowledged">Whether the user has seen the note that "Refresh Claude now" spends a very small amount of quota.</param>
/// <param name="Expanded">True for the Expanded layout (every provider as a card), false for Compact (one provider at a time).</param>
/// <param name="SelectedProvider">The provider Compact shows (null: the first one).</param>
internal sealed record UsageSettings(
    IReadOnlyList<string> EnabledProviders,
    IReadOnlyList<string> Order,
    int IntervalMinutes,
    UsageDisplayMode DisplayMode,
    bool AlertsEnabled,
    IReadOnlyList<int> Thresholds,
    bool ClaudeRefreshAcknowledged = false,
    bool Expanded = false,
    string? SelectedProvider = null)
{
    public const int DefaultIntervalMinutes = 3;
    public const int MaxThresholds = 4;

    public static UsageSettings Default { get; } = new([], [CodexUsageProvider.ProviderId, "claude"], DefaultIntervalMinutes, UsageDisplayMode.Bars, true, [80, 95]);

    public TimeSpan Interval => TimeSpan.FromMinutes(IntervalMinutes);

    public bool IsEnabled(string providerId) => EnabledProviders.Contains(providerId);

    /// <summary>The settings with a provider switched on or off.</summary>
    public UsageSettings WithEnabled(string providerId, bool enabled) => this with
    {
        EnabledProviders = enabled
            ? EnabledProviders.Contains(providerId) ? EnabledProviders : [.. EnabledProviders, providerId]
            : EnabledProviders.Where(id => id != providerId).ToList(),
    };

    internal bool IsValid =>
        EnabledProviders is not null && Order is not null && Thresholds is not null &&
        IntervalMinutes is >= 1 and <= 60 &&
        Enum.IsDefined(DisplayMode) &&
        Thresholds.Count is >= 1 and <= MaxThresholds &&
        Thresholds.All(t => t is >= 1 and <= 100) &&
        Thresholds.Distinct().Count() == Thresholds.Count;

    /// <summary>The same settings with thresholds sorted ascending.</summary>
    public UsageSettings Normalized() => this with { Thresholds = Thresholds.OrderBy(t => t).ToList() };
}

/// <summary>Reads and writes JSON files atomically (temp file and rename). A missing or damaged file gives the fallback.</summary>
internal static class AtomicJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string path, T fallback, Func<T, bool> isValid) where T : class
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            return value is not null && isValid(value) ? value : fallback;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return fallback;
        }
    }

    public static void Save<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>Stores <see cref="UsageSettings"/> in usage.json.</summary>
internal sealed class UsageSettingsStore
{
    private readonly string _path;

    public UsageSettingsStore(string path) => _path = path;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PomoQuote", "usage.json");

    public UsageSettings Load() => AtomicJson.Load(_path, UsageSettings.Default, settings => settings.IsValid).Normalized();

    public void Save(UsageSettings settings) => AtomicJson.Save(_path, settings.Normalized());
}

/// <summary>
/// The last good reading of each provider, kept so the widget is not empty after a restart. Only these fields are
/// ever written (the privacy test checks the file): provider, environment, hashed account key, plan, observed time,
/// state and the windows with kind, length, percent, reset, bucket and reset-is-real. No ids, emails or tokens.
/// </summary>
internal sealed class UsageCacheStore
{
    private readonly string _path;

    public UsageCacheStore(string path) => _path = path;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PomoQuote", "usage-cache.json");

    public IReadOnlyList<UsageSnapshot> Load()
    {
        var file = AtomicJson.Load(_path, new CacheFile(), f => f.Entries is not null);
        var list = new List<UsageSnapshot>();
        foreach (var entry in file.Entries)
        {
            if (entry.Provider is not { Length: > 0 } || !Enum.IsDefined(entry.Environment) || !Enum.IsDefined(entry.DataSource) || entry.Windows is null) continue;
            var windows = entry.Windows
                .Where(w => Enum.IsDefined(w.Kind) && w.PercentUsed is >= 0 and <= 100)
                .Select(w => new QuotaWindow(w.Kind, w.DurationMinutes, w.PercentUsed, w.ResetsAt, w.Bucket ?? "", w.IsResetReal))
                .ToList();
            var source = new UsageSource(entry.Provider, entry.Environment, entry.AccountKey ?? "", entry.PlanTier ?? "");
            list.Add(new UsageSnapshot(source, windows, entry.ObservedAt, entry.DataSource, UsageState.Ok));
        }

        return list;
    }

    /// <summary>Saves the last good snapshot of each provider (snapshots that are not Ok are not stored).</summary>
    public void Save(IEnumerable<UsageSnapshot> lastGood)
    {
        var entries = lastGood.Where(s => s.State == UsageState.Ok).Select(s => new CacheEntry
        {
            Provider = s.Source.ProviderId,
            Environment = s.Source.Environment,
            AccountKey = s.Source.AccountKey,
            PlanTier = s.Source.PlanTier,
            ObservedAt = s.ObservedAt,
            DataSource = s.DataSource,
            State = UsageState.Ok,
            Windows = s.Windows.Select(w => new CacheWindow
            {
                Kind = w.Kind,
                DurationMinutes = w.DurationMinutes,
                PercentUsed = w.PercentUsed,
                ResetsAt = w.ResetsAt,
                Bucket = w.Bucket,
                IsResetReal = w.IsResetReal,
            }).ToList(),
        }).ToList();
        AtomicJson.Save(_path, new CacheFile { Entries = entries });
    }

    /// <summary>The property names the cache file may contain (the privacy test compares against this).</summary>
    internal static readonly string[] AllowedKeys =
    [
        "version", "entries", "provider", "environment", "accountKey", "planTier", "observedAt", "dataSource", "state",
        "windows", "kind", "durationMinutes", "percentUsed", "resetsAt", "bucket", "isResetReal",
    ];

    private sealed class CacheFile
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("entries")] public List<CacheEntry> Entries { get; set; } = [];
    }

    private sealed class CacheEntry
    {
        [JsonPropertyName("provider")] public string? Provider { get; set; }
        [JsonPropertyName("environment")] public UsageEnvironment Environment { get; set; }
        [JsonPropertyName("accountKey")] public string? AccountKey { get; set; }
        [JsonPropertyName("planTier")] public string? PlanTier { get; set; }
        [JsonPropertyName("observedAt")] public DateTimeOffset ObservedAt { get; set; }
        [JsonPropertyName("dataSource")] public UsageDataSource DataSource { get; set; }
        [JsonPropertyName("state")] public UsageState State { get; set; }
        [JsonPropertyName("windows")] public List<CacheWindow>? Windows { get; set; }
    }

    private sealed class CacheWindow
    {
        [JsonPropertyName("kind")] public QuotaKind Kind { get; set; }
        [JsonPropertyName("durationMinutes")] public int? DurationMinutes { get; set; }
        [JsonPropertyName("percentUsed")] public double PercentUsed { get; set; }
        [JsonPropertyName("resetsAt")] public DateTimeOffset? ResetsAt { get; set; }
        [JsonPropertyName("bucket")] public string? Bucket { get; set; }
        [JsonPropertyName("isResetReal")] public bool IsResetReal { get; set; }
    }
}
