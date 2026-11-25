using PomoQuote.Weather;
using PomoQuote.Pomodoro;
using PomoQuote.Widget;

namespace PomoQuote;

/// <summary>
/// Widget mode: the same window and the same controllers, shown compactly. Switching only hides the
/// normal controls and shows <see cref="WidgetView"/> (borderless, always on top, draggable), so the
/// running Pomodoro, the weather data and the settings carry over and nothing is created twice.
/// </summary>
internal sealed partial class MainForm
{
    private const int WM_EXITSIZEMOVE = 0x0232;

    private readonly WidgetStore _widgetStore;
    private WidgetSettings _widgetSettings;
    private WidgetView _widgetView = null!;
    private ContextMenuStrip _widgetMenu = null!;
    private ToolStripMenuItem _widgetModeItem = null!;
    private ToolStripMenuItem _widgetCityItem = null!;
    private ToolStripMenuItem _widgetPomodoroToggleItem = null!;
    private ToolStripMenuItem _widgetRefreshClaudeItem = null!;
    private readonly Dictionary<int, List<ToolStripMenuItem>> _opacityItems = [];
    private bool _widgetMode;
    private Rectangle _normalBounds;
    private Size _normalMinimumSize;

    /// <summary>The usable area of every screen, the primary screen first. Replaceable so tests can fake monitors.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<IReadOnlyList<Rectangle>> WorkAreaProvider { get; set; } = () =>
        Screen.AllScreens.OrderByDescending(screen => screen.Primary).Select(screen => screen.WorkingArea).ToList();

    internal bool IsWidgetMode => _widgetMode;

    private WeatherEffectSettingsStore _weatherEffectsStore = null!;

    /// <summary>The widget and the weather panel it reads, for tests.</summary>
    internal WidgetView WidgetViewForTests => _widgetView;

    internal WeatherPanel WeatherPanelForTests => _weather;

    /// <summary>The weather effect settings as applied to the widget.</summary>
    internal WeatherEffectSettings WeatherEffectSettings => _widgetView.EffectSettings;

    /// <summary>Saves and applies new weather effect settings (the dialog calls this; tests call it directly).</summary>
    internal void ApplyWeatherEffectSettings(WeatherEffectSettings settings)
    {
        _weatherEffectsStore.Save(settings);
        _widgetView.ApplyEffectSettings(settings);
    }

    private void ShowWeatherEffectsSettings()
    {
        using var dialog = new WeatherEffectsDialog(_widgetView.EffectSettings);
        if (dialog.ShowDialog(this) == DialogResult.OK) ApplyWeatherEffectSettings(dialog.Value);
    }

    private void BuildWidget(ToolStripMenuItem viewMenu)
    {
        _widgetView = new WidgetView(_pomodoro, _weather) { Dock = DockStyle.Fill, Visible = false };
        _widgetView.UsageUiChanged += (_, e) => _usage.SetLayout(e.Expanded, e.SelectedProvider);
        Controls.Add(_widgetView);

        _widgetModeItem = new ToolStripMenuItem("&Widget mode", null, (_, _) => SetWidgetMode(!_widgetMode))
        {
            ShortcutKeyDisplayString = "Ctrl+W",
        };
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(_widgetModeItem);
        viewMenu.DropDownItems.Add(BuildOpacityMenu("Widget &opacity"));

        _widgetCityItem = new ToolStripMenuItem("&City...", null, (_, _) => ChangeCity());
        var settings = new ToolStripMenuItem("&Settings");
        settings.DropDownItems.Add(_widgetCityItem);
        settings.DropDownItems.Add(new ToolStripMenuItem("&Pomodoro...", null, (_, _) => ShowPomodoroSettings()));
        settings.DropDownItems.Add(new ToolStripMenuItem("&Mascot...", null, (_, _) => ShowMascotSettings()));
        settings.DropDownItems.Add(new ToolStripMenuItem("&Quotes...", null, (_, _) => ShowQuoteSettings()));
        settings.DropDownItems.Add(new ToolStripMenuItem("&AI Usage...", null, (_, _) => ShowUsageSettings()));
        settings.DropDownItems.Add(new ToolStripMenuItem("Weather &effects...", null, (_, _) => ShowWeatherEffectsSettings()));
        settings.DropDownItems.Add(BuildStartInMenu());

        _widgetPomodoroToggleItem = new ToolStripMenuItem("Start Pomodoro", null, (_, _) => TogglePomodoro());
        _widgetMenu = new ContextMenuStrip();
        _widgetMenu.Items.Add(new ToolStripMenuItem("&Hide to tray", null, (_, _) => HideWidgetToTray()) { ShortcutKeyDisplayString = "Ctrl+H" });
        _widgetMenu.Items.Add(new ToolStripMenuItem("Exit &widget mode", null, (_, _) => SetWidgetMode(false)) { ShortcutKeyDisplayString = "Ctrl+W" });
        _widgetMenu.Items.Add(_widgetPomodoroToggleItem);
        _widgetRefreshClaudeItem = new ToolStripMenuItem("Refresh &Claude now", null, (_, _) => RefreshClaudeNow());
        _widgetMenu.Items.Add(_widgetRefreshClaudeItem);
        _widgetMenu.Items.Add(new ToolStripMenuItem("&Next sentence", null, (_, _) => ShowNextQuote()) { ShortcutKeyDisplayString = "Ctrl+N" });
        _widgetMenu.Items.Add(BuildOpacityMenu("&Opacity"));
        _widgetMenu.Items.Add(settings);
        _widgetMenu.Items.Add(new ToolStripSeparator());
        _widgetMenu.Items.Add(new ToolStripMenuItem("E&xit app", null, (_, _) => Close()));
        _widgetMenu.Opening += (_, _) => _widgetRefreshClaudeItem.Enabled = _usage.Settings.IsEnabled("claude");
        _widgetMenu.Opening += (_, _) => _widgetPomodoroToggleItem.Text = _pomodoro.Timer.State switch
        {
            PomodoroRunState.Running => "Pause Pomodoro",
            PomodoroRunState.Paused => "Resume Pomodoro",
            _ => "Start Pomodoro",
        };
        _widgetView.ContextMenuStrip = _widgetMenu;

        UpdateOpacityChecks();
    }

