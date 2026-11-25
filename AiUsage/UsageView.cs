namespace PomoQuote.AiUsage;

/// <summary>How a row of the strip is coloured.</summary>
public enum UsageLevel
{
    Normal,
    Warning,
    Critical,
}

/// <summary>One window as the widget shows it.</summary>
/// <param name="Label">"5h", "Week" or the bucket name.</param>
/// <param name="Percent">"21%".</param>
/// <param name="Fraction">0 to 1, for the bar.</param>
/// <param name="Reset">"in 3h 12m", "due", "?" or empty when the reset is only a placeholder.</param>
/// <param name="Level">Colour class from the warning thresholds.</param>
/// <param name="Stale">The numbers are older than twice the refresh interval (an icon is drawn, not only a colour).</param>
/// <param name="Tip">The tooltip: used, left and the exact reset time.</param>
internal sealed record UsageRow(string Label, string Percent, double Fraction, string Reset, UsageLevel Level, bool Stale = false, string Tip = "");

/// <summary>What the badge on a provider's logo says (the most serious thing first).</summary>
public enum ProviderSeverity
{
    None,
    NearLimit,
    Stale,
    Error,
}

/// <summary>One provider as the widget shows it.</summary>
/// <param name="Name">Provider name.</param>
/// <param name="Plan">Plan tier ("plus") or empty.</param>
/// <param name="Rows">Window rows (empty when there is no data).</param>
/// <param name="Note">Updated time, stale marker or the state message (never empty).</param>
/// <param name="IsProblem">True when the note is a warning (stale data, error, not installed, not logged in, unsupported).</param>
/// <param name="ShortNote">A shorter form of the note that keeps the time of the data (used when the note does not fit the widget width).</param>
/// <param name="Severity">The badge on the logo: error, stale data or a window near its limit.</param>
/// <param name="Source">"Windows" or "WSL", or empty when nothing was read yet.</param>
/// <param name="Tip">The logo tooltip: name, plan, source, time of the update and the state.</param>
/// <param name="HasData">Whether any window was read (otherwise the widget shows N/A).</param>
/// <param name="Refreshing">This provider is being read right now (its last numbers stay on screen; only its own mark shows it).</param>
internal sealed record UsageProviderView(string Name, string Plan, IReadOnlyList<UsageRow> Rows, string Note, bool IsProblem, string? ShortNote = null, string Id = "", ProviderSeverity Severity = ProviderSeverity.None, string Source = "", string Tip = "", bool Refreshing = false)
{
    /// <summary>The short form, or the note itself when there is none.</summary>
    public string Short => ShortNote ?? Note;

    /// <summary>Whether any window was read; otherwise the widget shows N/A (never 0%).</summary>
    public bool HasData => Rows.Count > 0;
}

