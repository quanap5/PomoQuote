using PomoQuote.Widget;

namespace PomoQuote;

/// <summary>
/// Widget mode and the tray: the widget can be hidden to the notification-area icon (which is always there in widget mode) and
/// shown again from it, from the tray menu or by starting a second copy. While it is hidden there is no taskbar button and the
/// widget view is not visible (so the effect timer and the clock tick are stopped); the weather, quota and Pomodoro timers and
/// their alerts keep running because they never depended on the window.
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>The keyboard shortcut that hides the widget to the tray (it works while the widget has the keyboard focus).</summary>
    internal const Keys HideToTrayShortcut = Keys.Control | Keys.H;

    private bool _widgetHidden;
    private readonly List<(StartMode Mode, ToolStripMenuItem Item)> _startInItems = [];

    /// <summary>The widget on screen, the widget hidden in the tray, or the normal window.</summary>
    internal ShellState ShellState => !_widgetMode ? ShellState.NormalWindow : _widgetHidden ? ShellState.WidgetHidden : ShellState.WidgetVisible;

    /// <summary>Test seams: the pieces a test drives or observes.</summary>
    internal Pomodoro.PomodoroController PomodoroForTests => _pomodoro;

    internal bool TrayIconVisible => _tray.IconVisible;

    internal ContextMenuStrip WidgetMenuForTests => _widgetMenu;

    internal ContextMenuStrip TrayMenuForTests => _trayMenu;

    /// <summary>Runs a keyboard shortcut the way the window does while it has the keyboard focus.</summary>
    internal bool HandleShortcut(Keys keyData)
    {
        var message = default(Message);
        return ProcessCmdKey(ref message, keyData);
    }

    /// <summary>The start mode as saved.</summary>
    internal StartMode StartIn => _widgetSettings.StartIn;

    /// <summary>Hides the widget to the tray; only from widget mode.</summary>
    internal void HideWidgetToTray()
    {
        if (ShellStateMachine.Next(ShellState, ShellAction.HideToTray) != ShellState.WidgetHidden || _widgetHidden) return;
        SaveWidgetPlacement();
        _widgetHidden = true;
        UpdateTrayPersistence();
        Hide();
    }

    /// <summary>Shows the hidden widget where it was (moved into a visible work area if its monitor is gone).</summary>
    internal void ShowWidgetFromTray()
    {
        if (!_widgetMode || !_widgetHidden) return;
        _widgetHidden = false;
        Location = WidgetPlacement.Clamp(Location, Size, WorkAreaProvider());
        Show();
        Activate();
    }

    /// <summary>Applies a tray action to the state machine and carries out the new state.</summary>
    private void ApplyShellAction(ShellAction action)
    {
        var next = ShellStateMachine.Next(ShellState, action);
        if (next == ShellState && action != ShellAction.TrayDoubleClick && action != ShellAction.SecondLaunch) return;
        switch (next)
        {
            case ShellState.WidgetHidden: HideWidgetToTray(); break;
            case ShellState.WidgetVisible:
                ShowWidgetFromTray();
                Activate();
                break;
        }
    }

    internal void OnTrayClicked()
    {
        if (_inTray) RestoreFromTray();
        else if (_widgetMode) ApplyShellAction(ShellAction.TrayClick);
        else ActivateNormalWindow();
    }

    internal void OnTrayDoubleClicked()
    {
        if (_inTray) RestoreFromTray();
        else if (_widgetMode) ApplyShellAction(ShellAction.TrayDoubleClick);
        else ActivateNormalWindow();
    }

    private void ActivateNormalWindow()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>A second copy of the app was started: show this one wherever it is (called from a thread-pool thread).</summary>
    internal void ShowFromSecondLaunch()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                if (_inTray) RestoreFromTray();
                else if (_widgetHidden) ShowWidgetFromTray();
                else ActivateNormalWindow();
            });
        }
        catch (InvalidOperationException)
        {
            // The window closed between the check and the post.
        }
    }

    /// <summary>What a tray menu item that needs the window does first: bring the app back from the mascot tray and from the widget tray.</summary>
    private void BringBack()
    {
        RestoreFromTray();
        ShowWidgetFromTray();
    }

    /// <summary>"Normal window" in the tray menu: leaves widget mode (or comes back from the mascot tray).</summary>
    private void OpenNormalWindowFromTray()
    {
        if (_widgetMode) SetWidgetMode(false);
        RestoreFromTray();
        ActivateNormalWindow();
    }

    /// <summary>The tray icon stays on screen while the app is in widget mode (shown or hidden) or hidden by the mascot.</summary>
    private void UpdateTrayPersistence() => _tray.SetPersistent(_inTray || _widgetMode);

    private ToolStripMenuItem BuildStartInMenu()
    {
        var menu = new ToolStripMenuItem("S&tart in");
        foreach (var mode in Enum.GetValues<StartMode>())
        {
            var item = new ToolStripMenuItem(StartModePolicy.Caption(mode), null, (_, _) => SetStartMode(mode));
            _startInItems.Add((mode, item));
            menu.DropDownItems.Add(item);
        }

        UpdateStartInChecks();
        return menu;
    }

    private void UpdateStartInChecks()
    {
        foreach (var (mode, item) in _startInItems) item.Checked = mode == _widgetSettings.StartIn;
    }

    /// <summary>Sets and saves which mode the app opens in.</summary>
    internal void SetStartMode(StartMode mode)
    {
        _widgetSettings = _widgetSettings with { StartIn = mode };
        UpdateStartInChecks();
        SaveWidgetSettings();
    }
}