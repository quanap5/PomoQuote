using System.Text.Json;

namespace PomoQuote.AiUsage;

/// <summary>What was understood from a Codex `account/rateLimits/read` answer. The raw account id is hashed here and goes no further.</summary>
internal sealed record CodexRateLimits(string PlanTier, string AccountKey, IReadOnlyList<QuotaWindow> Windows);

/// <summary>The answer is not in a shape this version of PomoQuote understands.</summary>
internal sealed class UsageParseException(string message) : Exception(message);

/// <summary>
/// Reads the `result` of Codex's `account/rateLimits/read`: `rateLimits` with `primary` and `secondary` windows
/// (`usedPercent`, `windowDurationMins`, `resetsAt` in epoch seconds), `planType` and the account id. Unknown fields
/// are ignored, nulls are accepted where the protocol allows them, and the fields PomoQuote needs are checked.
/// </summary>
internal static class CodexRateLimitsParser
{
    public static CodexRateLimits Parse(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) throw new UsageParseException("The answer is not an object.");
        if (!result.TryGetProperty("rateLimits", out var limits) || limits.ValueKind != JsonValueKind.Object)
        {
            throw new UsageParseException("The answer has no rateLimits.");
        }

        var bucket = limits.TryGetProperty("limitId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : "";
        var windows = new List<QuotaWindow>();
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) continue;
            if (ReadWindow(window, bucket) is { } parsed) windows.Add(parsed);
        }

        // A reply with the object but neither window, or windows without a percentage, is not usable.
        if (windows.Count == 0) throw new UsageParseException("The answer has no usable window.");

        var plan = limits.TryGetProperty("planType", out var planElement) && planElement.ValueKind == JsonValueKind.String
            ? planElement.GetString() ?? ""
            : "";
        var account = result.TryGetProperty("accountId", out var accountElement) && accountElement.ValueKind == JsonValueKind.String
            ? AccountKey.From(accountElement.GetString())
            : "";
        return new CodexRateLimits(plan, account, windows);
    }

    private static QuotaWindow? ReadWindow(JsonElement window, string bucket)
    {
        if (!window.TryGetProperty("usedPercent", out var percent) || percent.ValueKind != JsonValueKind.Number) return null;
        var used = Math.Clamp(percent.GetDouble(), 0, 100);

        int? minutes = window.TryGetProperty("windowDurationMins", out var duration) && duration.ValueKind == JsonValueKind.Number && duration.TryGetInt32(out var m) && m > 0
            ? m
            : null;

        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resetsAt", out var reset) && reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var epoch) && epoch > 0)
        {
            try
            {
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(epoch);
            }
            catch (ArgumentOutOfRangeException)
            {
                resetsAt = null;
            }
        }

        // Codex reports a real window end whenever it reports one at all.
        return new QuotaWindow(QuotaWindow.KindFor(minutes), minutes, used, resetsAt, bucket, resetsAt is not null);
    }
}
