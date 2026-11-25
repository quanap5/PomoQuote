using System.Globalization;
using System.Text.Json;

namespace PomoQuote.AiUsage;

/// <summary>
/// The answer is not the report of the usage command: another command ran, or the CLI says a model was used. Nothing is read from it,
/// and the provider stops asking (a read that might spend quota must never be repeated).
/// </summary>
internal sealed class AgyGuardException(string message) : Exception(message);

/// <summary>The CLI itself says the run failed (not signed in, or another failure). Only the classification is kept: the text may name the account.</summary>
internal sealed class AgyReportedErrorException(bool signedOut) : Exception("The CLI reported an error.")
{
    public bool SignedOut { get; } = signedOut;
}

/// <summary>
/// Reads the JSON that `agy -p /usage --output-format json` prints: an envelope with `status`, `num_turns`, `usage.total_tokens` and
/// `command` { `name`, `data` { `groups` [ { `name`, `buckets` [ { `window`, `remaining_fraction`, `reset_time` } ] } ] } }. The shape is
/// not a documented contract, so unknown and missing fields are tolerated and only what is needed is checked. Each bucket becomes one
/// quota window with the group's name as its bucket; percent used is (1 - remaining_fraction) x 100.
/// </summary>
internal static class AgyUsageParser
{
    public const int WeeklyMinutes = 10080;
    public const int FiveHourMinutes = 300;

    public static IReadOnlyList<QuotaWindow> Parse(string output)
    {
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        if (start < 0 || end <= start) throw new UsageParseException("The answer has no JSON object.");

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(output.AsMemory(start, end - start + 1));
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new UsageParseException("The answer is not valid JSON.");
        }

        if (root.ValueKind != JsonValueKind.Object) throw new UsageParseException("The answer is not an object.");

        // The CLI says a run failed: not signed in, or something else. Only the kind is kept.
        if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
            && !string.Equals(status.GetString(), "SUCCESS", StringComparison.OrdinalIgnoreCase)
            && !HasUsageCommand(root))
        {
            var text = root.TryGetProperty("error", out var error) ? ErrorText(error) : "";
            throw new AgyReportedErrorException(LooksSignedOut(text));
        }

        // The guards: the usage command ran, and no model was used.
        if (!HasUsageCommand(root)) throw new AgyGuardException("The answer is not from the usage command.");
        if (!IsZero(root, "num_turns")) throw new AgyGuardException("The CLI reports a conversation turn.");
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object || !IsZero(usage, "total_tokens"))
        {
            throw new AgyGuardException("The CLI reports token use.");
        }

        var windows = new List<QuotaWindow>();
        if (root.GetProperty("command").TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object) continue;
                var name = group.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()) ? n.GetString()!.Trim() : "Antigravity";
                if (!group.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array) continue;
                foreach (var bucket in buckets.EnumerateArray())
                {
                    if (ReadBucket(bucket, name) is { } window) windows.Add(window);
                }
            }
        }

        if (windows.Count == 0) throw new UsageParseException("The answer has no usable quota bucket.");
        return windows;
    }

    private static bool HasUsageCommand(JsonElement root) =>
        root.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.Object
        && command.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
        && string.Equals(name.GetString(), "usage", StringComparison.OrdinalIgnoreCase);

    private static bool IsZero(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number == 0;

    private static QuotaWindow? ReadBucket(JsonElement bucket, string group)
    {
        if (bucket.ValueKind != JsonValueKind.Object) return null;
        if (!bucket.TryGetProperty("remaining_fraction", out var fraction) || fraction.ValueKind != JsonValueKind.Number) return null;
        var remaining = fraction.GetDouble();
        if (double.IsNaN(remaining) || double.IsInfinity(remaining)) return null;
        var used = Math.Clamp((1 - Math.Clamp(remaining, 0, 1)) * 100, 0, 100);

        string Text(string property) => bucket.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
        var minutes = MinutesFor(Text("window") + " " + Text("id"));

        DateTimeOffset? resetsAt = null;
        if (DateTimeOffset.TryParse(Text("reset_time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) resetsAt = parsed;

        return new QuotaWindow(QuotaWindow.KindFor(minutes), minutes, used, resetsAt, group, resetsAt is not null);
    }

    /// <summary>The window length in minutes from the window or bucket name ("weekly", "five_hour"), or null when it says nothing known.</summary>
    internal static int? MinutesFor(string name)
    {
        var text = name.ToLowerInvariant();
        if (text.Contains("week")) return WeeklyMinutes;
        if (text.Contains("five") && text.Contains("hour") || text.Contains("5h") || text.Contains("5-hour") || text.Contains("5_hour") || text.Contains("5 hour")) return FiveHourMinutes;
        return null;
    }

    private static string ErrorText(JsonElement error) => error.ValueKind switch
    {
        JsonValueKind.String => error.GetString() ?? "",
        JsonValueKind.Object when error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String => m.GetString() ?? "",
        _ => "",
    };

    internal static bool LooksSignedOut(string text) =>
        text.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("not authenticated", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("log in", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("login", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("signed in", StringComparison.OrdinalIgnoreCase);
}