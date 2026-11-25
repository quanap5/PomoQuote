using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Widget;

namespace PomoQuote;

/// <summary>
/// The taskbar mascot: minimizing hides the window to the tray and sends the robot walking along the
/// taskbar; clicking the robot or the tray icon brings the window back. The tray icon is shared with the
/// Pomodoro's balloon notifications.
/// </summary>
internal sealed partial class MainForm
{
    private readonly MascotStore _mascotStore;
    private readonly TrayIconHost _tray;
    private MascotSettings _mascotSettings;
    private MascotController _mascot = null!;
    private ContextMenuStrip _trayMenu = null!;
    private ToolStripMenuItem _trayPomodoroToggleItem = null!;
    private Font? _trayOpenFont;
    private bool _inTray;
    private readonly List<ToolStripMenuItem> _mascotShowItems = [];
    private readonly List<(string Id, ToolStripMenuItem Item)> _mascotCharacterItems = [];
    private ToolStripMenuItem _trayToggleItem = null!;
    private ToolStripMenuItem _trayOpenItem = null!;

    /// <summary>The monitors the robot may walk on. Replaceable so tests can fake a monitor layout.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<IReadOnlyList<MonitorInfo>> MonitorProvider { get; set; } = () =>
        Screen.AllScreens
            .Select(screen => new MonitorInfo(screen.DeviceName, screen.Bounds, screen.WorkingArea, SystemDpi, screen.Primary))
            .ToList();

    internal MascotController Mascot => _mascot;

    internal bool IsInTray => _inTray;

    // The app is system-DPI-aware, so Windows already gives every monitor the system scale.
    private static int SystemDpi => (int)Math.Round(DeviceDpiOrDefault());

