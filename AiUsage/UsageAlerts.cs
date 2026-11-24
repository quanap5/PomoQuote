using System.Text.Json.Serialization;

namespace PomoQuote.AiUsage;

/// <summary>A warning to show: a window crossed a threshold.</summary>
/// <param name="ProviderId">Which provider.</param>
/// <param name="Kind">Which window.</param>
/// <param name="Threshold">The level crossed (percent used).</param>
/// <param name="PercentUsed">The current percent used.</param>
/// <param name="ResetsAt">When the window resets.</param>
internal sealed record UsageAlert(string ProviderId, QuotaKind Kind, int Threshold, double PercentUsed, DateTimeOffset ResetsAt);

/// <summary>
/// Decides which warnings to raise. A warning fires once per (provider, account, window kind, threshold, window):
/// the window is identified by its reset time. It is re-armed when the window resets (a new reset time) or usage
/// falls back below the threshold. Windows whose reset time is not real never alert. The fired keys are persisted
/// by <see cref="UsageAlertStore"/> so a restart does not repeat a warning.
/// </summary>
internal sealed class UsageAlertEngine
{
    private readonly HashSet<string> _fired;

    public UsageAlertEngine(IEnumerable<string>? fired = null) => _fired = new HashSet<string>(fired ?? []);

    /// <summary>The keys that have fired (for persistence).</summary>
    public IReadOnlyCollection<string> FiredKeys => _fired;

    /// <summary>Evaluates a good snapshot. Returns the warnings to show (the highest crossed threshold per window) and whether the fired set changed.</summary>
    public (IReadOnlyList<UsageAlert> Alerts, bool Changed) Evaluate(UsageSnapshot snapshot, IReadOnlyList<int> thresholds)
    {
        var alerts = new List<UsageAlert>();
        var changed = false;
        if (snapshot.State != UsageState.Ok) return (alerts, false);

        foreach (var window in snapshot.Windows)
        {
            if (!window.IsResetReal || window.ResetsAt is not { } resetsAt) continue;

            var windowId = (resetsAt.ToUnixTimeSeconds() / 60).ToString();
            var highest = 0;
            foreach (var threshold in thresholds.OrderBy(t => t))
            {
                var prefix = $"{snapshot.Source.Key}|{window.Kind}|{threshold}|";
                var key = prefix + windowId;

                // Re-arm: a different window has fired before (this one is new), or usage is back below the level.
                changed |= _fired.RemoveWhere(k => k.StartsWith(prefix, StringComparison.Ordinal) && k != key) > 0;
                if (window.PercentUsed < threshold)
                {
                    changed |= _fired.Remove(key);
                    continue;
                }

                if (_fired.Add(key))
                {
                    changed = true;
                    highest = threshold;
                }
            }

            if (highest > 0) alerts.Add(new UsageAlert(snapshot.Source.ProviderId, window.Kind, highest, window.PercentUsed, resetsAt));
        }

        return (alerts, changed);
    }
}

/// <summary>Stores the fired alert keys in usage-alerts.json (atomic write, defaults on a damaged file).</summary>
internal sealed class UsageAlertStore
{
    private readonly string _path;

    public UsageAlertStore(string path) => _path = path;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PomoQuote", "usage-alerts.json");

    public IReadOnlyList<string> Load() => AtomicJson.Load(_path, new AlertFile(), f => f.Fired is not null).Fired;

    public void Save(IEnumerable<string> fired) => AtomicJson.Save(_path, new AlertFile { Fired = fired.OrderBy(k => k, StringComparer.Ordinal).ToList() });

    private sealed class AlertFile
    {
        [JsonPropertyName("fired")] public List<string> Fired { get; set; } = [];
    }
}
