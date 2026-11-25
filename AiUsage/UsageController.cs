using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace PomoQuote.AiUsage;

/// <summary>
/// Connects the collector to the app: one timer per enabled provider, the saved settings, warnings as balloons, and
/// the "read again now" moments (a resume from sleep, the network coming back). Everything the UI touches happens
/// on the UI thread: reads finish on worker threads and are posted back. Disposing stops the timers, cancels reads
/// and ends any child process.
/// </summary>
internal sealed class UsageController : IDisposable
{
    private static readonly TimeSpan RetryAfterResume = TimeSpan.FromSeconds(15);

    private readonly Control _ui;
    private readonly UsageSettingsStore _settingsStore;
    private readonly UsageAlertStore _alertStore;
    private readonly UsageCollector _collector;
    private readonly UsageAlertEngine _alerts;
    private readonly TimeProvider _clock;
    private readonly Action<string, string> _notify;
    private readonly Dictionary<string, System.Windows.Forms.Timer> _timers = [];
    private readonly System.Windows.Forms.Timer _retry = new();
    private bool _started;
    private bool _disposed;

    public UsageController(
        Control ui, UsageSettingsStore settingsStore, UsageCacheStore cacheStore, UsageAlertStore alertStore,
        IReadOnlyList<IUsageProvider> providers, TimeProvider clock, Action<string, string> notify)
    {
        _ui = ui;
        _settingsStore = settingsStore;
        _alertStore = alertStore;
        _clock = clock;
        _notify = notify;
        Settings = settingsStore.Load();
        _alerts = new UsageAlertEngine(alertStore.Load());
        _collector = new UsageCollector(providers, clock, cacheStore);
        _collector.StatusChanged += (_, status) => Post(() => OnStatus(status));
        _retry.Interval = (int)RetryAfterResume.TotalMilliseconds;
        _retry.Tick += (_, _) =>
        {
            _retry.Stop();
            RefreshAll();
        };
    }

    public UsageSettings Settings { get; private set; }

    /// <summary>Raised on the UI thread when a status or the settings changed: the widget redraws and re-measures.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after new settings were applied (not for a layout switch or a new reading), so things that follow a setting can follow it.</summary>
    public event EventHandler? SettingsApplied;

    public IReadOnlyList<IUsageProvider> Providers => _collector.Providers;

    /// <summary>The enabled providers' statuses in the user's display order.</summary>
    public IReadOnlyList<UsageStatus> Statuses()
    {
        var order = Settings.Order.Concat(_collector.Providers.Select(p => p.Id)).Distinct().ToList();
        return order
            .Where(Settings.IsEnabled)
            .Select(_collector.StatusOf)
            .Where(status => status is not null)
            .Select(status => status!)
            .ToList();
    }

    public IReadOnlyList<UsageProviderView> Views()
    {
        var now = _clock.GetUtcNow();
        return Statuses().Select(status => UsageViewBuilder.Build(status, Settings, now, TimeZoneInfo.Local)).ToList();
    }

