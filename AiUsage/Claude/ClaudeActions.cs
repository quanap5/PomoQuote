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
    private readonly Action? _bridgeChanged;
    private readonly Action? _refreshed;

    /// <param name="bridgeChanged">Called after the settings file was changed (set up, upgrade, removal) so cached readings of it are dropped.</param>
    /// <param name="refreshed">Called after a refresh by hand succeeded (the automatic refresh then forgets an earlier failure).</param>
    public ClaudeActions(ClaudeBridgeInstaller installer, ClaudeRefresher refresher, UsageController usage, Action? bridgeChanged = null, Action? refreshed = null)
    {
        _refreshed = refreshed;
        _installer = installer;
        _refresher = refresher;
        _usage = usage;
        _bridgeChanged = bridgeChanged;
    }

    public StatusLineInfo Detect() => _installer.Detect();

    /// <summary>Why the bridge cannot be set up from this program (not PomoQuote.exe, files missing), else null.</summary>
    public string? CannotInstallReason() => _installer.CannotInstallReason();

    /// <summary>
    /// Run once when AI Usage starts: refreshes the bridge copy after an app update, then checks the set-up bridge. When it is
    /// missing, damaged or written for the wrong shell, <paramref name="notify"/> gets a title and a message (a balloon).
    /// Only reads (plus PomoQuote's own bridge folder); the Claude settings are never written. Returns the status.
    /// </summary>
    public static BridgeStatus StartupCheck(ClaudeBridgeInstaller installer, Action<string, string> notify)
    {
        installer.RefreshProgramCopy();
        var status = installer.Check();
        if (status.NeedsAttention) notify("Claude usage bridge", Notice(status));
        return status;
    }

    /// <summary>The balloon text, chosen by what is wrong and by the form of the bridge: only the new form still runs the original status line when its program is gone.</summary>
    internal static string Notice(BridgeStatus status)
    {
        if (status.ProgramMissing && status.OldStyle)
        {
            return "The Claude status line bridge program is missing, and this old-style bridge has no fail-safe, so Claude Code shows no status line now. Open Settings > AI Usage > Claude and upgrade or remove the bridge.";
        }

        if (status.ProgramMissing)
        {
            return "The Claude status line bridge is missing, so PomoQuote cannot read Claude usage. Your own status line still runs. Open Settings > AI Usage > Claude.";
        }

        if (status.ShellMismatch)
        {
            return "The Claude status line bridge does not match the shell Claude Code uses now. Open Settings > AI Usage > Claude.";
        }

        return "Your Claude status line bridge is the old style, without a fail-safe: if PomoQuote.exe is moved or deleted, your status line disappears. Open Settings > AI Usage > Claude and choose Upgrade bridge.";
    }

    public BridgePreview? Preview() => _installer.Preview();

    public BridgeStatus Check(IEnumerable<string>? projectDirectories = null) => _installer.Check(projectDirectories);

    public BridgeResult Install() => Changed(_installer.Install());

    public BridgeResult Uninstall() => Changed(_installer.Uninstall());

    /// <summary>The exact change an upgrade of an old-style bridge would make, or null when there is nothing to upgrade.</summary>
    public BridgePreview? PreviewUpgrade() => _installer.PreviewUpgrade();

    /// <summary>Rewrites an old-style bridge to the new form (only after the user confirmed the preview).</summary>
    public BridgeResult Upgrade() => Changed(_installer.Upgrade());

    private BridgeResult Changed(BridgeResult result)
    {
        if (result == BridgeResult.Done)
        {
            _bridgeChanged?.Invoke();
            _usage.RefreshProvider(ClaudeSnapshotProvider.ProviderId);
        }

        return result;
    }

    /// <summary>Whether the user has already seen the note that a manual refresh spends a very small amount of quota.</summary>
    public bool RefreshNoteSeen => _usage.Settings.ClaudeRefreshAcknowledged;

    /// <summary>Whether the user has already accepted the note that the automatic refresh spends a very small amount of quota each time it runs.</summary>
    public bool AutoRefreshNoteSeen => _usage.Settings.ClaudeAutoRefreshAcknowledged;

    public void MarkAutoRefreshNoteSeen() => _usage.Apply(_usage.Settings with { ClaudeAutoRefreshAcknowledged = true });

    public void MarkRefreshNoteSeen() => _usage.Apply(_usage.Settings with { ClaudeRefreshAcknowledged = true });

    public int SecondsUntilRefreshAllowed => _refresher.SecondsUntilAllowed();

    /// <summary>Runs the one-request refresh; on success the new numbers are loaded into the widget.</summary>
    public async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await _refresher.RefreshAsync(cancellationToken).ConfigureAwait(true);
        if (result.Outcome == RefreshOutcome.Done)
        {
            _refreshed?.Invoke();
            await _usage.TestAsync(ClaudeSnapshotProvider.ProviderId, cancellationToken).ConfigureAwait(true);
        }

        return result;
    }

    /// <summary>The text that explains the cost of a manual refresh.</summary>
    public const string RefreshNote =
        "\"Refresh Claude now\" sends one very small request to Claude (the smallest model, one word) to read your current limits. " +
        "It uses a very small amount of your quota, so it only runs when you ask (or, if you turn it on in Settings, on the schedule you choose), and at most once a minute.";

    /// <summary>The note shown once, the first time the automatic refresh is turned on.</summary>
    public const string AutoRefreshNote =
        "Automatic refresh sends one very small request to Claude (the smallest model, one word) every time it runs, to read your current limits. " +
        "Each one counts a very small amount against your Claude quota. It only runs when no Claude Code session is already keeping the numbers fresh, " +
        "never while the PC is locked or asleep, the widget is hidden to the tray or a window is at 100%, and you can turn it off here at any time.";
}
