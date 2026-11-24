using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

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
    private readonly Icon _robotIcon;
    private bool _persistent;

    public TrayIconHost()
    {
        _robotIcon = CreateRobotIcon();
        _icon.Icon = _robotIcon;
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Clicked?.Invoke(this, EventArgs.Empty);
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

    /// <summary>A 32 px icon of the robot, cut from the sprite sheet when it is there, otherwise the stock icon.</summary>
    private static Icon CreateRobotIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Mascot", "robot-sprites.png");
        if (!File.Exists(path)) return (Icon)SystemIcons.Application.Clone();

        try
        {
            using var sheet = new Bitmap(path);
            const int frame = 24; // the first idle frame, enlarged 2x with no blur
            using var bitmap = new Bitmap(frame * 2, frame * 2);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(sheet, new Rectangle(0, 0, frame * 2, frame * 2), new Rectangle(0, 0, frame, frame), GraphicsUnit.Pixel);
            }

            var handle = bitmap.GetHicon();
            try
            {
                using var fromHandle = Icon.FromHandle(handle);
                return (Icon)fromHandle.Clone(); // the clone owns a copy, so the handle can be destroyed
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    public void Dispose()
    {
        _balloonTimer.Stop();
        _balloonTimer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _robotIcon.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
