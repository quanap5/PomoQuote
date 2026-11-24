using HelloWorldBot.Mascot;
using HelloWorldBot.Pomodoro;

namespace HelloWorldBot;

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
        _mascot.RestoreRequested += (_, _) => RestoreFromTray();
        _tray.Clicked += (_, _) => RestoreFromTray();

        _trayPomodoroToggleItem = new ToolStripMenuItem("Start Pomodoro", null, (_, _) => TogglePomodoro());
        var settings = new ToolStripMenuItem("&Settings");
        settings.DropDownItems.Add(new ToolStripMenuItem("&City...", null, (_, _) => { RestoreFromTray(); ChangeCity(); }));
        settings.DropDownItems.Add(new ToolStripMenuItem("&Pomodoro...", null, (_, _) => { RestoreFromTray(); ShowPomodoroSettings(); }));
        settings.DropDownItems.Add(new ToolStripMenuItem("&Mascot...", null, (_, _) => { RestoreFromTray(); ShowMascotSettings(); }));

        _trayMenu = new ContextMenuStrip();
        _trayOpenFont = new Font(Font, FontStyle.Bold);
        _trayMenu.Items.Add(new ToolStripMenuItem("&Open", null, (_, _) => RestoreFromTray()) { Font = _trayOpenFont });
        _trayMenu.Items.Add(_trayPomodoroToggleItem);
        _trayMenu.Items.Add(settings);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));
        _trayMenu.Opening += (_, _) => _trayPomodoroToggleItem.Text = _pomodoro.Timer.State switch
        {
            PomodoroRunState.Running => "Pause Pomodoro",
            PomodoroRunState.Paused => "Resume Pomodoro",
            _ => "Start Pomodoro",
        };
        _tray.Menu = _trayMenu;
    }

    private void ShowMascotSettings()
    {
        using var dialog = new MascotSettingsDialog(_mascotSettings, MonitorProvider(), _mascot.ReloadConfig);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _mascotSettings = dialog.Value;
        try
        {
            _mascotStore.Save(_mascotSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The options apply now; they are just not remembered next time.
        }

        _mascot.Apply(_mascotSettings);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
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
        _tray.SetPersistent(false);
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void DisposeMascot()
    {
        _mascot.Dispose();
        _trayMenu.Dispose();
        _trayOpenFont?.Dispose();
    }
}
