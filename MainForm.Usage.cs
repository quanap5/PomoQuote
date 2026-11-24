using PomoQuote.AiUsage;

namespace PomoQuote;

/// <summary>
/// AI usage on the widget: the controller reads the enabled providers on its own timers and the widget draws the
/// strip. Switching providers on or off, the display mode and the thresholds happen in Settings > AI Usage.
/// </summary>
internal sealed partial class MainForm
{
    private UsageController _usage = null!;

    private void BuildUsage(UsageSettingsStore? settings, UsageCacheStore? cache, UsageAlertStore? alerts, IReadOnlyList<IUsageProvider>? providers)
    {
        providers ??= [new CodexUsageProvider(new CodexAppServerSessionFactory(), TimeProvider.System)];
        _usage = new UsageController(
            this,
            settings ?? new UsageSettingsStore(UsageSettingsStore.DefaultPath),
            cache ?? new UsageCacheStore(UsageCacheStore.DefaultPath),
            alerts ?? new UsageAlertStore(UsageAlertStore.DefaultPath),
            providers,
            TimeProvider.System,
            (title, message) => _tray.ShowBalloon(title, message));
        _usage.Changed += (_, _) => ApplyUsageLayout();
        ApplyUsageLayout();
    }

    /// <summary>Gives the widget its content and, in widget mode, grows or shrinks the window to the height the strip needs.</summary>
    private void ApplyUsageLayout()
    {
        if (_widgetView is null) return;
        var changed = _widgetView.SetUsage(() => _usage.Views(), _usage.Settings.DisplayMode);
        if (!changed || !_widgetMode) return;

        ClientSize = LogicalToDeviceUnits(_widgetView.LogicalClientSize);
        Location = Widget.WidgetPlacement.Clamp(Location, Size, WorkAreaProvider());
    }

    private void ShowUsageSettings()
    {
        var providers = _usage.Providers.Select(p => (p.Id, p.DisplayName)).ToList();
        using var dialog = new UsageSettingsDialog(_usage.Settings, providers, new CodexDetector(), _usage.TestAsync);
        if (dialog.ShowDialog(this) == DialogResult.OK) _usage.Apply(dialog.ValueFrom(_usage.Settings));
    }

    private void DisposeUsage() => _usage.Dispose();
}
