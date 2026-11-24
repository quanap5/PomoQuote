namespace PomoQuote.AiUsage.Claude;

/// <summary>
/// The Claude Code provider. It only reads the snapshot files the status line bridge (or a manual refresh) left in the
/// usage folder: no process is started and no credential or Claude config file is opened. It tells apart: not set up,
/// bridged but waiting for a session, a session that has not had its first reply yet (rate limits appear only after it),
/// a login without plan limits, and real numbers (which become stale by age, shown with their update time).
/// </summary>
internal sealed class ClaudeSnapshotProvider : IUsageProvider
{
    public const string ProviderId = "claude";

    private readonly ClaudeSnapshotStore _store;
    private readonly Func<StatusLineState> _bridgeState;
    private readonly TimeProvider _clock;
    private readonly Func<bool>? _bridgeBroken;

    /// <param name="bridgeBroken">True when the set-up bridge program is missing or its saved command is damaged (the strip then says so).</param>
    public ClaudeSnapshotProvider(ClaudeSnapshotStore store, Func<StatusLineState> bridgeState, TimeProvider clock, Func<bool>? bridgeBroken = null)
    {
        _store = store;
        _bridgeState = bridgeState;
        _clock = clock;
        _bridgeBroken = bridgeBroken;
    }

    public string Id => ProviderId;

    public string DisplayName => "Claude";

    public UsageDataSource DataSource => UsageDataSource.ClaudeStatusLine;

    /// <summary>Reading a few small files is cheap: look every 10 seconds so the widget follows a running session.</summary>
    public TimeSpan? PollInterval => TimeSpan.FromSeconds(10);

    public Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var source = new UsageSource(ProviderId, UsageEnvironment.Windows, "", "");
        UsageSnapshot Empty(UsageState state, string text) => UsageSnapshot.Empty(source, now, DataSource, state, text);

        // A bridge that cannot run is worth more than any number: the user must know the status line is no longer measured.
        if (_bridgeBroken is not null && _bridgeState() == StatusLineState.Bridged && _bridgeBroken())
        {
            return Task.FromResult(Empty(UsageState.BridgeMissing, "bridge missing"));
        }

        IReadOnlyList<ClaudeSnapshot> snapshots;
        try
        {
            snapshots = _store.ReadAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Empty(UsageState.Error, "The usage folder cannot be read."));
        }

        // Newest first. A snapshot is usable when it carries a window that has not reset yet (Claude drops a window once it has).
        foreach (var snapshot in snapshots)
        {
            var windows = snapshot.Windows
                .Where(w => w.ResetsAt is null || w.ResetsAt > now)
                .Select(w => new QuotaWindow(w.Kind, w.Kind == QuotaKind.FiveHour ? 300 : 10080, w.PercentUsed, w.ResetsAt, "claude", w.ResetsAt is not null))
                .OrderBy(w => w.Kind)
                .ToList();
            if (windows.Count == 0) continue;

            var dataSource = snapshot.Source == ClaudeSnapshotSource.ManualRefresh ? UsageDataSource.ManualRefresh : UsageDataSource.ClaudeStatusLine;
            return Task.FromResult(new UsageSnapshot(source, windows, snapshot.WrittenAt, dataSource, UsageState.Ok));
        }

        // No numbers. Say why, from what is known.
        var state = _bridgeState();
        if (state != StatusLineState.Bridged)
        {
            return Task.FromResult(state == StatusLineState.Unreadable && snapshots.Count == 0
                ? Empty(UsageState.NotSetUp, "Claude settings were not found or cannot be read. Open Settings > AI Usage to set up.")
                : Empty(UsageState.NotSetUp, "Set up the status line bridge in Settings > AI Usage, or use Refresh Claude now."));
        }

        var latest = snapshots.FirstOrDefault(s => s.Source == ClaudeSnapshotSource.StatusLine);
        if (latest is { HadApiResponse: true, RateLimitsPresent: false })
        {
            return Task.FromResult(Empty(UsageState.NoPlanLimits, "This Claude login has no 5-hour or weekly plan limits (API-key logins have none)."));
        }

        return Task.FromResult(Empty(UsageState.Waiting, latest is null
            ? "waiting for first reply"
            : "waiting for first reply"));
    }
}
