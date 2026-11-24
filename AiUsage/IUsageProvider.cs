namespace PomoQuote.AiUsage;

/// <summary>
/// A source of quota numbers for one AI product. Adding a provider means implementing this and registering it;
/// the collector, cache, alerts and widget need no change. Implementations never throw for expected problems
/// (not installed, not logged in, timeout): they return a snapshot whose <see cref="UsageSnapshot.State"/> says so.
/// </summary>
internal interface IUsageProvider
{
    /// <summary>Stable lower-case id ("codex").</summary>
    string Id { get; }

    /// <summary>Name shown to the user ("Codex").</summary>
    string DisplayName { get; }

    /// <summary>Where this provider's numbers come from (also used for the snapshots the collector makes when a read fails).</summary>
    UsageDataSource DataSource { get; }

    /// <summary>How often to read this provider, or null for the interval in the settings (a provider that only reads a local file can poll often).</summary>
    TimeSpan? PollInterval => null;

    /// <summary>Reads the quota once. Must honour <paramref name="cancellationToken"/> and clean up any process it started.</summary>
    Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken);
}