    /// <summary>Loads the cached numbers, starts the timers of the enabled providers and reads them once now.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _collector.LoadCache();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        ConfigureTimers(readNow: true);
    }

    /// <summary>Applies and saves new settings; newly enabled providers are read at once, disabled ones stop.</summary>
    public void Apply(UsageSettings settings)
    {
        Settings = settings.Normalized();
        try
        {
            _settingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The choices just are not remembered for the next start.
        }

        ConfigureTimers(readNow: true);
        SettingsApplied?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Remembers the Compact/Expanded choice and the provider Compact shows. Only the settings file and the widget change: no
    /// timer is restarted and nothing is read (unlike <see cref="Apply"/>).
    /// </summary>
    public void SetLayout(bool expanded, string? selectedProvider)
    {
        if (Settings.Expanded == expanded && Settings.SelectedProvider == selectedProvider) return;
        Settings = Settings with { Expanded = expanded, SelectedProvider = selectedProvider };
        try
        {
            _settingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Just not remembered for the next start.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads one provider now (for example after a bridge was installed or a manual refresh saved new numbers).</summary>
    public void RefreshProvider(string providerId) => _ = _collector.RefreshAsync(providerId);

    /// <summary>Reads every enabled provider now (also used by "Test connection").</summary>
    public void RefreshAll()
    {
        foreach (var id in Settings.EnabledProviders) _ = _collector.RefreshAsync(id);
    }

    /// <summary>Reads one provider now and returns when it is done (for the Settings dialog's test button).</summary>
    public async Task<UsageStatus?> TestAsync(string providerId, CancellationToken cancellationToken)
    {
        await _collector.RefreshAsync(providerId, cancellationToken).ConfigureAwait(true);
        return _collector.StatusOf(providerId);
    }

    private void ConfigureTimers(bool readNow)
    {
        foreach (var timer in _timers.Values)
        {
            timer.Stop();
            timer.Dispose();
        }

        _timers.Clear();
        foreach (var provider in _collector.Providers.Where(p => Settings.IsEnabled(p.Id)))
        {
            var id = provider.Id;
            var timer = new System.Windows.Forms.Timer { Interval = (int)(provider.PollInterval ?? Settings.Interval).TotalMilliseconds };
            timer.Tick += (_, _) => _ = _collector.RefreshAsync(id);
            timer.Start();
            _timers[id] = timer;
            if (readNow) _ = _collector.RefreshAsync(id);
        }
    }

    private void OnStatus(UsageStatus status)
    {
        if (status.Latest is { State: UsageState.Ok } snapshot && Settings.AlertsEnabled && Settings.IsEnabled(status.ProviderId))
        {
            var (alerts, changed) = _alerts.Evaluate(snapshot, Settings.Thresholds);
            if (changed) SaveAlerts();
            foreach (var alert in alerts)
            {
                _notify($"{status.DisplayName} usage", AlertMessage(alert, snapshot, _clock.GetUtcNow()));
            }
        }

        // A read that failed right after a resume often means the network is not up yet: try once more shortly.
        if (status.Latest is { State: UsageState.Error } && _resumeRetryPending)
        {
            _resumeRetryPending = false;
            _retry.Stop();
            _retry.Start();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool _resumeRetryPending;

    /// <summary>The text of a warning balloon; a provider that reports one limit per model group (Antigravity) names the group.</summary>
    internal static string AlertMessage(UsageAlert alert, UsageSnapshot snapshot, DateTimeOffset now)
    {
        var label = alert.Kind == QuotaKind.FiveHour ? "5-hour" : alert.Kind == QuotaKind.Weekly ? "weekly" : "usage";
        var window = snapshot.Windows.First(w => w.Kind == alert.Kind && w.ResetsAt == alert.ResetsAt && (alert.Group.Length == 0 || w.Bucket == alert.Group));
        var reset = UsageViewBuilder.ResetText(window, now);
        var of = alert.Group.Length > 0 ? $" of {UsageViewBuilder.ShortGroup(alert.Group)}" : "";
        return $"The {label} window{of} is {Math.Round(alert.PercentUsed):0}% used (warning at {alert.Threshold}%). It resets in {reset}.";
    }

    private void SaveAlerts()
    {
        try
        {
            _alertStore.Save(_alerts.FiredKeys);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A warning may repeat after a restart; nothing else is affected.
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Post(() =>
        {
            _resumeRetryPending = true;
            RefreshAll();
        });
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable) Post(RefreshAll);
    }

    /// <summary>Runs on the UI thread; safe from any thread and after the window is gone.</summary>
    private void Post(Action action)
    {
        if (_disposed || _ui.IsDisposed || !_ui.IsHandleCreated) return;
        try
        {
            _ui.BeginInvoke(() =>
            {
                if (!_disposed && !_ui.IsDisposed) action();
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The window is closing.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        foreach (var timer in _timers.Values) timer.Dispose();
        _timers.Clear();
        _retry.Dispose();
        _collector.Dispose();
        ProcessJsonLineSession.KillAll();
    }
}
