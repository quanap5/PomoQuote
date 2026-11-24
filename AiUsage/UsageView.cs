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
internal sealed record UsageRow(string Label, string Percent, double Fraction, string Reset, UsageLevel Level);

/// <summary>One provider as the widget shows it.</summary>
/// <param name="Name">Provider name.</param>
/// <param name="Plan">Plan tier ("plus") or empty.</param>
/// <param name="Rows">Window rows (empty when there is no data).</param>
/// <param name="Note">Updated time, stale marker or the state message (never empty).</param>
/// <param name="IsProblem">True when the note is a warning (stale data, error, not installed, not logged in, unsupported).</param>
/// <param name="ShortNote">A shorter form of the note that keeps the time of the data (used when the note does not fit the widget width).</param>
internal sealed record UsageProviderView(string Name, string Plan, IReadOnlyList<UsageRow> Rows, string Note, bool IsProblem, string? ShortNote = null)
{
    /// <summary>The short form, or the note itself when there is none.</summary>
    public string Short => ShortNote ?? Note;
}

/// <summary>Turns provider statuses into what the widget draws. Pure: time and time zone are passed in, so it is easy to test.</summary>
internal static class UsageViewBuilder
{
    public static UsageProviderView Build(UsageStatus status, UsageSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        var shown = status.Shown;
        var latest = status.Latest;
        var rows = shown is null ? [] : Rows(shown, settings, now);
        var plan = shown?.Source.PlanTier ?? "";

        var effective = latest?.EffectiveState(now, settings.Interval);
        string note;
        string shortNote;
        bool problem;
        switch (effective)
        {
            case null or UsageState.Waiting:
                (note, problem) = shown is null ? ("Reading...", false) : ($"updated {Clock(shown.ObservedAt, zone)}", false);
                shortNote = note;
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
                    UsageState.Unsupported => "unsupported version",
                    _ => "read failed",
                };
                note = shown is null ? why : $"{why}; data from {Clock(shown.ObservedAt, zone)}";
                var shortWhy = latest.State switch { UsageState.Unsupported => "unsupported", _ => why };
                shortNote = shown is null ? shortWhy : $"{shortWhy} · {Clock(shown.ObservedAt, zone)}";
                problem = true;
                break;
        }

        return new UsageProviderView(status.DisplayName, plan, rows, note, problem, shortNote);
    }

    /// <summary>Lines the strip needs for these providers (the header and either the rows or one line for the note).</summary>
    public static int LineCount(IReadOnlyList<UsageProviderView> views, UsageDisplayMode mode) =>
        views.Sum(view => mode == UsageDisplayMode.Percent ? 1 : 1 + Math.Max(1, view.Rows.Count));

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
    private static List<UsageRow> Rows(UsageSnapshot snapshot, UsageSettings settings, DateTimeOffset now) =>
        snapshot.Windows
            .OrderBy(w => w.Kind)
            .Select(w => new UsageRow(Label(w), $"{Math.Round(w.PercentUsed):0}%", Math.Clamp(w.PercentUsed / 100, 0, 1), ResetText(w, now), LevelFor(w.PercentUsed, settings.Thresholds)))
            .ToList();

    internal static string Label(QuotaWindow window) => window.Kind switch
    {
        QuotaKind.FiveHour => "5h",
        QuotaKind.Weekly => "Week",
        _ => window.Bucket.Length > 0 ? window.Bucket : window.DurationMinutes is { } m ? $"{m}m" : "Other",
    };

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
