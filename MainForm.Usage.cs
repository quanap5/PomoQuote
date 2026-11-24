using System.Diagnostics;
using PomoQuote.AiUsage;
using PomoQuote.AiUsage.Claude;

namespace PomoQuote;

/// <summary>The Claude pieces the main window uses: the bridge installer, the manual refresh and where the snapshots are kept.</summary>
internal sealed record ClaudeSetup(ClaudeBridgeInstaller Installer, ClaudeRefresher Refresher, ClaudeSnapshotStore Store);

/// <summary>
/// AI usage on the widget: the controller reads the enabled providers on its own timers and the widget draws the
/// strip. Switching providers on or off, the display mode and the thresholds happen in Settings > AI Usage.
/// </summary>
internal sealed partial class MainForm
{
    private UsageController _usage = null!;
    private ClaudeActions _claude = null!;

    private void BuildUsage(UsageSettingsStore? settings, UsageCacheStore? cache, UsageAlertStore? alerts, IReadOnlyList<IUsageProvider>? providers, ClaudeSetup? claude)
    {
        var store = new ClaudeSnapshotStore(ClaudeSnapshotStore.DefaultDirectory);
        claude ??= new ClaudeSetup(
            new ClaudeBridgeInstaller(ClaudeBridgeInstaller.DefaultSettingsPath, ClaudeBridgeInstaller.DefaultStateDirectory, () => Environment.ProcessPath ?? Application.ExecutablePath),
            new ClaudeRefresher(new ClaudeProcessFactory(), store, TimeProvider.System),
            store);
        // The provider asks for the bridge state and for a missing program on every poll: read the settings file once per few seconds.
        var bridgeStatus = new CachedValue<BridgeStatus>(() => claude.Installer.Check(), TimeSpan.FromSeconds(5), TimeProvider.System);
        _bridgeInstaller = claude.Installer;
        providers ??=
        [
            new CodexUsageProvider(new CodexAppServerSessionFactory(), TimeProvider.System),
            new ClaudeSnapshotProvider(claude.Store, () => bridgeStatus.Get().State, TimeProvider.System, () => bridgeStatus.Get().ProgramMissing),
        ];
        _usage = new UsageController(
            this,
            settings ?? new UsageSettingsStore(UsageSettingsStore.DefaultPath),
            cache ?? new UsageCacheStore(UsageCacheStore.DefaultPath),
            alerts ?? new UsageAlertStore(UsageAlertStore.DefaultPath),
            providers,
            TimeProvider.System,
            (title, message) => _tray.ShowBalloon(title, message));
        _claude = new ClaudeActions(claude.Installer, claude.Refresher, _usage, bridgeStatus.Invalidate);
        _claudeSettingsPath = claude.Installer.SettingsPath;
        _usage.Changed += (_, _) => ApplyUsageLayout();
        ApplyUsageLayout();
    }

    private ClaudeBridgeInstaller _bridgeInstaller = null!;

    /// <summary>Test seam: receives the start-up notice instead of the tray balloon.</summary>
    internal Action<string, string>? BridgeNoticeSink;

    /// <summary>
    /// Once, when the window is shown (its handle exists then): a bridge that went missing, is damaged, is written for another
    /// shell or is the old style is announced instead of silently losing the numbers (or the user's status line).
    /// </summary>
    internal Task StartBridgeCheck()
    {
        var installer = _bridgeInstaller;
        var check = Task.Run(() => ClaudeActions.StartupCheck(installer, ShowBridgeNotice));
        _ = check.ContinueWith(
            task => Trace.TraceError("The Claude bridge check failed: " + task.Exception?.GetBaseException().Message),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        return check;
    }

    private void ShowBridgeNotice(string title, string message)
    {
        // Called on a worker thread: hop to the UI thread, but only when it can take the call.
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                if (BridgeNoticeSink is { } sink) sink(title, message);
                else _tray.ShowBalloon(title, message);
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Trace.TraceError("The Claude bridge notice could not be shown: " + ex.Message);
        }
    }

    private string _claudeSettingsPath = "";

    /// <summary>Gives the widget its content and, in widget mode, grows or shrinks the window to the height the strip needs.</summary>
    private void ApplyUsageLayout()
    {
        if (_widgetView is null) return;
        _widgetView.SetUsage(() => _usage.Views(), _usage.Settings.Expanded, _usage.Settings.SelectedProvider);
        FitWidgetHeight();
    }

    /// <summary>In widget mode: the window takes the height the widget needs now (the quote card and the usage area grow and shrink) and stays on screen.</summary>
    internal void FitWidgetHeight()
    {
        if (!_widgetMode) return;
        var wanted = LogicalToDeviceUnits(_widgetView.LogicalClientSize);
        if (ClientSize == wanted) return;
        ClientSize = wanted;
        Location = Widget.WidgetPlacement.Clamp(Location, Size, WorkAreaProvider());
    }

    private void ShowUsageSettings()
    {
        var providers = _usage.Providers.Select(p => (p.Id, p.DisplayName)).ToList();
        using var dialog = new UsageSettingsDialog(_usage.Settings, providers, new CodexDetector(), _usage.TestAsync, _claude, _claudeSettingsPath, ConfirmBridge);
        if (dialog.ShowDialog(this) == DialogResult.OK) _usage.Apply(dialog.ValueFrom(_usage.Settings));
    }

    /// <summary>Shows the exact change and asks; true only when the user clicked "Set up bridge".</summary>
    private bool ConfirmBridge(Form owner, BridgePreview preview)
    {
        using var confirm = new BridgeConfirmDialog(_claudeSettingsPath, preview);
        return confirm.ShowDialog(owner) == DialogResult.OK;
    }

    /// <summary>Widget menu and Settings: one small request to Claude to read the limits now. Asks once before the first use.</summary>
    internal async void RefreshClaudeNow()
    {
        if (!_claude.RefreshNoteSeen)
        {
            var go = MessageBox.Show(this, ClaudeActions.RefreshNote + "\r\n\r\nRefresh now?", "Refresh Claude now", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);
            if (go != DialogResult.Yes) return;
            _claude.MarkRefreshNoteSeen();
        }

        var result = await _claude.RefreshAsync(CancellationToken.None);
        if (!IsDisposed && result.Outcome != RefreshOutcome.Done) _tray.ShowBalloon("Claude usage", result.Message);
    }

    private void DisposeUsage() => _usage.Dispose();
}