/// <summary>Turns provider statuses into what the widget draws. Pure: time and time zone are passed in, so it is easy to test.</summary>
internal static class UsageViewBuilder
{
    public static UsageProviderView Build(UsageStatus status, UsageSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        var shown = status.Shown;
        var latest = status.Latest;
        // Antigravity and Grok are read every 5 minutes whatever the setting says, so their numbers are old only after twice that.
        var fixedPoll = status.ProviderId == AntigravityUsageProvider.ProviderId ? AntigravityUsageProvider.Poll : status.ProviderId == GrokUsageProvider.ProviderId ? GrokUsageProvider.Poll : (TimeSpan?)null;
        var interval = fixedPoll is { } poll && settings.Interval < poll ? poll : settings.Interval;
        var effective = latest?.EffectiveState(now, interval);

        // The numbers on screen are from an earlier good read while the latest read says something else (no plan limits, no reply, not
        // set up, a failure): they are kept, but never silently: icon, amber note and badge say they are old.
        var oldNumbers = shown is { Windows.Count: > 0 } && latest is not null && latest.State != UsageState.Ok;
        var rows = shown is null ? [] : Rows(shown, settings, now, zone, effective == UsageState.Stale || oldNumbers);
        var groups = shown is null ? "" : GroupDetail(shown);
        var plan = shown?.Source.PlanTier ?? "";

        string note;
        string shortNote;
        bool problem;
        switch (effective)
        {
            case null or UsageState.Waiting:
                (note, problem) = shown is null ? (latest is { ErrorText.Length: > 0 } ? latest.ErrorText : "Reading...", false)
                    : oldNumbers ? ($"no reply; data from {Clock(shown.ObservedAt, zone)}", true) : ($"updated {Clock(shown.ObservedAt, zone)}", false);
                shortNote = oldNumbers && shown is not null ? $"no reply · {Clock(shown.ObservedAt, zone)}" : note;
                break;
            case UsageState.NotSetUp:
                (note, problem) = (shown is null ? "not set up" : $"not set up; data from {Clock(shown.ObservedAt, zone)}", oldNumbers);
                shortNote = shown is null ? note : $"not set up · {Clock(shown.ObservedAt, zone)}";
                break;
            case UsageState.NoPlanLimits:
                (note, problem) = (shown is null ? "no plan limits" : $"no plan limits; data from {Clock(shown.ObservedAt, zone)}", oldNumbers);
                shortNote = shown is null ? note : $"no plan limits · {Clock(shown.ObservedAt, zone)}";
                break;
            case UsageState.Ok:
                (note, problem) = ($"updated {Clock(latest!.ObservedAt, zone)}", false);
                shortNote = note;
                break;
            case UsageState.Stale:
                (note, problem) = ($"stale, from {Clock(latest!.ObservedAt, zone)}", true);
                shortNote = $"stale · {Clock(latest.ObservedAt, zone)}";
                break;
            default:
                // A failed read: say why, and say when the numbers on screen are from.
                var why = latest!.State switch
                {
                    UsageState.NotInstalled => "not installed",
                    UsageState.NotLoggedIn => "not logged in",
                    UsageState.BridgeMissing => "bridge missing",
                    UsageState.Unsupported => "unsupported version",
                    _ => "read failed",
                };
                note = shown is null ? why : $"{why}; data from {Clock(shown.ObservedAt, zone)}";
                var shortWhy = latest.State switch { UsageState.Unsupported => "unsupported", _ => why };
                shortNote = shown is null ? shortWhy : $"{shortWhy} · {Clock(shown.ObservedAt, zone)}";
                problem = true;
                break;
        }

        var severity = effective switch
        {
            UsageState.Stale => ProviderSeverity.Stale,
            UsageState.Ok or null or UsageState.Waiting or UsageState.NotSetUp or UsageState.NoPlanLimits => rows.Any(r => r.Level != UsageLevel.Normal) ? ProviderSeverity.NearLimit : ProviderSeverity.None,
            _ => ProviderSeverity.Error,
        };
        if (oldNumbers && severity is ProviderSeverity.None or ProviderSeverity.NearLimit) severity = ProviderSeverity.Stale;
        var source = shown is null ? "" : shown.Source.Environment.ToString() + (groups.Length > 0 ? " · " + groups : "");
        var tip = new List<string> { status.DisplayName + (plan.Length > 0 ? $" ({plan})" : "") };
        if (source.Length > 0) tip.Add("Source: " + source);
        tip.Add(note.Length > 0 ? char.ToUpperInvariant(note[0]) + note[1..] : note);
        if (status.Refreshing) tip.Add("Updating...");
        if (rows.Any(r => r.Level == UsageLevel.Critical)) tip.Add("A window is almost used up.");
        else if (rows.Any(r => r.Level == UsageLevel.Warning)) tip.Add("A window is getting full.");

        return new UsageProviderView(status.DisplayName, plan, rows, note, problem, shortNote, status.ProviderId, severity, source, string.Join("\n", tip), status.Refreshing);
    }

    /// <summary>
    /// The note text that fits <paramref name="maxWidth"/>: the full note, else the short one, else the short one with the start
    /// cut and an ellipsis so that the time at its end (" · hh:mm") is always kept. Last resort: the time alone.
    /// </summary>
    public static string FitNote(UsageProviderView view, float maxWidth, Func<string, float> measure)
    {
        if (measure(view.Note) <= maxWidth) return view.Note;
        if (measure(view.Short) <= maxWidth) return view.Short;

        var separator = view.Short.LastIndexOf(" · ", StringComparison.Ordinal);
        if (separator < 0) return Truncate(view.Short, maxWidth, measure);

        var head = view.Short[..separator];
        var tail = view.Short[separator..];
        while (head.Length > 0)
        {
            head = head[..^1];
            var candidate = head.TrimEnd() + "…" + tail;
            if (measure(candidate) <= maxWidth) return candidate;
        }

        var timeOnly = tail.TrimStart(' ', '·');
        return measure(timeOnly) <= maxWidth ? timeOnly : Truncate(timeOnly, maxWidth, measure);
    }

    private static string Truncate(string text, float maxWidth, Func<string, float> measure)
    {
        while (text.Length > 1 && measure(text + "…") > maxWidth) text = text[..^1];
        return text.Length == 0 ? text : text + "…";
    }
    private static List<UsageRow> Rows(UsageSnapshot snapshot, UsageSettings settings, DateTimeOffset now, TimeZoneInfo zone, bool stale)
    {
        var grouped = snapshot.Source.ProviderId == AntigravityUsageProvider.ProviderId;
        var windows = grouped ? MostUsedPerKind(snapshot.Windows) : snapshot.Windows;
        return windows
            .OrderBy(w => w.Kind)
            .Select(w => new UsageRow(Label(w), $"{Math.Round(w.PercentUsed):0}%", Math.Clamp(w.PercentUsed / 100, 0, 1), ResetText(w, now), LevelFor(w.PercentUsed, settings.Thresholds), stale, grouped ? GroupedRowTip(w, snapshot.Windows, now, zone, stale) : RowTip(w, now, zone, stale)))
            .ToList();
    }

    /// <summary>Antigravity reports one limit per model group: the widget shows one row per window length, for the group that is most used (the one that will run out first).</summary>
    internal static IReadOnlyList<QuotaWindow> MostUsedPerKind(IReadOnlyList<QuotaWindow> windows) =>
        windows
            .GroupBy(w => (w.Kind, w.DurationMinutes))
            .Select(g => g.OrderByDescending(w => w.PercentUsed).ThenBy(w => w.ResetsAt ?? DateTimeOffset.MaxValue).First())
            .ToList();

