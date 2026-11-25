namespace PomoQuote.AiUsage;

/// <summary>Where a provider's CLI runs. Only Windows is supported in this release; WSL sources come later.</summary>
public enum UsageEnvironment
{
    Windows,
}

/// <summary>The quota windows PomoQuote knows by their length; anything else is <see cref="Other"/>.</summary>
public enum QuotaKind
{
    FiveHour,
    Weekly,
    Other,
}

/// <summary>How far a provider's last read got. Everything except <see cref="Ok"/> and <see cref="Stale"/> carries no windows.</summary>
public enum UsageState
{
    /// <summary>Data was read and is recent.</summary>
    Ok,

    /// <summary>Data is older than twice the refresh interval (computed by the reader of the snapshot, see <see cref="UsageSnapshot.IsStale"/>).</summary>
    Stale,

    /// <summary>The provider is enabled but nothing has been read yet.</summary>
    Waiting,

    NotInstalled,
    NotLoggedIn,

    /// <summary>The provider needs a one-time setup (for Claude: the status line bridge) that has not been done.</summary>
    NotSetUp,

    /// <summary>The account has no plan limits to show (an API-key login has no 5-hour or weekly window).</summary>
    NoPlanLimits,

    /// <summary>The Claude status line bridge is set up but its program is missing or its saved command is damaged (the user's own status line keeps running).</summary>
    BridgeMissing,

    /// <summary>The CLI answered, but not in a shape PomoQuote understands (an older or newer version).</summary>
    Unsupported,

    /// <summary>The read failed (timeout, crash, no network); see <see cref="UsageSnapshot.ErrorText"/>.</summary>
    Error,
}

/// <summary>Where the numbers came from.</summary>
public enum UsageDataSource
{
    CodexAppServer,
    ClaudeStatusLine,
    ManualRefresh,
    AntigravityCli,
}

/// <summary>Who the numbers belong to. The account is only ever a short hash, never the id, email or name.</summary>
/// <param name="ProviderId">Stable id of the provider, for example "codex".</param>
/// <param name="Environment">Where the CLI runs.</param>
/// <param name="AccountKey">Hash of the account id, or empty when the provider gave none.</param>
/// <param name="PlanTier">Subscription tier as the provider names it ("plus", "pro"), or empty.</param>
public sealed record UsageSource(string ProviderId, UsageEnvironment Environment, string AccountKey, string PlanTier)
{
    /// <summary>Identity of the source for caches and alerts (provider, environment, account).</summary>
    public string Key => $"{ProviderId}|{Environment}|{AccountKey}";
}

/// <summary>One quota window, normalised to percent used.</summary>
/// <param name="Kind">Five-hour, weekly or other, from the window length.</param>
/// <param name="DurationMinutes">Window length in minutes, when the provider says.</param>
/// <param name="PercentUsed">0 to 100 (not remaining).</param>
/// <param name="ResetsAt">When the window resets, if known.</param>
/// <param name="Bucket">Model group or limit id the window belongs to, or empty.</param>
/// <param name="IsResetReal">False when the reset time is only a placeholder (never alert on those).</param>
public sealed record QuotaWindow(QuotaKind Kind, int? DurationMinutes, double PercentUsed, DateTimeOffset? ResetsAt, string Bucket, bool IsResetReal)
{
    /// <summary>The window kind for a length in minutes (300 is five hours, 10080 is a week).</summary>
    public static QuotaKind KindFor(int? durationMinutes) => durationMinutes switch
    {
        300 => QuotaKind.FiveHour,
        10080 => QuotaKind.Weekly,
        _ => QuotaKind.Other,
    };

    /// <summary>Time left until the reset, computed from the clock (never a ticking counter), or null when unknown or past.</summary>
    public TimeSpan? TimeLeft(DateTimeOffset now) => ResetsAt is { } at && at > now ? at - now : null;
}

/// <summary>One immutable read of a provider.</summary>
/// <param name="Source">Who the data belongs to.</param>
/// <param name="Windows">Quota windows, empty unless <see cref="State"/> is Ok or Stale.</param>
/// <param name="ObservedAt">When the data was read.</param>
/// <param name="DataSource">Where it came from.</param>
/// <param name="State">How the read ended.</param>
/// <param name="ErrorText">Short, non-secret reason for any state other than Ok.</param>
public sealed record UsageSnapshot(
    UsageSource Source,
    IReadOnlyList<QuotaWindow> Windows,
    DateTimeOffset ObservedAt,
    UsageDataSource DataSource,
    UsageState State,
    string ErrorText = "")
{
    /// <summary>Whether the numbers are older than twice the refresh interval.</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan interval) => now - ObservedAt > interval + interval;

    /// <summary>The state to show now: a good read that has gone stale reads as <see cref="UsageState.Stale"/>.</summary>
    public UsageState EffectiveState(DateTimeOffset now, TimeSpan interval) =>
        State == UsageState.Ok && IsStale(now, interval) ? UsageState.Stale : State;

    /// <summary>A snapshot without data.</summary>
    public static UsageSnapshot Empty(UsageSource source, DateTimeOffset now, UsageDataSource dataSource, UsageState state, string errorText = "") =>
        new(source, [], now, dataSource, state, errorText);
}

/// <summary>Turns an account id into a short hash so it can sit in caches and logs without being the id.</summary>
internal static class AccountKey
{
    public static string From(string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return "";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("pomoquote-usage|" + accountId));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
