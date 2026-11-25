using Microsoft.Win32;

namespace PomoQuote.AiUsage.Claude;

/// <summary>Why a scheduled Claude refresh runs or does not run right now.</summary>
internal enum AutoRefreshDecision
{
    Run,

    /// <summary>The setting is off: nothing is scheduled at all.</summary>
    Off,

    /// <summary>The last attempt (or a pause after a resume) is too recent for the interval.</summary>
    NotDue,

    /// <summary>A Claude session (the status line) or an earlier refresh updated the numbers less than half an interval ago.</summary>
    Fresh,

    /// <summary>A window is at 100%: nothing is spent while Claude is limited.</summary>
    Limited,

    /// <summary>The PC is locked or asleep.</summary>
    Paused,

    /// <summary>The widget is hidden to the tray.</summary>
    Hidden,
}

/// <summary>What the schedule needs to know to decide. Plain values only, so the decision allocates nothing.</summary>
internal readonly record struct AutoRefreshInputs(
    bool Enabled, TimeSpan Interval, DateTimeOffset Now, DateTimeOffset? LastAttempt, DateTimeOffset? NotBefore,
    bool Locked, bool Asleep, bool Hidden, DateTimeOffset? NewestSnapshot, bool AtLimit);

/// <summary>
/// The optional automatic refresh of the Claude numbers: on its own once-a-minute timer (created only while the setting is on, so it costs
/// nothing when off) it asks, once per interval, whether a refresh is needed, and then runs the existing one-request refresh (`claude -p`, the
/// smallest model, one word). Nothing new is read or sent: no credential, no other endpoint. It does not run when a Claude session or an earlier
/// refresh updated the numbers less than half an interval ago, while a window is at 100%, while the PC is locked or asleep, or while the widget
/// is hidden to the tray, and it waits a short while after a resume for the network. A refresh that fails (not installed, not signed in, no
/// answer) is not retried before the next interval; changing the setting or a refresh by hand that works starts again. Disposing stops the timer
/// and cancels a running refresh, which ends the process.
/// </summary>
internal sealed class ClaudeAutoRefresh : IDisposable
{
    public static readonly TimeSpan TickPeriod = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(45);

    private readonly Func<CancellationToken, Task<RefreshResult>> _refresh;
    private readonly ClaudeSnapshotStore _store;
    private readonly TimeProvider _clock;
    private readonly Func<bool> _widgetHidden;
    private readonly Func<TimeSpan, Action, IDisposable> _startTimer;
    private readonly bool _watchSystemEvents;
    private IDisposable? _timer;
    private CancellationTokenSource? _cancel;
    private bool _enabled;
    private TimeSpan _interval = TimeSpan.FromMinutes(UsageSettings.DefaultAutoRefreshMinutes);
    private DateTimeOffset? _lastAttempt;
    // Written by the system-events thread (unlock, resume) and read by the UI thread: UTC ticks, 0 for none, always through Interlocked.
    private long _notBeforeTicks;
    private volatile bool _locked;
    private volatile bool _asleep;
    private bool _running;
    private bool _disposed;

    /// <param name="refresh">Runs the one-request refresh (the same one the Refresh Claude now button runs).</param>
    /// <param name="widgetHidden">True while the widget is hidden to the tray.</param>
    /// <param name="startTimer">Starts a repeating timer on the UI thread; replaceable so tests can drive the ticks.</param>
    /// <param name="watchSystemEvents">Whether to follow lock, unlock, sleep and resume (tests turn it off and call the handlers).</param>
    public ClaudeAutoRefresh(
        Func<CancellationToken, Task<RefreshResult>> refresh, ClaudeSnapshotStore store, TimeProvider clock, Func<bool> widgetHidden,
        Func<TimeSpan, Action, IDisposable>? startTimer = null, bool watchSystemEvents = true)
    {
        _refresh = refresh;
        _store = store;
        _clock = clock;
        _widgetHidden = widgetHidden;
        _startTimer = startTimer ?? StartFormsTimer;
        _watchSystemEvents = watchSystemEvents;
    }

    /// <summary>True while a refresh started by the schedule is running.</summary>
    public bool Running => _running;

    /// <summary>Whether the timer exists (it does only while the setting is on).</summary>
    public bool Scheduled => _timer is not null;

    /// <summary>The outcome of the last scheduled refresh (null before the first): shown in Settings as a short canned message, never raw output.</summary>
    public RefreshOutcome? LastOutcome { get; private set; }