    /// <summary>The group names with their percent used, for the Expanded source line and the tooltips: "Gemini 33%, Claude and GPT 0%" (empty unless there are two or more groups).</summary>
    internal static string GroupDetail(UsageSnapshot snapshot)
    {
        if (snapshot.Source.ProviderId != AntigravityUsageProvider.ProviderId) return "";
        var parts = snapshot.Windows
            .Where(w => w.Bucket.Length > 0)
            .GroupBy(w => w.Bucket)
            .Select(g => $"{ShortGroup(g.Key)} {Math.Round(g.Max(w => w.PercentUsed)):0}%")
            .ToList();
        return parts.Count > 1 ? string.Join(", ", parts) : "";
    }

    internal static string ShortGroup(string name) => name.EndsWith(" models", StringComparison.OrdinalIgnoreCase) ? name[..^7].TrimEnd() : name;

    /// <summary>The tooltip of the one row: the most used group first, then every group's own used percent and reset time.</summary>
    internal static string GroupedRowTip(QuotaWindow shown, IReadOnlyList<QuotaWindow> all, DateTimeOffset now, TimeZoneInfo zone, bool stale)
    {
        var tip = shown.Bucket.Length > 0 ? $"{Label(shown)}, {ShortGroup(shown.Bucket)} (the most used): " + RowTip(shown, now, zone, stale)[(Label(shown).Length + 2)..] : RowTip(shown, now, zone, stale);
        var others = all.Where(w => w.Kind == shown.Kind && w.DurationMinutes == shown.DurationMinutes && !ReferenceEquals(w, shown) && w.Bucket.Length > 0).ToList();
        if (others.Count == 0) return tip;
        var lines = others.Select(w => $"{ShortGroup(w.Bucket)}: used {Math.Round(w.PercentUsed):0}%" + (w.IsResetReal && w.ResetsAt is { } at ? (at <= now ? ", reset was due" : $", resets in {ResetText(w, now)}") : "") + ".");
        return tip + "\n" + string.Join("\n", lines);
    }

    /// <summary>The window name as the data says it: "5h" and "Week" for those lengths, else the length itself ("3h", "2d") or the limit name; never forced into 5 hours or a week.</summary>
    internal static string Label(QuotaWindow window) => window.Kind switch
    {
        QuotaKind.FiveHour => "5h",
        QuotaKind.Weekly => "Week",
        _ => window.DurationMinutes is { } m && window.Bucket.Length == 0 ? Duration(m) : window.Bucket.Length > 0 ? window.Bucket : "Other",
    };

    internal static string Duration(int minutes) => minutes % 10080 == 0 ? $"{minutes / 10080}w"
        : minutes % 1440 == 0 ? $"{minutes / 1440}d"
        : minutes % 60 == 0 ? $"{minutes / 60}h"
        : $"{minutes}m";

    /// <summary>"Used 42%, left 58%. Resets Fri 27 Nov 18:25 (in 3d 1h)." (the exact time in the user's zone).</summary>
    internal static string RowTip(QuotaWindow window, DateTimeOffset now, TimeZoneInfo zone, bool stale)
    {
        var used = Math.Round(window.PercentUsed);
        var text = $"{Label(window)}: used {used:0}%, left {100 - used:0}%.";
        if (window.IsResetReal && window.ResetsAt is { } at)
        {
            var local = TimeZoneInfo.ConvertTime(at, zone);
            text += at <= now ? $" Reset was due {local:ddd d MMM HH:mm}." : $" Resets {local:ddd d MMM HH:mm} (in {ResetText(window, now)}).";
        }
        else
        {
            text += " The reset time is not known.";
        }

        return stale ? text + " These numbers are old." : text;
    }

    /// <summary>The reset as a countdown from the clock: "3h 12m", "2d 4h", "under 1m", "due" once past, "?" when unknown, empty when only a placeholder.</summary>
    internal static string ResetText(QuotaWindow window, DateTimeOffset now)
    {
        if (!window.IsResetReal) return "";
        if (window.ResetsAt is not { } at) return "?";
        if (at <= now) return "due";

        var left = at - now;
        if (left.TotalMinutes < 1) return "<1m";
        if (left.TotalHours < 1) return $"{(int)left.TotalMinutes}m";
        if (left.TotalDays < 1) return $"{(int)left.TotalHours}h {left.Minutes:00}m";
        return $"{(int)left.TotalDays}d {left.Hours}h";
    }

    internal static UsageLevel LevelFor(double percent, IReadOnlyList<int> thresholds)
    {
        var sorted = thresholds.OrderBy(t => t).ToList();
        if (sorted.Count == 0) return UsageLevel.Normal;
        if (percent >= sorted[^1]) return UsageLevel.Critical;
        return percent >= sorted[0] ? UsageLevel.Warning : UsageLevel.Normal;
    }

    private static string Clock(DateTimeOffset time, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(time, zone).ToString("HH:mm");
}
