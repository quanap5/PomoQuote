using System.Globalization;
using System.Text.Json;

namespace PomoQuote.AiUsage;

/// <summary>What was understood from the Grok CLI's billing answer: the plan name as the CLI shows it, and the one usage window.</summary>
internal sealed record GrokBilling(string PlanTier, IReadOnlyList<QuotaWindow> Windows);

/// <summary>
/// Reads the result of the Grok CLI's billing request: `config` { `creditUsagePercent` (0 to 100), `currentPeriod` { `type`, `start`, `end` },
/// `prepaidBalance`, ... } and `subscription_tier`. The shape comes from the CLI's open-source code and a real answer, not from a documented contract,
/// so unknown and missing fields are tolerated and only what is needed is checked. One window: its length from the period type (weekly is 10080
/// minutes; a monthly or other period takes its real length from its start and end), percent used from `creditUsagePercent`, reset from the period end.
/// </summary>
internal static class GrokBillingParser
{
    public static GrokBilling Parse(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) throw new UsageParseException("The answer is not an object.");
        if (!result.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object) throw new UsageParseException("The answer has no config.");

        var percent = Percent(config) ?? throw new UsageParseException("The answer has no usage percent.");

        JsonElement period = default;
        var hasPeriod = config.TryGetProperty("currentPeriod", out period) && period.ValueKind == JsonValueKind.Object;
        var start = Time(hasPeriod ? Text(period, "start") : "") ?? Time(Text(config, "billingPeriodStart"));
        var end = Time(hasPeriod ? Text(period, "end") : "") ?? Time(Text(config, "billingPeriodEnd"));
        var type = hasPeriod ? Text(period, "type").ToUpperInvariant() : "";

        int? minutes = type.Contains("WEEK") ? AgyUsageParser.WeeklyMinutes
            : type.Contains("DAY") ? 1440
            : start is { } s && end is { } e && e > s ? (int)Math.Round((e - s).TotalMinutes)
            : null;

        var plan = "";
        foreach (var name in new[] { "subscription_tier", "subscriptionTier" })
        {
            if (result.TryGetProperty(name, out var tier) && tier.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tier.GetString()))
            {
                plan = tier.GetString()!.Trim();
                break;
            }
        }

        var window = new QuotaWindow(QuotaWindow.KindFor(minutes), minutes, percent, end, "", end is not null);
        return new GrokBilling(plan, [window]);
    }

    /// <summary>`creditUsagePercent`, else used divided by the limit of the deprecated fields (in cents); null when neither is there. Clamped to 0 to 100.</summary>
    private static double? Percent(JsonElement config)
    {
        if (config.TryGetProperty("creditUsagePercent", out var percent) && percent.ValueKind == JsonValueKind.Number)
        {
            var value = percent.GetDouble();
            if (!double.IsNaN(value) && !double.IsInfinity(value)) return Math.Clamp(value, 0, 100);
        }

        var used = Cents(config, "used");
        var limit = Cents(config, "monthlyLimit");
        return used is { } u && limit is { } l && l > 0 ? Math.Clamp(u * 100.0 / l, 0, 100) : null;
    }

    /// <summary>A `Cent` value ({ "val": n }); a value that is omitted because it is zero counts as 0 once the object is there.</summary>
    private static long? Cents(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var cent) || cent.ValueKind != JsonValueKind.Object) return null;
        return cent.TryGetProperty("val", out var val) && val.ValueKind == JsonValueKind.Number && val.TryGetInt64(out var number) ? number : 0;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static DateTimeOffset? Time(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
}