    private ToolStripMenuItem BuildOpacityMenu(string caption)
    {
        var menu = new ToolStripMenuItem(caption);
        foreach (var percent in WidgetSettings.OpacityChoices)
        {
            var item = new ToolStripMenuItem($"{percent}%", null, (_, _) => SetOpacity(percent));
            if (!_opacityItems.TryGetValue(percent, out var items)) _opacityItems[percent] = items = [];
            items.Add(item);
            menu.DropDownItems.Add(item);
        }

        return menu;
    }

    private void UpdateOpacityChecks()
    {
        foreach (var (percent, items) in _opacityItems)
        {
            foreach (var item in items) item.Checked = percent == _widgetSettings.OpacityPercent;
        }
    }

    private void SetOpacity(int percent)
    {
        _widgetSettings = _widgetSettings with { OpacityPercent = percent };
        if (_widgetMode) Opacity = _widgetSettings.Opacity;
        UpdateOpacityChecks();
        SaveWidgetSettings();
    }

    private void TogglePomodoro()
    {
        var timer = _pomodoro.Timer;
        if (timer.State == PomodoroRunState.Running) timer.Pause();
        else timer.Start();
    }

    /// <summary>Switches between the normal window and the compact widget without touching any running state.</summary>
    internal void SetWidgetMode(bool on)
    {
        if (_widgetMode == on) return;

        SuspendLayout();
        if (on)
        {
            _normalBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _normalMinimumSize = MinimumSize;
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;

            SetNormalControlsVisible(false);
            MainMenuStrip = null;
            MinimumSize = Size.Empty;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = LogicalToDeviceUnits(_widgetView.LogicalClientSize);
            Location = WidgetPlacement.Clamp(_widgetSettings.Position, Size, WorkAreaProvider());
            TopMost = true;
            Opacity = _widgetSettings.Opacity;
            _widgetView.Visible = true;
        }
        else
        {
            _widgetSettings = _widgetSettings.WithPosition(Location);
            _widgetView.Visible = false;
            Opacity = 1;
            TopMost = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = _normalMinimumSize;
            Bounds = new Rectangle(
                WidgetPlacement.Clamp(_normalBounds.Location, _normalBounds.Size, WorkAreaProvider()),
                _normalBounds.Size);
            MainMenuStrip = _menu;
            SetNormalControlsVisible(true);
        }

        _widgetMode = on;
        _widgetHidden = false;
        UpdateTrayPersistence();
        _widgetModeItem.Checked = on;
        _widgetSettings = _widgetSettings with { Enabled = on };
        ResumeLayout(performLayout: true);
        Invalidate(true);
        SaveWidgetSettings();
        if (!on)
        {
            if (IsHandleCreated && !Visible) Show(); // it was hidden in the tray: leaving widget mode shows the normal window
            RestoreTaskbarPresence();
        }
    }

    /// <summary>
    /// Leaving widget mode must give the normal window its taskbar button and its title-bar logo back. A window that was created already borderless
    /// (the app started in widget mode) never gets its taskbar button from the border change alone, so the window is registered with the taskbar
    /// again (the handle is recreated with the form's icon) and the icon is applied once more.
    /// </summary>
    private void RestoreTaskbarPresence()
    {
        if (!IsHandleCreated || IsDisposed) return;
        var icon = Icon;
        ShowInTaskbar = false;
        ShowInTaskbar = true;
        Icon = icon;
        ShowIcon = true;
    }

    private void SetNormalControlsVisible(bool visible)
    {
        _menu.Visible = visible;
        _quote.Visible = visible;
        _weather.Visible = visible;
        _pomodoroPanel.Visible = visible;
        _clock.Visible = visible;
    }

    private void SetCityItemsEnabled(bool enabled)
    {
        _cityItem.Enabled = enabled;
        _widgetCityItem.Enabled = enabled;
    }

    private void SaveWidgetSettings()
    {
        try
        {
            _widgetStore.Save(_widgetSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Widget position and mode are just not remembered next time.
        }
    }

    /// <summary>Remembers where the widget was dropped.</summary>
    private void SaveWidgetPlacement()
    {
        if (!_widgetMode) return;
        _widgetSettings = _widgetSettings.WithPosition(Location);
        SaveWidgetSettings();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_EXITSIZEMOVE) SaveWidgetPlacement();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handled here rather than as a menu shortcut so it also works while the menu bar is hidden.
        if (keyData == (Keys.Control | Keys.W))
        {
            SetWidgetMode(!_widgetMode);
            return true;
        }

        if (keyData == HideToTrayShortcut && _widgetMode)
        {
            HideWidgetToTray();
            return true;
        }

        if (keyData == (Keys.Control | Keys.N))
        {
            ShowNextQuote();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveWidgetPlacement();
        base.OnFormClosing(e);
    }

    private void DisposeWidget() => _widgetMenu.Dispose();
}