    private static float DeviceDpiOrDefault()
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        return graphics.DpiX;
    }

    private void BuildMascot(TimeProvider clock)
    {
        _mascot = new MascotController(_pomodoro, _mascotSettings, Path.Combine(AppContext.BaseDirectory, "Assets", "Mascot"), clock, MascotStore.UserConfigPath)
        {
            MonitorProvider = () => MonitorProvider(),
        };

        // A single click only makes the character react; the window comes back on a double click (or from the tray).
        _mascot.RestoreRequested += (_, _) => OnMascotDoubleClicked();
        _tray.Clicked += (_, _) => OnTrayClicked();
        _tray.DoubleClicked += (_, _) => OnTrayDoubleClicked();

        _trayPomodoroToggleItem = new ToolStripMenuItem("Start Pomodoro", null, (_, _) => TogglePomodoro());
        var settings = new ToolStripMenuItem("&Settings");
        settings.DropDownItems.Add(new ToolStripMenuItem("&City...", null, (_, _) => { BringBack(); ChangeCity(); }));
        settings.DropDownItems.Add(new ToolStripMenuItem("&Pomodoro...", null, (_, _) => { BringBack(); ShowPomodoroSettings(); }));
        settings.DropDownItems.Add(BuildMascotMenu(fromTray: true));
        settings.DropDownItems.Add(new ToolStripMenuItem("Weather &effects...", null, (_, _) => { BringBack(); ShowWeatherEffectsSettings(); }));
        settings.DropDownItems.Add(BuildThemeMenu());
        settings.DropDownItems.Add(BuildStartInMenu());

        _trayMenu = new ContextMenuStrip();
        _trayOpenFont = new Font(Font, FontStyle.Bold);
        _trayToggleItem = new ToolStripMenuItem("Hide widget", null, (_, _) => ApplyShellAction(ShellAction.TrayClick)) { Font = _trayOpenFont };
        _trayOpenItem = new ToolStripMenuItem("&Open", null, (_, _) => OpenNormalWindowFromTray());
        _trayMenu.Items.Add(_trayToggleItem);
        _trayMenu.Items.Add(_trayOpenItem);
        _trayMenu.Items.Add(_trayPomodoroToggleItem);
        _trayMenu.Items.Add(settings);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));
        _trayMenu.Opening += (_, _) =>
        {
            _trayToggleItem.Visible = _widgetMode;
            _trayToggleItem.Text = _widgetHidden ? "Show widget" : "Hide widget";
            _trayOpenItem.Text = _widgetMode ? "&Normal window" : "&Open";
        };
        _trayMenu.Opening += (_, _) => _trayPomodoroToggleItem.Text = _pomodoro.Timer.State switch
        {
            PomodoroRunState.Running => "Pause Pomodoro",
            PomodoroRunState.Paused => "Resume Pomodoro",
            _ => "Start Pomodoro",
        };
        _tray.Menu = _trayMenu;
    }

    /// <summary>
    /// Settings > Mascot: Show the mascot (on or off at once), the character picker (the current one is ticked), and Options... (the dialog).
    /// The same menu is in the normal window, the widget and the tray; their entries are kept in step.
    /// </summary>
    private ToolStripMenuItem BuildMascotMenu(bool fromTray)
    {
        var menu = new ToolStripMenuItem("&Mascot");
        var show = new ToolStripMenuItem("Show the &mascot", null, (_, _) => SetMascotEnabled(!_mascotSettings.Enabled));
        _mascotShowItems.Add(show);
        menu.DropDownItems.Add(show);
        menu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var character in MascotCharacters.All)
        {
            var id = character.Id;
            var item = new ToolStripMenuItem(character.Name, null, (_, _) => SetMascotCharacter(id));
            _mascotCharacterItems.Add((id, item));
            menu.DropDownItems.Add(item);
        }

        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(new ToolStripMenuItem("&Options...", null, (_, _) =>
        {
            if (fromTray) BringBack();
            ShowMascotSettings();
        }));
        UpdateMascotChecks();
        return menu;
    }

    /// <summary>The "Show the mascot" item that sits directly in the widget's right-click menu.</summary>
    private ToolStripMenuItem BuildMascotToggle()
    {
        var item = new ToolStripMenuItem("Show the &mascot", null, (_, _) => SetMascotEnabled(!_mascotSettings.Enabled));
        _mascotShowItems.Add(item);
        UpdateMascotChecks();
        return item;
    }

    private void UpdateMascotChecks()
    {
        foreach (var item in _mascotShowItems) item.Checked = _mascotSettings.Enabled;
        foreach (var (id, item) in _mascotCharacterItems) item.Checked = id == _mascotSettings.Character;
    }

    /// <summary>For the tests: the Show the mascot entries and the character entries with their tick state.</summary>
    internal IReadOnlyList<(string Text, bool Checked)> MascotMenuForTests =>
        _mascotShowItems.Select(i => (i.Text ?? "", i.Checked)).Concat(_mascotCharacterItems.Select(c => (c.Item.Text ?? "", c.Item.Checked))).ToList();

    internal IReadOnlyList<(string Id, bool Checked)> MascotCharacterItemsForTests => _mascotCharacterItems.Select(c => (c.Id, c.Item.Checked)).ToList();

    /// <summary>The mascot on or off, at once and remembered: it walks the taskbar next to the widget (and when the window is minimized), or it is gone.</summary>
    internal void SetMascotEnabled(bool enabled)
    {
        ApplyMascotSettings(_mascotSettings with { Enabled = enabled });
    }

    /// <summary>The character the mascot is, at once and remembered.</summary>
    internal void SetMascotCharacter(string id)
    {
        ApplyMascotSettings(_mascotSettings with { Character = id });
    }

    private void ApplyMascotSettings(MascotSettings settings)
    {
        _mascotSettings = settings.Normalized();
        try
        {
            _mascotStore.Save(_mascotSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The options apply now; they are just not remembered next time.
        }

        _mascot.Apply(_mascotSettings);
        UpdateWidgetMascot();
        UpdateMascotChecks();
    }

    /// <summary>
    /// In widget mode the mascot walks the taskbar for as long as it is on, the same character that walks out when the window is minimized; a double
    /// click brings the widget forward (or back from the tray). Outside widget mode only minimizing sends it out, as before.
    /// </summary>
    private void UpdateWidgetMascot()
    {
        if (_inTray || IsDisposed || !IsHandleCreated) return;
        if (_widgetMode && _mascotSettings.Enabled) _mascot.Start();
        else _mascot.Stop();
    }

    internal void OnMascotDoubleClicked()
    {
        if (_inTray) RestoreFromTray();
        else if (_widgetHidden) ShowWidgetFromTray();
        else if (_widgetMode) Activate();
    }

    private void ShowMascotSettings()
    {
        using var dialog = new MascotSettingsDialog(_mascotSettings, MonitorProvider(), _mascot.ReloadConfig);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        ApplyMascotSettings(dialog.Value);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateQuoteBand();
        if (WindowState == FormWindowState.Minimized && _mascot is not null && !_inTray && IsHandleCreated)
        {
            // Not from inside the resize itself: hiding a window while it is being minimized is asking for trouble.
            BeginInvoke(HideToTray);
        }
    }

    /// <summary>Hides the window to the tray and sends the robot out, if the mascot is enabled and can start.</summary>
    internal void HideToTray()
    {
        if (_inTray || IsDisposed || WindowState != FormWindowState.Minimized || !_mascotSettings.Enabled) return;
        if (!_mascot.Start()) return;

        _inTray = true;
        _tray.SetPersistent(true);
        Hide();
    }

    /// <summary>Brings the window back and takes the robot off the taskbar.</summary>
    internal void RestoreFromTray()
    {
        if (!_inTray || IsDisposed) return;

        _inTray = false;
        _mascot.Stop();
        UpdateTrayPersistence();
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        UpdateWidgetMascot(); // in widget mode the mascot goes on walking beside the widget
    }

    private void DisposeMascot()
    {
        _mascot.Dispose();
        _trayMenu.Dispose();
        _trayOpenFont?.Dispose();
    }
}