    /// <summary>Applies the setting: on starts the timer (and looks at once), off stops it and cancels a running refresh. Any change starts the schedule fresh.</summary>
    public void Configure(bool enabled, int minutes)
    {
        if (_disposed) return;
        var interval = TimeSpan.FromMinutes(UsageSettings.AutoRefreshChoices.Contains(minutes) ? minutes : UsageSettings.DefaultAutoRefreshMinutes);
        var changed = enabled != _enabled || interval != _interval;
        _enabled = enabled;
        _interval = interval;
        if (!enabled)
        {
            Stop();
            return;
        }

        if (_timer is null)
        {
            _timer = _startTimer(TickPeriod, Tick);
            if (_watchSystemEvents)
            {
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                SystemEvents.SessionSwitch += OnSessionSwitch;
            }
        }

        if (changed)
        {
            _lastAttempt = null;
            Interlocked.Exchange(ref _notBeforeTicks, 0);
            Tick();
        }
    }

    /// <summary>A refresh by hand worked: an earlier failure no longer holds the schedule back.</summary>
    public void ForgetFailure()
    {
        LastOutcome = RefreshOutcome.Done;
        _lastAttempt = null;
    }

    /// <summary>The decision, from plain values only (public to the tests).</summary>
    internal static AutoRefreshDecision Decide(in AutoRefreshInputs s)
    {
        if (!s.Enabled) return AutoRefreshDecision.Off;
        if (s.Locked || s.Asleep) return AutoRefreshDecision.Paused;
        if (s.Hidden) return AutoRefreshDecision.Hidden;
        if (s.NotBefore is { } notBefore && s.Now < notBefore) return AutoRefreshDecision.NotDue;
        if (s.LastAttempt is { } last && s.Now - last < s.Interval) return AutoRefreshDecision.NotDue;
        if (s.NewestSnapshot is { } newest && s.Now - newest < s.Interval / 2) return AutoRefreshDecision.Fresh;
        if (s.AtLimit) return AutoRefreshDecision.Limited;
        return AutoRefreshDecision.Run;
    }

    /// <summary>One look: cheap while nothing is due (a few comparisons), and the snapshot folder is read only when an attempt is really due.</summary>
    internal void Tick()
    {
        if (_disposed || _running || !_enabled) return;
        var now = _clock.GetUtcNow();
        var notBeforeTicks = Interlocked.Read(ref _notBeforeTicks);
        var notBefore = notBeforeTicks == 0 ? (DateTimeOffset?)null : new DateTimeOffset(notBeforeTicks, TimeSpan.Zero);
        var cheap = new AutoRefreshInputs(true, _interval, now, _lastAttempt, notBefore, _locked, _asleep, _widgetHidden(), null, false);
        if (Decide(cheap) != AutoRefreshDecision.Run) return;

        var (newest, atLimit) = ReadNumbers(now);
        if (Decide(cheap with { NewestSnapshot = newest, AtLimit = atLimit }) != AutoRefreshDecision.Run) return;
        _ = RunAsync(now);
    }

    private (DateTimeOffset? Newest, bool AtLimit) ReadNumbers(DateTimeOffset now)
    {
        try
        {
            var all = _store.ReadAll(); // newest first
            if (all.Count == 0) return (null, false);
            var atLimit = all[0].Windows.Any(w => w.PercentUsed >= 100 && (w.ResetsAt is null || w.ResetsAt > now));
            return (all[0].WrittenAt, atLimit);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, false);
        }
    }

    private async Task RunAsync(DateTimeOffset started)
    {
        _running = true;
        _lastAttempt = started; // a failure (or a refresh that cannot start) waits one interval, like a success
        _cancel = new CancellationTokenSource();
        try
        {
            var result = await _refresh(_cancel.Token).ConfigureAwait(true);
            if (_disposed) return;
            if (result.Outcome is RefreshOutcome.TooSoon or RefreshOutcome.Busy)
            {
                _lastAttempt = null; // nothing ran (a refresh by hand just ran or is running): look again at the next tick
                return;
            }

            LastOutcome = result.Outcome;
        }
        catch (OperationCanceledException)
        {
            // Turned off or the app is closing.
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            LastOutcome = RefreshOutcome.Failed;
        }
        finally
        {
            _running = false;
            var cancel = _cancel;
            _cancel = null;
            cancel?.Dispose();
        }
    }

    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        if (_watchSystemEvents)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }

        try
        {
            _cancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It finished just now.
        }

        _lastAttempt = null;
        Interlocked.Exchange(ref _notBeforeTicks, 0);
        LastOutcome = null;
    }

    internal void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) _asleep = true;
        else if (e.Mode == PowerModes.Resume) Resumed();
    }

    internal void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                _locked = true;
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
            case SessionSwitchReason.SessionLogon:
                Resumed();
                break;
        }
    }

    /// <summary>After a resume or an unlock the network may not be up yet: wait a little before the next attempt.</summary>
    private void Resumed()
    {
        _asleep = false;
        _locked = false;
        Interlocked.Exchange(ref _notBeforeTicks, (_clock.GetUtcNow() + ResumeDelay).UtcTicks);
    }

    private static IDisposable StartFormsTimer(TimeSpan period, Action tick)
    {
        var timer = new System.Windows.Forms.Timer { Interval = (int)period.TotalMilliseconds };
        timer.Tick += (_, _) => tick();
        timer.Start();
        return timer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}