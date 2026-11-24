namespace PomoQuote.AiUsage.Claude;

/// <summary>
/// What the Settings dialog and the widget menu can do with Claude: look at the status line, install or remove the bridge
/// (only on the user's explicit confirmation, which the caller asks for first), check it, and refresh the numbers by hand.
/// </summary>
internal sealed class ClaudeActions
{
    private readonly ClaudeBridgeInstaller _installer;
    private readonly ClaudeRefresher _refresher;
    private readonly UsageController _usage;

    public ClaudeActions(ClaudeBridgeInstaller installer, ClaudeRefresher refresher, UsageController usage)
    {
        _installer = installer;
        _refresher = refresher;
        _usage = usage;
    }

    public StatusLineInfo Detect() => _installer.Detect();

    public BridgePreview? Preview() => _installer.Preview();

    public BridgeStatus Check(IEnumerable<string>? projectDirectories = null) => _installer.Check(projectDirectories);

    public BridgeResult Install()
    {
        var result = _installer.Install();
        if (result == BridgeResult.Done) _usage.RefreshProvider(ClaudeSnapshotProvider.ProviderId);
        return result;
    }

    public BridgeResult Uninstall()
    {
        var result = _installer.Uninstall();
        if (result == BridgeResult.Done) _usage.RefreshProvider(ClaudeSnapshotProvider.ProviderId);
        return result;
    }

    /// <summary>Whether the user has already seen the note that a manual refresh spends a very small amount of quota.</summary>
    public bool RefreshNoteSeen => _usage.Settings.ClaudeRefreshAcknowledged;

    public void MarkRefreshNoteSeen() => _usage.Apply(_usage.Settings with { ClaudeRefreshAcknowledged = true });

    public int SecondsUntilRefreshAllowed => _refresher.SecondsUntilAllowed();

    /// <summary>Runs the one-request refresh; on success the new numbers are loaded into the widget.</summary>
    public async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await _refresher.RefreshAsync(cancellationToken).ConfigureAwait(true);
        if (result.Outcome == RefreshOutcome.Done) await _usage.TestAsync(ClaudeSnapshotProvider.ProviderId, cancellationToken).ConfigureAwait(true);
        return result;
    }

    /// <summary>The text that explains the cost of a manual refresh.</summary>
    public const string RefreshNote =
        "\"Refresh Claude now\" sends one very small request to Claude (the smallest model, one word) to read your current limits. " +
        "It uses a very small amount of your quota, so it only runs when you ask: never automatically, and at most once a minute.";
}
