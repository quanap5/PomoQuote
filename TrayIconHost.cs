
using PomoQuote.Mascot;

namespace PomoQuote;

/// <summary>
/// The app's one notification-area icon. It stays visible while the window is hidden in the tray, and
/// otherwise shows up only for the few seconds a balloon notification needs. Shared by the Pomodoro
/// (balloons) and the mascot (minimize to tray), so there is never more than one icon.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private const int BalloonVisibleMilliseconds = 6000;

    private readonly NotifyIcon _icon = new() { Text = "PomoQuote" };
    private readonly System.Windows.Forms.Timer _balloonTimer = new() { Interval = BalloonVisibleMilliseconds };
    private readonly Icon _logoIcon;
    private bool _persistent;

    public TrayIconHost()
    {
        // The PomoQuote logo at the size the notification area uses at the current display scale.
        _logoIcon = AppIcon.Load(SystemInformation.SmallIconSize);
        _icon.Icon = _logoIcon;
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Clicked?.Invoke(this, EventArgs.Empty);
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) DoubleClicked?.Invoke(this, EventArgs.Empty);
        };
        _balloonTimer.Tick += (_, _) =>
        {
            _balloonTimer.Stop();
            UpdateVisibility();
        };
    }

    /// <summary>The right-click menu of the icon.</summary>
    public ContextMenuStrip? Menu
    {
        get => _icon.ContextMenuStrip;
        set => _icon.ContextMenuStrip = value;
    }

    /// <summary>Raised when the icon is clicked with the left mouse button.</summary>
    public event EventHandler? Clicked;

    /// <summary>Raised when the icon is double-clicked with the left mouse button.</summary>
    public event EventHandler? DoubleClicked;

    public bool IconVisible => _icon.Visible;

    /// <summary>Keeps the icon on screen (true) while the window is hidden, or lets it hide again (false).</summary>
    public void SetPersistent(bool persistent)
    {
        _persistent = persistent;
        UpdateVisibility();
    }

    public void ShowBalloon(string title, string message)
    {
        _icon.Visible = true;
        _icon.ShowBalloonTip(BalloonVisibleMilliseconds, title, message, ToolTipIcon.Info);
        _balloonTimer.Stop();
        _balloonTimer.Start();
    }

    private void UpdateVisibility() => _icon.Visible = _persistent || _balloonTimer.Enabled;

    public void Dispose()
    {
        _balloonTimer.Stop();
        _balloonTimer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _logoIcon.Dispose();
    }
}
